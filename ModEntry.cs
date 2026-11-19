using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SpectrumStardewMod.Gameplay;
using SpectrumStardewMod.Networking;

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
    private HostSettings? pendingGmcmSettings;
    private string pendingGmcmConnectionAction = "none";
    private string pendingGmcmHostAction = "none";
    private string lastHandledGmcmConnectionAction = "none";
    private string lastHandledGmcmHostAction = "none";
    private bool gmcmClearArmed;
    private string gmcmStatusMessage = "";
    private readonly ConcurrentQueue<PairingResult> pairingResults = new();
    private readonly Queue<SpawnWorkItem> pendingSpawnCommands = new();
    private readonly HashSet<string> queuedSpawnCommandIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingForwardedCommand> forwardedCommands = new(StringComparer.Ordinal);
    private readonly Queue<PendingClearWorkItem> pendingClearActions = new();
    private readonly HashSet<string> queuedClearActionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> completedClearActionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingForwardedClearAction> forwardedClearActions = new(StringComparer.Ordinal);
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
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;

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
        this.pendingClearActions.Clear();
        this.queuedClearActionIds.Clear();
        this.completedClearActionCounts.Clear();
        this.forwardedClearActions.Clear();
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
            reset: this.ResetGmcmOptions,
            save: this.SaveGmcmOptions,
            titleScreenOnly: false
        );

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Spectrum Stardew Monster Spawner");
        this.configMenuApi.AddParagraph(this.ModManifest, this.GetGmcmStatusText);

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Streamer settings");
        this.configMenuApi.AddBoolOption(
            this.ModManifest,
            () => this.GetGmcmSettings().SpawnEnabled,
            value => this.SetGmcmSettings(settings => settings with { SpawnEnabled = value }),
            () => "Streamer spawning preference",
            () => "Shared with the Streamer preference in Control Center. Changes sync through the gateway; the Control Center safety gate and administrator override still take priority.",
            "spectrum-spawning-enabled"
        );
        this.configMenuApi.AddTextOption(
            this.ModManifest,
            () => this.GetGmcmSettings().Difficulty,
            value => this.SetGmcmSettings(settings => settings with { Difficulty = value }),
            () => "Difficulty",
            () => "Choose the monster threat weighting used for this streamer's redemptions.",
            new[] { "easy", "normal", "hard" },
            value => value.ToUpperInvariant(),
            "spectrum-difficulty"
        );
        this.configMenuApi.AddBoolOption(
            this.ModManifest,
            () => this.GetGmcmSettings().ChatResponses,
            value => this.SetGmcmSettings(settings => settings with { ChatResponses = value }),
            () => "Chat responses",
            () => "Show the normal in-game chat response after a successful spawn.",
            "spectrum-chat-responses"
        );
        this.configMenuApi.AddBoolOption(
            this.ModManifest,
            () => this.GetGmcmSettings().DebugMessages,
            value => this.SetGmcmSettings(settings => settings with { DebugMessages = value }),
            () => "Debug messages",
            () => "Show additional diagnostic messages for this streamer's commands.",
            "spectrum-debug-messages"
        );

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Connection actions");
        this.configMenuApi.AddParagraph(
            this.ModManifest,
            () => "Choose an action, then press Save at the bottom of this GMCM page. Choose No action and save before repeating the same action."
        );
        this.configMenuApi.AddTextOption(
            this.ModManifest,
            () => "none",
            value => this.pendingGmcmConnectionAction = value,
            () => "Twitch / Control Center",
            () => "Connect this Stardew installation to its own Twitch account, continue an existing pairing, or open Control Center if already connected.",
            new[] { "none", "connect" },
            value => value == "connect" ? "Connect Twitch / open Control Center" : "No action",
            "spectrum-connection-action"
        );

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Host actions");
        this.configMenuApi.AddParagraph(
            this.ModManifest,
            () => this.gmcmClearArmed
                ? "Clear is armed. Select Confirm clear and press Save to remove hostile monsters from all currently loaded locations."
                : "Clearing is a two-step, host-only action. Select Arm clear and press Save, then select Confirm clear and press Save. Farmhand spawn requests are still executed by the host."
        );
        this.configMenuApi.AddTextOption(
            this.ModManifest,
            () => "none",
            value => this.pendingGmcmHostAction = value,
            () => "Loaded monsters",
            () => "Host only. Arm and confirm separately before clearing hostile monsters from all loaded locations.",
            new[] { "none", "arm_clear", "confirm_clear", "cancel_clear" },
            value => value switch
            {
                "arm_clear" => "Arm clear",
                "confirm_clear" => "Confirm clear",
                "cancel_clear" => "Cancel clear",
                _ => "No action"
            },
            "spectrum-loaded-monster-action"
        );

        this.Monitor.Log("Spectrum settings and actions registered directly in Generic Mod Config Menu.", LogLevel.Info);
    }

    private HostSettings GetGmcmSettings()
    {
        return this.pendingGmcmSettings ?? this.gameModsClient?.Settings ?? new HostSettings();
    }

    private void SetGmcmSettings(Func<HostSettings, HostSettings> update)
    {
        this.pendingGmcmSettings = update(this.GetGmcmSettings());
    }

    private void ResetGmcmOptions()
    {
        this.pendingGmcmSettings = new HostSettings();
        this.pendingGmcmConnectionAction = "none";
        this.pendingGmcmHostAction = "none";
        this.lastHandledGmcmConnectionAction = "none";
        this.lastHandledGmcmHostAction = "none";
        this.gmcmClearArmed = false;
        this.gmcmStatusMessage = "Settings reset to their defaults. Press Save to apply them.";
    }

    private void SaveGmcmOptions()
    {
        if (this.pendingGmcmSettings is HostSettings settings)
        {
            HostSettings current = this.gameModsClient?.Settings ?? new HostSettings();
            if (settings != current)
            {
                if (this.gameModsClient?.IsConnected == true)
                {
                    _ = this.gameModsClient.UpdateSettingsAsync(settings);
                    this.gmcmStatusMessage = "Streamer settings sent to Control Center.";
                }
                else
                {
                    this.gmcmStatusMessage = "Settings were not sent because this player is not connected to the Spectrum gateway. Connect Twitch first.";
                }
            }

            this.pendingGmcmSettings = null;
        }

        if (this.pendingGmcmConnectionAction == "none")
        {
            this.lastHandledGmcmConnectionAction = "none";
        }
        else if (this.pendingGmcmConnectionAction != this.lastHandledGmcmConnectionAction)
        {
            this.lastHandledGmcmConnectionAction = this.pendingGmcmConnectionAction;
            this.StartPairingOrOpenControlCenter();
        }

        switch (this.pendingGmcmHostAction)
        {
            case "none":
                if (this.lastHandledGmcmHostAction == "arm_clear")
                    this.gmcmClearArmed = false;
                this.lastHandledGmcmHostAction = "none";
                break;

            case "arm_clear":
                if (this.lastHandledGmcmHostAction != "arm_clear")
                {
                    if (!Context.IsMainPlayer)
                    {
                        this.gmcmStatusMessage = "Only the multiplayer host can clear loaded monsters.";
                    }
                    else
                    {
                        this.gmcmClearArmed = true;
                        this.gmcmStatusMessage = "Clear armed. Select Confirm clear and press Save to continue.";
                    }
                    this.lastHandledGmcmHostAction = "arm_clear";
                }
                break;

            case "confirm_clear":
                if (this.lastHandledGmcmHostAction != "confirm_clear")
                {
                    this.lastHandledGmcmHostAction = "confirm_clear";
                    if (!Context.IsMainPlayer)
                    {
                        this.gmcmStatusMessage = "Only the multiplayer host can clear loaded monsters.";
                    }
                    else if (!this.gmcmClearArmed)
                    {
                        this.gmcmStatusMessage = "Clear was not armed. Select Arm clear and save first.";
                    }
                    else if (!Context.IsWorldReady)
                    {
                        this.gmcmStatusMessage = "The world is not ready. No monsters were cleared.";
                        this.gmcmClearArmed = false;
                    }
                    else
                    {
                        int removed = this.monsterSpawner?.ClearLoadedMonsters() ?? -1;
                        this.gmcmClearArmed = false;
                        this.gmcmStatusMessage = removed >= 0
                            ? $"Spectrum cleared {removed} loaded monster(s)."
                            : "Spectrum could not clear monsters because the world was not ready.";
                        if (removed >= 0)
                            Game1.chatBox?.addInfoMessage(this.gmcmStatusMessage);
                    }
                }
                break;

            case "cancel_clear":
                this.gmcmClearArmed = false;
                this.lastHandledGmcmHostAction = "cancel_clear";
                this.gmcmStatusMessage = "Monster clearing cancelled.";
                break;
        }
    }

    private string GetGmcmStatusText()
    {
        bool connected = this.gameModsClient?.IsConnected == true;
        string connection = connected ? "Gateway: Connected" : "Gateway: Not connected";
        string role = Context.IsMainPlayer ? "World authority: Host" : "World authority: Farmhand (host executes spawns)";
        string preference = "Shared streamer preference: " + (this.gameModsClient?.Settings.SpawnEnabled == true ? "ON" : "OFF");
        string adminOverride = "Admin override: " + (this.gameModsClient?.SpawnOverride ?? "follow").Replace("_", " ");
        string safetyGate = "Control Center safety gate: " + (this.gameModsClient?.SafetyGateEnabled == true ? "OPEN" : "CLOSED");
        string state = !string.IsNullOrWhiteSpace(this.pairingStatus)
            ? this.pairingStatus
            : !string.IsNullOrWhiteSpace(this.gmcmStatusMessage)
                ? this.gmcmStatusMessage
                : this.gameModsClient?.SpawnStateReason ?? "Connect this player to their Spectrum Twitch account.";

        return string.Join(Environment.NewLine, new[] { connection, role, preference, adminOverride, safetyGate, state });
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
                this.ForwardFarmhandClearActions();
            }
        }

        // Only the host mutates the shared Stardew world.
        if (!Context.IsMainPlayer)
            return;

        if (this.gameModsClient is not null)
        {
            while (this.gameModsClient.TryDequeueAction(out GameAction? action) && action is not null)
                this.ExecuteLocalClearAction(action);
        }

        if (this.pendingClearActions.Count > 0)
        {
            PendingClearWorkItem actionWork = this.pendingClearActions.Dequeue();
            int removed = 0;
            string detail = "";
            bool succeeded;

            if (actionWork.Name == "stardew.clear_loaded_monsters")
            {
                removed = this.monsterSpawner.ClearLoadedMonsters();
                succeeded = removed >= 0;
                detail = succeeded ? "Cleared loaded monsters" : "The host world was not ready";
            }
            else if (actionWork.Name == "stardew.spawn_random_test")
            {
                Farmer? target = Game1.getOnlineFarmers().FirstOrDefault(farmer => farmer.UniqueMultiplayerID == actionWork.SenderPlayerId);
                succeeded = target is not null && this.monsterSpawner.TrySpawnRandomMonster(
                    actionWork.Difficulty,
                    target
                );
                detail = succeeded ? "Random progression-eligible enemy spawned" : "No eligible enemy or safe spawn tile was available";
            }
            else
            {
                succeeded = false;
                detail = "Unsupported game action";
            }

            this.queuedClearActionIds.Remove(actionWork.ActionId);
            if (succeeded)
            {
                this.completedClearActionCounts[actionWork.ActionId] = removed;
                while (this.completedClearActionCounts.Count > 500)
                    this.completedClearActionCounts.Remove(this.completedClearActionCounts.Keys.First());
            }

            this.CompleteForwardedClearAction(
                actionWork.SenderPlayerId,
                actionWork.ActionId,
                succeeded,
                Math.Max(0, removed),
                succeeded ? "" : detail,
                detail
            );
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

    private void ExecuteLocalClearAction(GameAction action)
    {
        int removed = 0;
        string detail;
        bool succeeded;

        if (action.Name == "stardew.clear_loaded_monsters")
        {
            removed = this.monsterSpawner?.ClearLoadedMonsters() ?? -1;
            succeeded = removed >= 0;
            detail = succeeded ? "Cleared loaded monsters" : "The host world was not ready";
        }
        else if (action.Name == "stardew.spawn_random_test")
        {
            succeeded = this.monsterSpawner?.TrySpawnRandomMonster(action.Difficulty) == true;
            detail = succeeded ? "Random progression-eligible enemy spawned" : "No eligible enemy or safe spawn tile was available";
        }
        else
        {
            _ = this.gameModsClient?.AcknowledgeActionAsync(action.ActionId, false, 0, "Unsupported game action.");
            return;
        }

        this.Monitor.Log($"[Spectrum] REMOTE ACTION | {action.Name} | {(succeeded ? detail : "failed: " + detail)}", succeeded ? LogLevel.Info : LogLevel.Warn);
        _ = this.gameModsClient?.AcknowledgeActionAsync(
            action.ActionId,
            succeeded,
            Math.Max(0, removed),
            succeeded ? null : detail,
            detail
        );
    }

    private void ForwardFarmhandClearActions()
    {
        if (this.gameModsClient is null || Game1.MasterPlayer is null)
            return;

        if (this.forwardedClearActions.Count == 0 &&
            this.gameModsClient.TryDequeueAction(out GameAction? action) &&
            action is not null)
        {
            if (action.Name is not ("stardew.clear_loaded_monsters" or "stardew.spawn_random_test"))
            {
                _ = this.gameModsClient.AcknowledgeActionAsync(action.ActionId, false, 0, "Unsupported game action.");
                return;
            }

            this.forwardedClearActions[action.ActionId] = new PendingForwardedClearAction(action, DateTime.MinValue);
        }

        foreach (string actionId in this.forwardedClearActions.Keys.ToList())
        {
            PendingForwardedClearAction pending = this.forwardedClearActions[actionId];
            if (pending.LastSentAtUtc != DateTime.MinValue &&
                DateTime.UtcNow - pending.LastSentAtUtc < TimeSpan.FromSeconds(10))
                continue;

            this.Helper.Multiplayer.SendMessage(
                new ForwardedClearAction(pending.Action.ActionId, pending.Action.Name, pending.Action.Difficulty),
                "spectrum.action.forward",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
            this.forwardedClearActions[actionId] = pending with { LastSentAtUtc = DateTime.UtcNow };
        }
    }

    private void CompleteForwardedClearAction(long senderPlayerId, string actionId, bool success, int removedCount, string error, string detail = "")
    {
        this.Helper.Multiplayer.SendMessage(
            new ClearActionResult(actionId, success, removedCount, error, detail),
            "spectrum.action.result",
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { senderPlayerId }
        );
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

        if (e.Type == "spectrum.action.forward" && Context.IsMainPlayer)
        {
            ForwardedClearAction message;
            try { message = e.ReadAs<ForwardedClearAction>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid forwarded clear action: {ex.Message}", LogLevel.Warn);
                return;
            }

            IMultiplayerPeer? peer = this.Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID);
            bool hasMod = peer?.GetMod(this.ModManifest.UniqueID) is not null;
            bool farmerOnline = Game1.getOnlineFarmers().Any(farmer => farmer.UniqueMultiplayerID == e.FromPlayerID);
            if (!hasMod || !farmerOnline || string.IsNullOrWhiteSpace(message.ActionId) ||
                message.Name is not ("stardew.clear_loaded_monsters" or "stardew.spawn_random_test") ||
                message.Difficulty is not ("easy" or "normal" or "hard"))
            {
                this.Monitor.Log($"Rejected forwarded Spectrum clear action from multiplayer peer {e.FromPlayerID}.", LogLevel.Warn);
                return;
            }

            if (this.completedClearActionCounts.TryGetValue(message.ActionId, out int previousCount))
            {
                this.CompleteForwardedClearAction(e.FromPlayerID, message.ActionId, true, previousCount, "Already processed.", "Already processed");
                return;
            }

            if (this.queuedClearActionIds.Add(message.ActionId))
                this.pendingClearActions.Enqueue(new PendingClearWorkItem(message.ActionId, message.Name, message.Difficulty, e.FromPlayerID));
            return;
        }

        if (e.Type == "spectrum.action.result" && !Context.IsMainPlayer &&
            e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            ClearActionResult result;
            try { result = e.ReadAs<ClearActionResult>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid host clear-action result: {ex.Message}", LogLevel.Warn);
                return;
            }

            if (!this.forwardedClearActions.TryGetValue(result.ActionId, out PendingForwardedClearAction? pending))
                return;

            if (result.Success)
            {
                this.forwardedClearActions.Remove(result.ActionId);
                _ = this.gameModsClient?.AcknowledgeActionAsync(result.ActionId, true, result.RemovedCount, null, result.Detail);
            }
            else
            {
                this.forwardedClearActions[result.ActionId] = pending with { LastSentAtUtc = DateTime.UtcNow };
                this.Monitor.Log($"[Spectrum] MULTIPLAYER CLEAR RETRY | {result.ActionId} | {result.Error}", LogLevel.Warn);
            }
            return;
        }

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
    private sealed record PendingClearWorkItem(string ActionId, string Name, string Difficulty, long SenderPlayerId);
    private sealed record PendingForwardedClearAction(GameAction Action, DateTime LastSentAtUtc);

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
                    ? "Spectrum Game Mods is not paired. Use the in-game Connect Twitch account button to pair this player's Twitch account."
                    : "Spectrum Game Mods is not connected. Check this player's in-game connection status and retry pairing if needed.",
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
