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
    private readonly ConcurrentQueue<GameCommand> commands = new();
    private readonly ConcurrentQueue<GameAction> actions = new();
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

    public GameModsClient(ModConfig config, IMonitor monitor)
    {
        this.config = config;
        this.monitor = monitor;
        this.connectionTask = Task.Run(() => this.RunConnectionLoopAsync(this.cancellation.Token));
    }

    public bool IsConnected => this.connected;
    public bool SpawnsEnabled => this.spawnsEnabled;
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

    public async Task UpdateSettingsAsync(HostSettings next)
    {
        await this.SendMessageAsync(new
        {
            type = "settings.update",
            base_revision = this.settingsRevision,
            settings = new
            {
                spawn_enabled = next.SpawnEnabled,
                difficulty = next.Difficulty,
                chat_responses = next.ChatResponses,
                debug_messages = next.DebugMessages
            }
        });
    }

    public async Task AcknowledgeActionAsync(string actionId, bool success, int removedCount, string? error = null)
    {
        await this.SendMessageAsync(new
        {
            type = "action.ack",
            action_id = actionId,
            success,
            removed_count = removedCount,
            error
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
            this.monitor.Log("Spectrum Game Mods is not connected. Add BroadcasterUserId and HostToken to the local mod config to pair this host.", LogLevel.Info);
            return;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(this.config.BroadcasterUserId, @"^\d{1,30}$"))
        {
            this.monitor.Log("Spectrum Game Mods connection disabled: BroadcasterUserId must be a numeric Twitch user ID.", LogLevel.Error);
            return;
        }

        while (!token.IsCancellationRequested)
        {
            ClientWebSocket? current = null;
            try
            {
                Uri gatewayUri = this.BuildGatewayUri();
                current = new ClientWebSocket();
                current.Options.SetRequestHeader("Authorization", "Bearer " + this.config.HostToken);

                this.monitor.Log("Connecting to Spectrum Game Mods…", LogLevel.Info);
                await current.ConnectAsync(gatewayUri, token);
                this.socket = current;
                this.connected = true;
                this.spawnsEnabled = false;
                this.monitor.Log("Connected to Spectrum Game Mods. Spawning remains paused until enabled in Control Center.", LogLevel.Info);

                await this.ReceiveLoopAsync(current, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
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

            if (!token.IsCancellationRequested)
            {
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
            "/connect/stardew/" + Uri.EscapeDataString(this.config.BroadcasterUserId);

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
                break;

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
                this.monitor.Log("Spectrum host session is ready.", LogLevel.Trace);
                return;
            }

            if (type == "settings.state")
            {
                if (root.TryGetProperty("settings", out JsonElement settingsElement) &&
                    settingsElement.ValueKind == JsonValueKind.Object)
                {
                    this.settings = new HostSettings(
                        ReadBool(settingsElement, "spawn_enabled", true),
                        ReadString(settingsElement, "difficulty") is "easy" or "hard" ? ReadString(settingsElement, "difficulty") : "normal",
                        ReadBool(settingsElement, "chat_responses", true),
                        ReadBool(settingsElement, "debug_messages", false)
                    );
                }
                this.settingsRevision = ReadInt(root, "revision", this.settingsRevision);
                this.spawnOverride = ReadString(root, "spawn_override") is "force_on" or "force_off" ? ReadString(root, "spawn_override") : "follow";
                this.safetyGateEnabled = ReadBool(root, "safety_gate_enabled", false);
                this.spawnsEnabled = ReadBool(root, "effective_spawn_enabled", false);
                this.monitor.Log(
                    $"Spectrum settings synced (revision {this.settingsRevision}); preference {(this.settings.SpawnEnabled ? "enabled" : "disabled")}; override {this.spawnOverride}; safety gate {(this.safetyGateEnabled ? "open" : "closed")}.",
                    this.settings.DebugMessages ? LogLevel.Info : LogLevel.Trace
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
                string game = ReadString(root, "game");
                string name = ReadString(root, "name");
                if (string.IsNullOrWhiteSpace(actionId) || game != "stardew-valley" ||
                    name != "stardew.clear_loaded_monsters")
                {
                    this.monitor.Log("Ignored an unsupported Spectrum game action.", LogLevel.Warn);
                    return;
                }
                this.actions.Enqueue(new GameAction(actionId, game, name));
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
