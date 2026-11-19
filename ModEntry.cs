using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SpectrumStardewMod.Gameplay;
using SpectrumStardewMod.Networking;
using SpectrumStardewMod.UI;

namespace SpectrumStardewMod;

/// <summary>The SMAPI entry point for Spectrum Stardew Mod.</summary>
internal sealed class ModEntry : Mod
{
    private MonsterSpawner? monsterSpawner;
    private GameModsClient? gameModsClient;
    private ModConfig? config;
    private readonly HashSet<string> processedCommandIds = new(StringComparer.Ordinal);
    private readonly Queue<string> processedCommandOrder = new();
    private const string ProcessedCommandGlobalKey = "processed-spectrum-command-ids";
    private bool processedCommandsLoaded;
    private DateTime retryCommandsAtUtc = DateTime.MinValue;
    private GenericModConfigMenu.IGenericModConfigMenuApi? configMenuApi;
    private Rectangle spectrumMenuButtonBounds;
    private readonly ConcurrentQueue<PairingResult> pairingResults = new();
    private readonly Queue<SpawnWorkItem> pendingSpawnCommands = new();
    private readonly HashSet<string> queuedSpawnCommandIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingForwardedCommand> forwardedCommands = new(StringComparer.Ordinal);
    private CancellationTokenSource? pairingCancellation;
    private volatile bool pairingInProgress;
    private volatile string pairingStatus = "";
    private volatile string pairingVerificationUri = "";

    public override void Entry(IModHelper helper)
    {
        this.monsterSpawner = new MonsterSpawner(this.Monitor);

        this.LoadProcessedCommands(helper);

        this.config = helper.ReadConfig<ModConfig>();
        if (this.config.Normalize(this.Monitor))
            helper.WriteConfig(this.config);

        if (string.IsNullOrWhiteSpace(this.config.BroadcasterUserId) ||
            string.IsNullOrWhiteSpace(this.config.HostToken))
        {
            this.Monitor.Log("Spectrum Game Mods connection is not paired yet. Local test commands remain available.", LogLevel.Info);
        }

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;\n        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;

        helper.ConsoleCommands.Add(
            "spectrum_spawn_test",
            "Spawn a test Green Slime near the host player. Usage: spectrum_spawn_test",
            this.OnSpawnTestCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_spawn_random_test",
            "Spawn a weighted-random vanilla monster near the host player. Usage: spectrum_spawn_random_test",
            this.OnSpawnRandomTestCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_spawn_monster_test",
            "Spawn one progression-eligible catalogue monster by name or ID. Usage: spectrum_spawn_monster_test <monster name or ID>",
            this.OnSpawnMonsterTestCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_spawn_monster_force_test",
            "Developer test: spawn a catalogue monster while bypassing progression only. Exclusions remain enforced. Usage: spectrum_spawn_monster_force_test <monster name or ID>",
            this.OnSpawnMonsterForceTestCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_progression_test",
            "Apply a temporary in-memory progression profile or reset to the real save. Usage: spectrum_progression_test <early|mines|skull100|danger-mines|quarry|skull-cavern|danger-skull|bug-lair|volcano|combat-0|combat-5|combat-10|shrine-0|shrine-1|shrine-2|all|reset>",
            this.OnProgressionTestCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_monster_catalog_test",
            "Run every supported monster constructor and special setup without adding monsters to the world. Usage: spectrum_monster_catalog_test",
            this.OnMonsterCatalogTestCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_connection_status",
            "Show the Spectrum Game Mods connection status. Usage: spectrum_connection_status",
            this.OnConnectionStatusCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_monster_pool_status",
            "Show progression, area difficulty, threat-data coverage, selectable variants, and catalogue validation.",
            this.OnMonsterPoolStatusCommand
        );

        this.Monitor.Log("Spectrum Stardew Mod loaded. Use 'spectrum_connection_status' to check the gateway connection.", LogLevel.Info);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (this.config is null ||
            string.IsNullOrWhiteSpace(this.config.BroadcasterUserId) ||
            string.IsNullOrWhiteSpace(this.config.HostToken))
        {
            return;
        }

        this.gameModsClient?.Dispose();
        this.gameModsClient = new GameModsClient(this.config, this.Monitor);
        this.Monitor.Log("Starting this player’s Spectrum connection for the loaded Stardew save.", LogLevel.Info);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.gameModsClient?.Dispose();
        this.gameModsClient = null;
        this.pairingCancellation?.Cancel();
        this.pairingCancellation?.Dispose();
        this.pairingCancellation = null;
        this.pairingResults.Clear();
        this.pairingInProgress = false;
        this.pairingStatus = "";
        this.pairingVerificationUri = "";
        this.retryCommandsAtUtc = DateTime.MinValue;
        this.pendingSpawnCommands.Clear();
        this.queuedSpawnCommandIds.Clear();
        this.forwardedCommands.Clear();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.configMenuApi = this.Helper.ModRegistry.GetApi<GenericModConfigMenu.IGenericModConfigMenuApi>(
            "spacechase0.GenericModConfigMenu"
        );

        if (this.configMenuApi is null)
        {
            this.Monitor.Log(
                "Generic Mod Config Menu is not installed. Install it to access Spectrum controls from the vanilla Options menu.",
                LogLevel.Warn
            );
            return;
        }

        this.configMenuApi.Register(
            mod: this.ModManifest,
            reset: () => { },
            save: () => { },
            titleScreenOnly: false
        );
        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Spectrum Stardew Monster Spawner");
        this.configMenuApi.AddParagraph(
            this.ModManifest,
            () => "Open the Spectrum control panel to connect Twitch, manage spawns, change settings, or clear loaded monsters."
        );
        this.configMenuApi.AddComplexOptionWithGamepadSupport(
            mod: this.ModManifest,
            name: () => "Spectrum controls",
            draw: (batch, position) =>
            {
                this.spectrumMenuButtonBounds = new Rectangle(
                    (int)position.X,
                    (int)position.Y,
                    520,
                    64
                );
                batch.Draw(Game1.staminaRect, this.spectrumMenuButtonBounds, new Color(67, 46, 98, 235));
                const string label = "Open Spectrum Control Panel";
                Vector2 size = Game1.smallFont.MeasureString(label);
                Vector2 textPosition = new(
                    this.spectrumMenuButtonBounds.X + (this.spectrumMenuButtonBounds.Width - size.X) / 2f,
                    this.spectrumMenuButtonBounds.Y + (this.spectrumMenuButtonBounds.Height - size.Y) / 2f
                );
                batch.DrawString(Game1.smallFont, label, textPosition, Color.White);
            },
            tooltip: () => "Open the full in-game Spectrum controls.",
            height: () => 76,
            fieldId: "spectrum-open-controls"
        );
        this.Monitor.Log("Spectrum controls registered in Generic Mod Config Menu.", LogLevel.Info);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.Button != SButton.MouseLeft || this.configMenuApi is null)
            return;

        if (!this.configMenuApi.TryGetCurrentMenu(out IManifest? currentMod, out _) ||
            currentMod?.UniqueID != this.ModManifest.UniqueID)
        {
            return;
        }

        Vector2 cursor = e.Cursor.GetScaledScreenPixels();
        if (!this.spectrumMenuButtonBounds.Contains((int)cursor.X, (int)cursor.Y))
            return;

        this.Helper.Input.Suppress(e.Button);
        this.OpenSpectrumMenu();
    }

    private void OpenSpectrumMenu()
    {
        Game1.activeClickableMenu = new SpectrumMenu(
            () => this.gameModsClient?.IsConnected == true,
            () => Context.IsMainPlayer,
            () => this.gameModsClient?.Settings ?? new HostSettings(),
            () => this.gameModsClient?.SpawnOverride ?? "follow",
            () => this.gameModsClient?.SafetyGateEnabled == true,
            () => this.gameModsClient?.SpawnStateReason ?? "Connect this host to your Spectrum Twitch account.",
            settings =>
            {
                if (this.gameModsClient is not null)
                    _ = this.gameModsClient.UpdateSettingsAsync(settings);
            },
            () =>
            {
                int removed = this.monsterSpawner?.ClearLoadedMonsters() ?? -1;
                if (removed >= 0)
                {
                    Game1.chatBox?.addInfoMessage($"Spectrum cleared {removed} loaded monster(s).");
                }
            },
            () => this.pairingStatus,
            this.StartPairingOrOpenControlCenter
        );
    }

    private void StartPairingOrOpenControlCenter()
    {
        if (this.gameModsClient?.IsConnected == true ||
            this.pairingStatus.StartsWith("Twitch account authorised", StringComparison.OrdinalIgnoreCase))
        {
            this.OpenControlCenter();
            return;
        }

        if (this.pairingInProgress)
        {
            this.OpenPairingApprovalPage();
            return;
        }

        this.config ??= new ModConfig();
        this.pairingCancellation?.Cancel();
        this.pairingCancellation?.Dispose();
        this.pairingCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = this.pairingCancellation.Token;
        this.pairingInProgress = true;
        this.pairingStatus = "Requesting a secure pairing code…";
        this.pairingVerificationUri = "";

        _ = Task.Run(async () =>
        {
            using var pairingClient = new StardewPairingClient();
            try
            {
                PairingStart pairing = await pairingClient.StartAsync(this.config.GatewayUrl, cancellationToken);
                this.pairingVerificationUri = pairing.VerificationUri;
                this.pairingStatus = $"Waiting for approval · Code {pairing.UserCode}";
                this.OpenPairingApprovalPage();
                PairingResult result = await pairingClient.WaitForApprovalAsync(this.config.GatewayUrl, pairing, cancellationToken);
                this.pairingResults.Enqueue(result);
            }
            catch (OperationCanceledException)
            {
                this.pairingStatus = "Pairing cancelled.";
            }
            catch (Exception ex)
            {
                this.pairingVerificationUri = "";
                this.pairingStatus = "Pairing failed · " + ex.Message;
                this.Monitor.Log("Spectrum device pairing failed: " + ex.Message, LogLevel.Warn);
            }
            finally
            {
                this.pairingInProgress = false;
            }
        });
    }

    private void OpenPairingApprovalPage()
    {
        string url = this.pairingVerificationUri;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not open the Spectrum pairing page: {ex.Message}", LogLevel.Warn);
            this.pairingStatus = "Open Control Center manually · " + this.pairingStatus;
        }
    }

    private void OpenControlCenter()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://spectrum-control-center.rydrakeandlonk.workers.dev/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not open Spectrum Control Center: {ex.Message}", LogLevel.Warn);
            Game1.chatBox?.addInfoMessage("Open Spectrum Control Center in your browser to pair this host.");
        }
    }

    private void LoadProcessedCommands(IModHelper helper)
    {
        List<string> savedIds = helper.Data.ReadGlobalData<List<string>>(ProcessedCommandGlobalKey) ?? new List<string>();
        foreach (string id in savedIds.Where(id => !string.IsNullOrWhiteSpace(id)).TakeLast(500))
        {
            if (this.processedCommandIds.Add(id))
                this.processedCommandOrder.Enqueue(id);
        }

        this.processedCommandsLoaded = true;
        this.Monitor.Log($"Loaded {this.processedCommandIds.Count} saved Spectrum redemption IDs for duplicate protection.", LogLevel.Trace);
    }

    private bool RememberProcessedCommand(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return false;

        if (this.processedCommandIds.Add(commandId))
            this.processedCommandOrder.Enqueue(commandId);

        while (this.processedCommandOrder.Count > 500)
            this.processedCommandIds.Remove(this.processedCommandOrder.Dequeue());

        return this.TryPersistProcessedCommands();
    }

    private bool TryPersistProcessedCommands()
    {
        try
        {
            this.Helper.Data.WriteGlobalData(ProcessedCommandGlobalKey, this.processedCommandOrder.ToList());
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not persist processed Spectrum redemption IDs; acknowledgement is being held for retry: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Context.IsWorldReady)
        {
            while (this.pairingResults.TryDequeue(out PairingResult? pairingResult) && pairingResult is not null)
            {
                this.config ??= new ModConfig();
                this.config.BroadcasterUserId = pairingResult.BroadcasterUserId;
                this.config.HostToken = pairingResult.HostToken;
                this.config.ConfigVersion = ModConfig.CurrentConfigVersion;
                this.Helper.WriteConfig(this.config);
                this.gameModsClient?.Dispose();
                this.gameModsClient = new GameModsClient(this.config, this.Monitor);
                this.pairingStatus = "Twitch account authorised · connecting…";
                Game1.chatBox?.addInfoMessage("Your Spectrum Twitch connection is authorised. Spawning remains paused until Control Center opens its safety gate.");
            }
        }

        if (this.gameModsClient?.IsConnected == true &&
            this.pairingStatus.StartsWith("Twitch account authorised", StringComparison.OrdinalIgnoreCase))
        {
            this.pairingStatus = "";
            this.pairingVerificationUri = "";
        }

        if (!Context.IsWorldReady || this.monsterSpawner is null)
            return;

        // Every player consumes only the commands delivered to their own Twitch connection.
        // Farmhands forward those typed commands to the host through SMAPI messaging.
        if (this.gameModsClient is not null)
        {
            if (Context.IsMainPlayer)
            {
                if (this.gameModsClient.SpawnsEnabled &&
                    this.gameModsClient.TryDequeueCommand(out GameCommand? localCommand) &&
                    localCommand is not null)
                {
                    this.QueueSpawnCommand(localCommand, null, this.gameModsClient.Settings.ChatResponses);
                }
            }
            else
            {
                this.ForwardFarmhandCommands();
            }
        }

        // Only the host mutates the shared Stardew world.
        if (!Context.IsMainPlayer)
            return;

        if (this.gameModsClient is not null)
        {
            // One-shot world actions are independent of the spawn safety gate.
            while (this.gameModsClient.TryDequeueAction(out GameAction? action) && action is not null)
            {
                if (action.Name != "stardew.clear_loaded_monsters")
                {
                    _ = this.gameModsClient.AcknowledgeActionAsync(action.ActionId, false, 0, "Unsupported game action.");
                    continue;
                }

                int removed = this.monsterSpawner.ClearLoadedMonsters();
                bool succeeded = removed >= 0;
                if (this.gameModsClient.Settings.DebugMessages)
                    this.Monitor.Log($"[Spectrum] REMOTE ACTION | clear loaded monsters | {(succeeded ? $"removed {removed}" : "failed")}", succeeded ? LogLevel.Info : LogLevel.Warn);
                _ = this.gameModsClient.AcknowledgeActionAsync(
                    action.ActionId,
                    succeeded,
                    Math.Max(0, removed),
                    succeeded ? null : "The host world was not ready."
                );
            }
        }

        if (this.pendingSpawnCommands.Count == 0 || DateTime.UtcNow < this.retryCommandsAtUtc)
            return;

        SpawnWorkItem work = this.pendingSpawnCommands.Dequeue();
        GameCommand command = work.Command;

        // A replay after a successful spawn is acknowledged without another world mutation.
        if (this.processedCommandIds.Contains(command.CommandId))
        {
            this.queuedSpawnCommandIds.Remove(command.CommandId);
            this.CompleteSpawnCommand(work, true, "Already processed.");
            return;
        }

        Farmer? targetFarmer = work.ReplyToPlayerId is long playerId
            ? Game1.getOnlineFarmers().FirstOrDefault(farmer => farmer.UniqueMultiplayerID == playerId)
            : Game1.player;

        if (targetFarmer is null)
        {
            this.queuedSpawnCommandIds.Remove(command.CommandId);
            this.CompleteSpawnCommand(work, false, "The redeeming farmer is not currently connected to this farm.");
            this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
            return;
        }

        this.Monitor.Log(
            $"[Spectrum] REDEEM | {DisplayValue(command.TwitchUserName)} | {DisplayValue(command.RewardTitle)} | {FormatRedeemedAt(command.RedeemedAt)} | {command.CommandId} | target {targetFarmer.Name}",
            LogLevel.Info
        );

        bool spawned = this.monsterSpawner.TryExecute(
            command,
            targetFarmer,
            out int spawnedCount,
            out int requestedCount,
            out List<string> spawnedTypes,
            out string locationName,
            out string result
        );

        string monsterSummary = string.Join(", ", spawnedTypes
            .GroupBy(type => type)
            .Select(group =>
            {
                string name = MonsterCatalog.GetDisplayName(group.Key);
                return group.Count() > 1 ? $"{name} x{group.Count()}" : name;
            }));

        this.Monitor.Log(
            $"[Spectrum] SPAWN | {(string.IsNullOrWhiteSpace(monsterSummary) ? "none" : monsterSummary)} | {(string.IsNullOrWhiteSpace(locationName) ? "n/a" : locationName)} | {spawnedCount}/{requestedCount} | target {targetFarmer.Name} | {result}",
            spawned && result == "SUCCESS" ? LogLevel.Info : LogLevel.Warn
        );

        if (spawned && result == "SUCCESS" && spawnedCount == requestedCount)
        {
            if (!this.RememberProcessedCommand(command.CommandId))
            {
                this.pendingSpawnCommands.Enqueue(work);
                this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
                this.Monitor.Log($"[Spectrum] RETRY | {command.CommandId} | Could not persist duplicate protection; acknowledgement held.", LogLevel.Error);
                return;
            }

            this.queuedSpawnCommandIds.Remove(command.CommandId);
            if (work.ChatResponses)
            {
                string redeemer = DisplayValue(command.TwitchUserName);
                string spawnDescription = spawnedCount == 1 ? $"a {monsterSummary}" : monsterSummary;
                Game1.chatBox?.addInfoMessage($"{redeemer} spawned {spawnDescription} near {targetFarmer.Name} in {locationName}!");
            }

            this.CompleteSpawnCommand(work, true, null);
        }
        else
        {
            // Keep the source command pending. The host retries placement without
            // asking every farmhand to maintain a second gameplay queue.
            this.pendingSpawnCommands.Enqueue(work);
            this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
            this.Monitor.Log($"[Spectrum] RETRY | {command.CommandId} | Spawn was not completed; retrying in 10 seconds.", LogLevel.Warn);
        }
    }

    private void ForwardFarmhandCommands()
    {
        if (this.gameModsClient is null || !this.gameModsClient.SpawnsEnabled ||
            Game1.MasterPlayer is null || DateTime.UtcNow < this.retryCommandsAtUtc)
            return;

        // Keep one in-flight relay per farmhand. The host replies only after the
        // spawn is complete, so the gateway command isn't acknowledged early.
        if (this.forwardedCommands.Count == 0 &&
            this.gameModsClient.TryDequeueCommand(out GameCommand? command) &&
            command is not null)
        {
            if (this.processedCommandIds.Contains(command.CommandId))
            {
                _ = this.gameModsClient.AcknowledgeAsync(command.CommandId);
                return;
            }

            this.forwardedCommands[command.CommandId] = new PendingForwardedCommand(command, DateTime.MinValue);
        }

        foreach (string commandId in this.forwardedCommands.Keys.ToList())
        {
            PendingForwardedCommand pending = this.forwardedCommands[commandId];
            if (pending.LastSentAtUtc != DateTime.MinValue &&
                DateTime.UtcNow - pending.LastSentAtUtc < TimeSpan.FromSeconds(10))
                continue;

            ForwardedSpawnCommand message = ForwardedSpawnCommand.FromCommand(
                pending.Command,
                this.config?.BroadcasterUserId ?? "",
                this.gameModsClient.Settings.ChatResponses
            );
            this.Helper.Multiplayer.SendMessage(
                message,
                "spectrum.spawn.forward",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
            this.forwardedCommands[commandId] = pending with { LastSentAtUtc = DateTime.UtcNow };
        }
    }

    private void QueueSpawnCommand(GameCommand command, long? replyToPlayerId, bool chatResponses)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId) || this.processedCommandIds.Contains(command.CommandId))
        {
            if (!string.IsNullOrWhiteSpace(command.CommandId))
                this.CompleteSpawnCommand(new SpawnWorkItem(command, replyToPlayerId, chatResponses), true, "Already processed.");
            return;
        }

        if (this.queuedSpawnCommandIds.Add(command.CommandId))
            this.pendingSpawnCommands.Enqueue(new SpawnWorkItem(command, replyToPlayerId, chatResponses));
    }

    private void CompleteSpawnCommand(SpawnWorkItem work, bool success, string? error)
    {
        if (work.ReplyToPlayerId is long playerId)
        {
            this.Helper.Multiplayer.SendMessage(
                new SpawnCommandResult(work.Command.CommandId, success, error ?? ""),
                "spectrum.spawn.result",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: new[] { playerId }
            );
        }
        else if (success)
        {
            _ = this.gameModsClient?.AcknowledgeAsync(work.Command.CommandId);
        }
        else
        {
            this.gameModsClient?.RequeueCommand(work.Command);
        }
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.ModManifest.UniqueID || !Context.IsWorldReady)
            return;

        if (e.Type == "spectrum.spawn.forward" && Context.IsMainPlayer)
        {
            ForwardedSpawnCommand message;
            try { message = e.ReadAs<ForwardedSpawnCommand>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid forwarded Spectrum command: {ex.Message}", LogLevel.Warn);
                return;
            }

            IMultiplayerPeer? peer = this.Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID);
            bool hasMod = peer?.GetMod(this.ModManifest.UniqueID) is not null;
            bool farmerOnline = Game1.getOnlineFarmers().Any(farmer => farmer.UniqueMultiplayerID == e.FromPlayerID);
            if (!hasMod || !farmerOnline ||
                string.IsNullOrWhiteSpace(message.CommandId) ||
                message.Game != "stardew-valley" ||
                message.Name != "stardew.spawn_monster" ||
                !SupportedForwardedBehaviors.Contains(message.BehaviorKey) ||
                !System.Text.RegularExpressions.Regex.IsMatch(message.SourceBroadcasterUserId ?? "", @"^\d{1,30}$"))
            {
                this.Monitor.Log($"Rejected forwarded Spectrum command from multiplayer peer {e.FromPlayerID}.", LogLevel.Warn);
                return;
            }

            GameCommand command = message.ToGameCommand();
            if (this.processedCommandIds.Contains(command.CommandId))
            {
                this.CompleteSpawnCommand(new SpawnWorkItem(command, e.FromPlayerID, message.ChatResponses), true, "Already processed.");
                return;
            }

            this.QueueSpawnCommand(command, e.FromPlayerID, message.ChatResponses);
            return;
        }

        if (e.Type == "spectrum.spawn.result" && !Context.IsMainPlayer &&
            e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            SpawnCommandResult result;
            try { result = e.ReadAs<SpawnCommandResult>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid host spawn result: {ex.Message}", LogLevel.Warn);
                return;
            }

            if (!this.forwardedCommands.TryGetValue(result.CommandId, out PendingForwardedCommand? pending))
                return;

            if (result.Success)
            {
                if (!this.RememberProcessedCommand(result.CommandId))
                {
                    this.Monitor.Log($"Could not persist completed redemption {result.CommandId}; waiting for the host's duplicate-safe confirmation.", LogLevel.Error);
                    this.forwardedCommands[result.CommandId] = pending with { LastSentAtUtc = DateTime.MinValue };
                    return;
                }

                this.forwardedCommands.Remove(result.CommandId);
                _ = this.gameModsClient?.AcknowledgeAsync(result.CommandId);
                this.Monitor.Log($"[Spectrum] MULTIPLAYER SPAWN CONFIRMED | {result.CommandId}", LogLevel.Info);
            }
            else
            {
                this.forwardedCommands.Remove(result.CommandId);
                this.gameModsClient?.RequeueCommand(pending.Command);
                this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
                this.Monitor.Log($"[Spectrum] MULTIPLAYER SPAWN RETRY | {result.CommandId} | {result.Error}", LogLevel.Warn);
            }
        }
    }

    private static readonly HashSet<string> SupportedForwardedBehaviors = new(StringComparer.Ordinal)
    {
        "difficulty_random",
        "chat_monster",
        "random_count_random_monsters",
        "fixed_count_random_monsters",
        "random_count_specific_monster",
        "fixed_count_specific_monster"
    };

    private sealed record SpawnWorkItem(GameCommand Command, long? ReplyToPlayerId, bool ChatResponses);
    private sealed record PendingForwardedCommand(GameCommand Command, DateTime LastSentAtUtc);

    private static string DisplayValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }

    private static string FormatRedeemedAt(string value)
    {
        return DateTimeOffset.TryParse(value, out DateTimeOffset redeemedAt)
            ? redeemedAt.ToLocalTime().ToString("HH:mm:ss")
            : DateTimeOffset.Now.ToString("HH:mm:ss");
    }

    private void OnSpawnTestCommand(string command, string[] args)
    {
        this.monsterSpawner?.TrySpawnGreenSlime();
    }

    private void OnSpawnRandomTestCommand(string command, string[] args)
    {
        string difficulty = args.Length == 0 ? "normal" : args[0].Trim().ToLowerInvariant();
        if (args.Length > 1 || difficulty is not ("easy" or "normal" or "hard"))
        {
            this.Monitor.Log("Usage: spectrum_spawn_random_test [easy|normal|hard]", LogLevel.Warn);
            return;
        }

        this.monsterSpawner?.TrySpawnRandomMonster(difficulty);
    }

    private void OnSpawnMonsterTestCommand(string command, string[] args)
    {
        if (args.Length == 0)
        {
            this.Monitor.Log("Usage: spectrum_spawn_monster_test <monster name or ID>", LogLevel.Warn);
            return;
        }

        string monsterType = string.Join(" ", args);
        this.monsterSpawner?.TrySpawnMonster(monsterType);
    }

    private void OnSpawnMonsterForceTestCommand(string command, string[] args)
    {
        if (args.Length == 0)
        {
            this.Monitor.Log("Usage: spectrum_spawn_monster_force_test <monster name or ID>", LogLevel.Warn);
            return;
        }

        string monsterType = string.Join(" ", args);
        this.monsterSpawner?.TrySpawnMonster(monsterType, ignoreProgression: true);
    }

    private void OnProgressionTestCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady || Game1.player is null)
        {
            this.Monitor.Log("Load a save before changing the temporary progression test profile.", LogLevel.Warn);
            return;
        }

        if (args.Length != 1)
        {
            this.Monitor.Log("Usage: spectrum_progression_test <early|mines|skull100|danger-mines|quarry|skull-cavern|danger-skull|bug-lair|volcano|combat-0|combat-5|combat-10|shrine-0|shrine-1|shrine-2|all|reset>", LogLevel.Warn);
            this.Monitor.Log("Profiles are temporary and memory-only. 'reset' restores the real save's progression checks.", LogLevel.Info);
            return;
        }

        string preset = args[0].Trim().ToLowerInvariant();
        if (preset == "reset")
        {
            MonsterProgressionSnapshot.ClearTestOverride();
            this.Monitor.Log("Progression test override cleared. Using the real save again.", LogLevel.Info);
        }
        else if (!MonsterProgressionSnapshot.TrySetTestPreset(preset, Game1.player, out string error))
        {
            this.Monitor.Log(error, LogLevel.Warn);
            return;
        }
        else
        {
            this.Monitor.Log($"Temporary progression profile applied: {preset}. No save data was changed.", LogLevel.Warn);
        }

        this.OnMonsterPoolStatusCommand(command, Array.Empty<string>());
    }

    private void OnMonsterCatalogTestCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady || Game1.player is null || !Context.IsMainPlayer)
        {
            this.Monitor.Log("Load a save as the host before running the monster constructor test.", LogLevel.Warn);
            return;
        }

        int passed = 0;
        int failed = 0;
        foreach (string displayName in MonsterCatalog.GetAllTestableTypes())
        {
            if (!MonsterCatalog.TryGetCanonicalType(displayName, out string id, ignoreProgression: true))
            {
                failed++;
                this.Monitor.Log($"[Spectrum] CATALOG TEST FAILED | {displayName} | definition could not be resolved.", LogLevel.Error);
                continue;
            }

            if (!MonsterCatalog.TryCreateMonster(id, Game1.player.Position, out _, out string error, ignoreProgression: true))
            {
                failed++;
                this.Monitor.Log($"[Spectrum] CATALOG TEST FAILED | {displayName} | {error}", LogLevel.Error);
                continue;
            }

            passed++;
            this.Monitor.Log($"[Spectrum] CATALOG TEST PASSED | {displayName}", LogLevel.Trace);
        }

        this.Monitor.Log(
            $"Monster constructor/setup test complete: {passed} passed, {failed} failed, {passed + failed} tested. No monsters were added to the world.",
            failed == 0 ? LogLevel.Info : LogLevel.Error
        );
    }

    private void OnConnectionStatusCommand(string command, string[] args)
    {
        if (this.gameModsClient is null)
        {
            this.Monitor.Log(
                string.IsNullOrWhiteSpace(this.config?.HostToken)
                    ? "Spectrum Game Mods is not paired. Use the in-game Connect Twitch account button to pair this host."
                    : "Spectrum Game Mods is not connected. Check the in-game connection status and retry pairing if needed.",
                LogLevel.Warn
            );
            return;
        }

        this.Monitor.Log(
            this.gameModsClient.SpawnStateReason,
            this.gameModsClient.IsConnected ? LogLevel.Info : LogLevel.Warn
        );
    }

    private void OnMonsterPoolStatusCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady || Game1.player is null)
        {
            this.Monitor.Log("Load a save before checking monster pool progression.", LogLevel.Warn);
            return;
        }

        MonsterProgressionSnapshot progression = MonsterProgressionSnapshot.Read(Game1.player);
        IReadOnlyList<MonsterPool> unlockedPools = MonsterLocationPools.GetUnlockedPools(progression);
        IReadOnlyList<string> eligibleTypes = MonsterCatalog.GetEligibleTypes();
        IReadOnlyList<string> catalogIssues = MonsterCatalog.GetCatalogValidationIssues();
        IReadOnlyList<string> threatDataFallbacks = MonsterCatalog.GetThreatDataFallbacks();

        this.Monitor.Log($"Vanilla threat data: {MonsterCatalog.AllTypes.Count - threatDataFallbacks.Count}/{MonsterCatalog.AllTypes.Count} definitions | Fallbacks: {(threatDataFallbacks.Count == 0 ? "none" : string.Join(", ", threatDataFallbacks))}", LogLevel.Info);
        this.Monitor.Log($"Progression stage: {progression.ProgressionStageName} | Combat Level: {progression.CombatLevel}/10 | Test override: {(MonsterProgressionSnapshot.IsTestOverrideActive ? "ACTIVE (memory only)" : "off")}", LogLevel.Info);
        this.Monitor.Log($"Current area: {progression.CurrentAreaName} | Shrine difficulty: {progression.CurrentAreaDifficulty}/2", LogLevel.Info);
        this.Monitor.Log($"Milestones: Mines floor 120 {(progression.MinesCompleted ? "reached" : "not reached")} | Skull Cavern floor 100 {(progression.SkullCavern100Reached ? "reached" : "not reached")}", LogLevel.Info);
        this.Monitor.Log($"Unlocked monster pools: {string.Join(", ", unlockedPools)}", LogLevel.Info);
        this.Monitor.Log($"Currently selectable monster variants ({eligibleTypes.Count}/{MonsterCatalog.AllTypes.Count}): {string.Join(", ", eligibleTypes)}", LogLevel.Info);
        this.Monitor.Log(
            catalogIssues.Count == 0
                ? "Monster catalogue validation passed: all pool IDs, constructors and special setup methods are supported."
                : $"Monster catalogue validation found {catalogIssues.Count} issue(s): {string.Join(" | ", catalogIssues)}",
            catalogIssues.Count == 0 ? LogLevel.Info : LogLevel.Warn
        );
    }
}
