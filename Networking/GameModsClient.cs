using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;

namespace SpectrumStardewMod.Networking;

/// <summary>
/// Maintains the host's outbound connection to Spectrum Game Mods.
/// Network callbacks only validate and enqueue typed messages; game state is
/// always changed later by ModEntry on the SMAPI update thread.
/// </summary>
internal sealed class GameModsClient : IDisposable
{
    private const int MaxMessageBytes = 32 * 1024;
    private static readonly HashSet<string> SupportedBehaviorKeys = new(StringComparer.Ordinal)
    {
        "difficulty_random",
        "chat_monster",
        "random_count_random_monsters",
        "fixed_count_random_monsters",
        "random_count_specific_monster",
        "fixed_count_specific_monster"
    };
    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly string requestedSessionId;
    private readonly ConcurrentQueue<GameCommand> commands = new();
    private readonly ConcurrentQueue<GameAction> actions = new();
    private readonly ConcurrentQueue<PoolOverrideUpdate> poolOverrideUpdates = new();
    private readonly ConcurrentDictionary<string, byte> queuedActionIds = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> actionIdOrder = new();
    private readonly ConcurrentDictionary<string, byte> queuedCommandIds = new(StringComparer.Ordinal);
    private volatile HostSettings settings = new();
    private volatile int settingsRevision = 1;
    private volatile string spawnOverride = "follow";
    private volatile bool safetyGateEnabled;
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private ClientWebSocket? socket;
    private readonly Task connectionTask;
    private volatile bool connected;
    private volatile bool spawnsEnabled;
    private volatile bool accessRevoked;
    private volatile string connectionStatusMessage = "Waiting to connect to Spectrum Game Mods.";
    private volatile string activeSessionId = "";

    public GameModsClient(ModConfig config, IMonitor monitor, string sessionId)
    {
        this.config = config;
        this.monitor = monitor;
        this.requestedSessionId = System.Text.RegularExpressions.Regex.IsMatch(sessionId ?? "", @"^[a-fA-F0-9]{32}$")
            ? sessionId.ToLowerInvariant()
            : Guid.NewGuid().ToString("N");
        this.connectionTask = Task.Run(() => this.RunConnectionLoopAsync(this.cancellation.Token));
    }

    public bool IsConnected => this.connected;
    public string ActiveSessionId => this.activeSessionId;
    public bool SpawnsEnabled => this.spawnsEnabled;
    public string ConnectionStatusMessage => this.connectionStatusMessage;
    public string SpawnStateReason
    {
        get
        {
            if (!this.connected)
                return this.connectionStatusMessage;

            if (!this.safetyGateEnabled)
                return "Spawning paused: the in-game spawn control is paused.";

            if (this.spawnOverride == "force_off")
                return "Spawning paused: the administrator override is forcing it off.";

            if (this.spawnOverride == "follow" && !this.settings.SpawnEnabled)
                return "Spawning paused: your streamer preference is set to stopped.";

            return this.spawnsEnabled
                ? "Spawning is enabled."
                : "Spawning is paused.";
        }
    }
    public HostSettings Settings => this.settings;
    public int SettingsRevision => this.settingsRevision;
    public string SpawnOverride => this.spawnOverride;
    public bool SafetyGateEnabled => this.safetyGateEnabled;

    public bool TryDequeueCommand(out GameCommand? command)
    {
        if (!this.commands.TryDequeue(out command) || command is null)
            return false;
        this.queuedCommandIds.TryRemove(command.CommandId, out _);
        return true;
    }

    public void RequeueCommand(GameCommand command)
    {
        if (this.queuedCommandIds.TryAdd(command.CommandId, 0))
            this.commands.Enqueue(command);
    }

    public bool TryDequeueAction(out GameAction? action) => this.actions.TryDequeue(out action);

    public bool TryDequeuePoolOverrideUpdate(out PoolOverrideUpdate? update) =>
        this.poolOverrideUpdates.TryDequeue(out update);

    public Task SendPoolDiagnosticsAsync(object diagnostics)
    {
        string sessionId = this.activeSessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            return Task.CompletedTask;

        return this.SendMessageAsync(new
        {
            type = "pool.diagnostics",
            session_id = sessionId,
            diagnostics
        });
    }

    private void EnqueueAction(GameAction action)
    {
        if (!this.queuedActionIds.TryAdd(action.ActionId, 0))
            return;

        this.actions.Enqueue(action);
        this.actionIdOrder.Enqueue(action.ActionId);
        while (this.actionIdOrder.Count > 500 && this.actionIdOrder.TryDequeue(out string? expiredId))
            this.queuedActionIds.TryRemove(expiredId, out _);
    }

    public Task SetSpawnsEnabledAsync(bool enabled)
    {
        return this.SendMessageAsync(new
        {
            type = "spawns.set",
            enabled
        });
    }

    public Task SendMonsterSpawnedAsync(
        string commandId,
        string redeemer,
        IReadOnlyList<string> monsterNames,
        int spawnedCount,
        string location,
        bool isHorde)
    {
        if (string.IsNullOrWhiteSpace(commandId) || monsterNames.Count != spawnedCount ||
            spawnedCount is < 1 or > 10)
            return Task.CompletedTask;

        return this.SendMessageAsync(new
        {
            type = "game.event",
            event_id = commandId,
            game = "stardew-valley",
            name = "stardew.monster_spawned",
            occurred_at = DateTimeOffset.UtcNow.ToString("O"),
            payload = new
            {
                redeemer = string.IsNullOrWhiteSpace(redeemer) ? "Unknown" : redeemer,
                monster_names = monsterNames,
                spawned_count = spawnedCount,
                location = string.IsNullOrWhiteSpace(location) ? "Unknown" : location,
                is_horde = isHorde
            }
        });
    }

    public Task SendPlayerDiedAsync(int deathCount)
    {
        if (deathCount is < 1 or > 1000000)
            return Task.CompletedTask;

        return this.SendMessageAsync(new
        {
            type = "game.event",
            event_id = "death-" + Guid.NewGuid().ToString("N"),
            game = "stardew-valley",
            name = "stardew.player_died",
            occurred_at = DateTimeOffset.UtcNow.ToString("O"),
            payload = new { death_count = deathCount }
        });
    }

    public Task SendChallengeStateAsync(string challengeName, int deathCount)
    {
        string safeName = string.IsNullOrWhiteSpace(challengeName)
            ? "Spectrum Challenge"
            : challengeName.Trim();
        if (safeName.Length > 80)
            safeName = safeName[..80];

        return this.SendMessageAsync(new
        {
            type = "game.event",
            event_id = "challenge-" + Guid.NewGuid().ToString("N"),
            game = "stardew-valley",
            name = "stardew.challenge_state",
            occurred_at = DateTimeOffset.UtcNow.ToString("O"),
            payload = new
            {
                challenge_name = safeName,
                death_count = Math.Clamp(deathCount, 0, 1000000)
            }
        });
    }

    public async Task AcknowledgeActionAsync(string actionId, bool success, int removedCount, string? error = null, string? detail = null)
    {
        await this.SendMessageAsync(new
        {
            type = "action.ack",
            action_id = actionId,
            success,
            removed_count = removedCount,
            error,
            detail
        });
    }

    private async Task SendMessageAsync(object message)
    {
        ClientWebSocket? current = this.socket;
        if (current is null || current.State != WebSocketState.Open) return;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
        try
        {
            await this.sendLock.WaitAsync(this.cancellation.Token);
            try
            {
                current = this.socket;
                if (current is null || current.State != WebSocketState.Open) return;
                await current.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, this.cancellation.Token);
            }
            finally { this.sendLock.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            this.monitor.Log($"Could not send a Spectrum host update yet: {ex.Message}", LogLevel.Trace);
        }
    }

    public async Task AcknowledgeAsync(string commandId)
    {
        ClientWebSocket? current = this.socket;
        if (current is null || current.State != WebSocketState.Open)
            return;

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "command.ack",
            command_id = commandId
        });

        try
        {
            await this.sendLock.WaitAsync(this.cancellation.Token);
            try
            {
                current = this.socket;
                if (current is null || current.State != WebSocketState.Open)
                    return;

                await current.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    this.cancellation.Token
                );
            }
            finally
            {
                this.sendLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        catch (WebSocketException ex)
        {
            this.monitor.Log($"Could not acknowledge a Spectrum command yet: {ex.Message}", LogLevel.Trace);
        }
    }

    private async Task RunConnectionLoopAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(this.config.BroadcasterUserId) ||
            string.IsNullOrWhiteSpace(this.config.HostToken))
        {
            this.connectionStatusMessage = "This host is not paired. Choose Connect Twitch account to start pairing.";
            this.monitor.Log("Spectrum Game Mods is not paired. Use the in-game Connect Twitch account button.", LogLevel.Info);
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(this.config.BroadcasterUserId, @"^\d{1,30}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(this.config.HostToken, @"^[a-fA-F0-9]{64}$"))
        {
            this.connectionStatusMessage = "Saved host credentials are invalid. Connect Twitch account to pair this host again.";
            this.monitor.Log("Spectrum Game Mods connection disabled because the saved host credentials are invalid.", LogLevel.Error);
            return;
        }

        while (!token.IsCancellationRequested && !this.accessRevoked)
        {
            ClientWebSocket? current = null;
            try
            {
                this.connectionStatusMessage = "Connecting to the Spectrum gateway…";
                Uri gatewayUri = this.BuildGatewayUri();
                current = new ClientWebSocket();
                current.Options.SetRequestHeader("Authorization", "Bearer " + this.config.HostToken);

                this.monitor.Log("Connecting to Spectrum Game Mods…", LogLevel.Info);
                await current.ConnectAsync(gatewayUri, token);
                this.socket = current;
                this.connected = true;
                this.spawnsEnabled = false;
                this.connectionStatusMessage = "Connected to Spectrum Game Mods.";
                this.monitor.Log("Connected to Spectrum Game Mods. Spawning remains paused until enabled from the in-game menu.", LogLevel.Info);
                await this.SendChallengeStateAsync(this.config.ChallengeName, this.config.ChallengeDeathCount);

                await this.ReceiveLoopAsync(current, token);
                if (current.CloseStatusDescription?.Contains("Host access revoked", StringComparison.OrdinalIgnoreCase) == true)
                {
                    this.accessRevoked = true;
                    this.connectionStatusMessage = "Host access was revoked in Control Center. Connect Twitch account to authorise this host again.";
                }
                else if (current.CloseStatusDescription?.Contains("Host token rotated", StringComparison.OrdinalIgnoreCase) == true)
                {
                    this.accessRevoked = true;
                    this.connectionStatusMessage = "Host credentials changed. Connect Twitch account to pair this game host again.";
                }
                else if (!token.IsCancellationRequested)
                {
                    this.connectionStatusMessage = "The gateway connection closed. Reconnecting automatically…";
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this.connectionStatusMessage = "The Spectrum gateway is unavailable or rejected the connection. Retrying automatically; use Connect Twitch account if this continues.";
                this.monitor.Log($"Spectrum Game Mods connection interrupted: {ex.Message}", LogLevel.Warn);
            }
            finally
            {
                this.connected = false;
                this.spawnsEnabled = false;
                if (ReferenceEquals(this.socket, current))
                    this.socket = null;

                try { current?.Abort(); } catch { }
                current?.Dispose();
            }

            if (!token.IsCancellationRequested && !this.accessRevoked)
            {
                this.connectionStatusMessage = "Gateway unavailable. Retrying automatically in a few seconds…";
                try { await Task.Delay(TimeSpan.FromSeconds(8), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private Uri BuildGatewayUri()
    {
        string baseUrl = this.config.GatewayUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("GatewayUrl must be an HTTPS URL.");
        }

        string websocketUrl = "wss://" + baseUri.Authority +
            baseUri.AbsolutePath.TrimEnd('/') +
            "/connect/stardew/" + Uri.EscapeDataString(this.config.BroadcasterUserId) +
            "?session_id=" + Uri.EscapeDataString(this.requestedSessionId);

        return new Uri(websocketUrl);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket current, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        using MemoryStream message = new();

        while (!token.IsCancellationRequested && current.State == WebSocketState.Open)
        {
            ValueWebSocketReceiveResult result = await current.ReceiveAsync(
                new Memory<byte>(buffer),
                token
            );

            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (string.IsNullOrWhiteSpace(current.CloseStatusDescription))
                    this.connectionStatusMessage = "The gateway closed the connection. Reconnecting automatically…";
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            if (message.Length + result.Count > MaxMessageBytes)
                throw new InvalidOperationException("Spectrum sent a message larger than the allowed limit.");

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
                continue;

            this.HandleServerMessage(message.ToArray());
            message.SetLength(0);
        }
    }

    private void HandleServerMessage(byte[] bytes)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            string type = ReadString(root, "type");

            if (type == "session.ready")
            {
                this.activeSessionId = ReadString(root, "session_id");
                this.monitor.Log("Spectrum host session is ready.", LogLevel.Trace);
                return;
            }

            if (type == "spawn.pools.state")
            {
                string sessionId = ReadString(root, "session_id");
                if (string.IsNullOrWhiteSpace(sessionId) || sessionId != this.activeSessionId)
                    return;

                if (!root.TryGetProperty("overrides", out JsonElement overridesElement) ||
                    overridesElement.ValueKind != JsonValueKind.Object)
                    return;

                var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in overridesElement.EnumerateObject())
                {
                    string mode = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : "";
                    if (mode is "automatic" or "force_enabled" or "force_disabled")
                        overrides[property.Name] = mode;
                }

                this.poolOverrideUpdates.Enqueue(new PoolOverrideUpdate(
                    sessionId,
                    ReadInt(root, "revision", 1),
                    overrides
                ));
                return;
            }

            if (type == "overlay.state.request")
            {
                _ = this.SendChallengeStateAsync(this.config.ChallengeName, this.config.ChallengeDeathCount);
                return;
            }

            if (type == "settings.state")
            {
                if (root.TryGetProperty("settings", out JsonElement settingsElement) &&
                    settingsElement.ValueKind == JsonValueKind.Object)
                {
                    this.settings = new HostSettings(
                        ReadBool(settingsElement, "spawn_enabled", true),
                        ReadString(settingsElement, "difficulty") is "easy" or "hard" ? ReadString(settingsElement, "difficulty") : "normal"
                    );
                }
                this.settingsRevision = ReadInt(root, "revision", this.settingsRevision);
                this.spawnOverride = ReadString(root, "spawn_override") is "force_on" or "force_off" ? ReadString(root, "spawn_override") : "follow";
                this.safetyGateEnabled = ReadBool(root, "safety_gate_enabled", false);
                this.spawnsEnabled = ReadBool(root, "effective_spawn_enabled", false);
                this.monitor.Log(
                    $"Spectrum settings synced (revision {this.settingsRevision}); preference {(this.settings.SpawnEnabled ? "enabled" : "disabled")}; override {this.spawnOverride}; safety gate {(this.safetyGateEnabled ? "open" : "closed")}.",
                    LogLevel.Info
                );
                return;
            }

            if (type == "spawns.state")
            {
                this.spawnsEnabled = root.TryGetProperty("enabled", out JsonElement enabled) &&
                    enabled.ValueKind == JsonValueKind.True;
                return;
            }

            if (type == "game.action")
            {
                string actionId = ReadString(root, "action_id");
                string actionGame = ReadString(root, "game");
                string actionName = ReadString(root, "name");
                if (string.IsNullOrWhiteSpace(actionId) || actionGame != "stardew-valley" ||
                    actionName is not ("stardew.clear_loaded_monsters" or "stardew.spawn_random_test" or "stardew.community_single" or "stardew.community_horde" or "stardew.community_mega_horde"))
                {
                    this.monitor.Log("Ignored an unsupported Spectrum game action.", LogLevel.Warn);
                    return;
                }
                string actionDifficulty = ReadString(root, "difficulty");
                if (actionDifficulty is not ("easy" or "normal" or "hard"))
                    actionDifficulty = "normal";
                string outcome = ReadString(root, "outcome");
                if ((actionName == "stardew.community_single" && outcome != "single") ||
                    (actionName == "stardew.community_horde" && outcome != "horde") ||
                    (actionName == "stardew.community_mega_horde" && outcome != "mega"))
                {
                    this.monitor.Log("Ignored a community action with an invalid outcome.", LogLevel.Warn);
                    return;
                }
                int monsterCount = root.TryGetProperty("monster_count", out JsonElement countElement) && countElement.TryGetInt32(out int parsedCount)
                    ? Math.Clamp(parsedCount, 1, 50)
                    : (actionName == "stardew.community_mega_horde" ? 20 : 10);
                string donorName = ReadString(root, "donor_name");
                string charityName = ReadString(root, "charity_name");
                bool announce = !root.TryGetProperty("announce", out JsonElement announceElement) ||
                    announceElement.ValueKind != JsonValueKind.False;
                this.EnqueueAction(new GameAction(
                    actionId, actionGame, actionName, actionDifficulty, outcome, monsterCount,
                    donorName, charityName, announce
                ));
                return;
            }

            if (type != "game.command")
                return;

            string commandId = ReadString(root, "command_id");
            string game = ReadString(root, "game");
            string name = ReadString(root, "name");

            if (string.IsNullOrWhiteSpace(commandId) ||
                game != "stardew-valley" ||
                name != "stardew.spawn_monster")
            {
                this.monitor.Log("Ignored an unsupported Spectrum game command.", LogLevel.Warn);
                return;
            }

            if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object)
            {
                this.monitor.Log("Ignored a Spectrum game command without a typed payload.", LogLevel.Warn);
                return;
            }

            string behaviorKey = ReadString(payload, "behavior_key");
            if (!SupportedBehaviorKeys.Contains(behaviorKey))
            {
                this.monitor.Log("Ignored a Spectrum command with an unsupported spawn behaviour.", LogLevel.Warn);
                return;
            }

            GameCommand command = new(
                commandId, game, name,
                ReadString(payload, "user_name"),
                ReadString(payload, "reward_title"),
                ReadString(payload, "redeemed_at"),
                behaviorKey,
                ReadString(payload, "user_input"),
                ReadString(payload, "monster_type"),
                ReadString(payload, "difficulty"),
                ReadInt(payload, "count", 1),
                ReadInt(payload, "min_count", 1),
                ReadInt(payload, "max_count", 3)
            );
            if (this.queuedCommandIds.TryAdd(commandId, 0))
                this.commands.Enqueue(command);
        }
        catch (JsonException ex)
        {
            this.monitor.Log($"Ignored an invalid Spectrum message: {ex.Message}", LogLevel.Warn);
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
            value.TryGetInt32(out int result)
                ? result
                : fallback;
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    public void Dispose()
    {
        this.cancellation.Cancel();
        try { this.socket?.Abort(); } catch { }
        this.socket?.Dispose();
        this.sendLock.Dispose();
        this.cancellation.Dispose();
    }
}
