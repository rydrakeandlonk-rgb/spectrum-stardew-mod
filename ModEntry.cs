using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
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
    private const int MaxPendingSpawnRequestsPerPlayer = 20;
    private DateTime retryCommandsAtUtc = DateTime.MinValue;
    private GenericModConfigMenu.IGenericModConfigMenuApi? configMenuApi;
    private string? pendingChallengeName;
    private string? pendingChallengeDeathCount;
    private Rectangle spawningButtonBounds = Rectangle.Empty;
    private Rectangle linkTwitchButtonBounds = Rectangle.Empty;
    private Rectangle clearEnemiesButtonBounds = Rectangle.Empty;
    private string gmcmStatusMessage = "";
    private readonly ConcurrentQueue<PairingResult> pairingResults = new();
    private readonly Queue<SpawnWorkItem> pendingSpawnCommands = new();
    private readonly HashSet<long> pendingSpawnRetryPlayers = new();
    private readonly HashSet<string> queuedSpawnCommandIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingForwardedCommand> forwardedCommands = new(StringComparer.Ordinal);
    private readonly Queue<PendingClearWorkItem> pendingClearActions = new();
    private readonly HashSet<string> queuedClearActionIds = new(StringComparer.Ordinal);
    private readonly HashSet<long> sessionContextRecipients = new();
    private readonly Dictionary<string, int> completedClearActionCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingForwardedClearAction> forwardedClearActions = new(StringComparer.Ordinal);
    private readonly List<PendingChatMessage> pendingChatMessages = new();
    private MonsterProgressionSnapshot? lastObservedProgression;
    private bool playerDeathLatched;
    private string lastPoolDiagnosticsSessionId = "";
    private string localConnectionSessionId = "";
    private string gameSessionId = "";
    private string hostBroadcasterUserId = "";
    private string lastReportedHostSessionContext = "";
    private bool wasGatewayConnected;
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
        helper.Events.Player.Warped += this.OnPlayerWarped;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
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
            "spectrum_spawn_horde_test",
            "Developer test: force the rare ten-monster horde event near the host player. Usage: spectrum_spawn_horde_test",
            this.OnSpawnHordeTestCommand
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
        this.lastObservedProgression = Context.IsMainPlayer && Game1.player is not null
            ? MonsterProgressionSnapshot.Read(Game1.player, useTestOverride: false)
            : null;
        this.playerDeathLatched = Game1.player is null || Game1.player.health <= 0;
        this.localConnectionSessionId = Guid.NewGuid().ToString("N");
        this.gameSessionId = Context.IsMainPlayer ? this.localConnectionSessionId : "";
        this.hostBroadcasterUserId = Context.IsMainPlayer ? this.config?.BroadcasterUserId ?? "" : "";
        this.lastReportedHostSessionContext = "";
        this.lastPoolDiagnosticsSessionId = "";
        this.wasGatewayConnected = false;
        this.monsterSpawner?.SetPoolOverrides(new Dictionary<MonsterPool, string>());
        this.sessionContextRecipients.Clear();
        this.gameModsClient?.Dispose();
        this.gameModsClient = null;

        if (!Context.IsMainPlayer && Game1.MasterPlayer is not null)
        {
            this.Helper.Multiplayer.SendMessage(
                new SessionContextRequest(),
                "spectrum.session.request",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: new[] { Game1.MasterPlayer.UniqueMultiplayerID }
            );
        }

        if (this.config is null ||
            string.IsNullOrWhiteSpace(this.config.BroadcasterUserId) ||
            string.IsNullOrWhiteSpace(this.config.HostToken))
        {
            return;
        }

        this.gameModsClient = new GameModsClient(this.config, this.Monitor, this.localConnectionSessionId);
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
        this.localConnectionSessionId = "";
        this.gameSessionId = "";
        this.hostBroadcasterUserId = "";
        this.lastReportedHostSessionContext = "";
        this.lastPoolDiagnosticsSessionId = "";
        this.wasGatewayConnected = false;
        this.sessionContextRecipients.Clear();
        this.retryCommandsAtUtc = DateTime.MinValue;
        this.pendingSpawnCommands.Clear();
        this.pendingSpawnRetryPlayers.Clear();
        this.monsterSpawner?.ClearPendingPlans();
        this.queuedSpawnCommandIds.Clear();
        this.forwardedCommands.Clear();
        this.pendingClearActions.Clear();
        this.queuedClearActionIds.Clear();
        this.completedClearActionCounts.Clear();
        this.forwardedClearActions.Clear();
        this.pendingChatMessages.Clear();
        this.lastObservedProgression = null;
        this.playerDeathLatched = false;
        this.lastPoolDiagnosticsSessionId = "";
        this.monsterSpawner?.SetPoolOverrides(new Dictionary<MonsterPool, string>());
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

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Connection");
        this.configMenuApi.AddComplexOption(
            this.ModManifest,
            () => "Twitch account",
            this.DrawLinkTwitchButton,
            () => "Connect this game to your Spectrum Twitch account.",
            height: () => 72,
            fieldId: "spectrum-link-twitch"
        );

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Spectrum Stardew Monster Spawner");
        this.configMenuApi.AddParagraph(this.ModManifest, this.GetGmcmStatusText);

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Monster controls");
        this.configMenuApi.AddComplexOption(
            this.ModManifest,
            () => "Monster spawning",
            this.DrawSpawningButton,
            () => "Pause or resume Twitch monster redemptions. Administrator overrides and connection safety still apply.",
            height: () => 72,
            fieldId: "spectrum-spawning-toggle"
        );

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Overlay Settings");
        this.configMenuApi.AddTextOption(
            this.ModManifest,
            () => this.pendingChallengeName ?? this.config?.ChallengeName ?? "Spectrum Challenge",
            value => this.pendingChallengeName = value,
            () => "Title",
            () => "Title displayed on the overlay.",
            fieldId: "spectrum-challenge-name"
        );
        this.configMenuApi.AddTextOption(
            this.ModManifest,
            () => this.pendingChallengeDeathCount ?? (this.config?.ChallengeDeathCount ?? 0).ToString(CultureInfo.InvariantCulture),
            value => this.pendingChallengeDeathCount = value,
            () => "Death Counter",
            () => "Overlay death count.",
            fieldId: "spectrum-challenge-death-count"
        );

        this.configMenuApi.AddSectionTitle(this.ModManifest, () => "Host actions");
        this.configMenuApi.AddParagraph(
            this.ModManifest,
            () => "Clear Enemies immediately removes hostile monsters from all currently loaded locations. Multiplayer host only."
        );
        this.configMenuApi.AddComplexOption(
            this.ModManifest,
            () => "Loaded enemies",
            this.DrawClearEnemiesButton,
            () => "Immediately clear hostile monsters from all loaded locations. Only the multiplayer host can use this action.",
            height: () => 72,
            fieldId: "spectrum-clear-enemies"
        );

        this.Monitor.Log("Spectrum settings and controls registered directly in Generic Mod Config Menu.", LogLevel.Info);
    }

    private void ResetGmcmOptions()
    {
        this.pendingChallengeName = this.config?.ChallengeName ?? "Spectrum Challenge";
        this.pendingChallengeDeathCount = (this.config?.ChallengeDeathCount ?? 0).ToString(CultureInfo.InvariantCulture);
        this.gmcmStatusMessage = "Challenge settings reset. Press Save to apply them.";
    }

    private void SaveGmcmOptions()
    {
        if (this.config is not null &&
            (this.pendingChallengeName is not null || this.pendingChallengeDeathCount is not null))
        {
            string challengeName = (this.pendingChallengeName ?? this.config.ChallengeName ?? "Spectrum Challenge").Trim();
            challengeName = string.Join(" ", challengeName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (challengeName.Length == 0) challengeName = "Spectrum Challenge";
            if (challengeName.Length > 80) challengeName = challengeName[..80];

            string deathCountText = (this.pendingChallengeDeathCount ?? this.config.ChallengeDeathCount.ToString(CultureInfo.InvariantCulture)).Trim();
            if (int.TryParse(deathCountText, NumberStyles.None, CultureInfo.InvariantCulture, out int deathCount) && deathCount is >= 0 and <= 1000000)
            {
                bool changed = this.config.ChallengeName != challengeName || this.config.ChallengeDeathCount != deathCount;
                this.config.ChallengeName = challengeName;
                this.config.ChallengeDeathCount = deathCount;
                if (changed)
                {
                    this.Helper.WriteConfig(this.config);
                    _ = this.gameModsClient?.SendChallengeStateAsync(this.config.ChallengeName, this.config.ChallengeDeathCount);
                    this.gmcmStatusMessage = "Overlay settings saved.";
                }
            }
            else
            {
                this.gmcmStatusMessage = "Death Counter must be a whole number from 0 to 1,000,000. No overlay changes were saved.";
            }
            this.pendingChallengeName = null;
            this.pendingChallengeDeathCount = null;
        }

    }

    private string GetGmcmStatusText()
    {
        bool enabled = this.gameModsClient?.SpawnsEnabled == true;
        string spawning = "Monster spawning: " + (enabled ? "Enabled" : "Paused");
        string hostName = Game1.MasterPlayer?.Name;
        string authority = "World Authority: " + (string.IsNullOrWhiteSpace(hostName) ? "Detecting host…" : hostName);
        string state = !string.IsNullOrWhiteSpace(this.pairingStatus)
            ? this.pairingStatus
            : this.gmcmStatusMessage;

        return string.IsNullOrWhiteSpace(state) ? string.Join(Environment.NewLine, new[] { spawning, authority }) : string.Join(Environment.NewLine, new[] { spawning, authority, state });
    }

    private void DrawSpawningButton(SpriteBatch spriteBatch, Vector2 position)
    {
        GameModsClient? client = this.gameModsClient;
        string label = client?.IsConnected == true
            ? (client.SafetyGateEnabled ? "Pause Monster Spawning" : "Enable Monster Spawning")
            : "Connect Twitch to control spawning";
        this.spawningButtonBounds = this.DrawGmcmButton(spriteBatch, position, label, client?.IsConnected == true);
    }

    private bool IsTwitchLinked =>
        !string.IsNullOrWhiteSpace(this.config?.BroadcasterUserId) &&
        !string.IsNullOrWhiteSpace(this.config?.HostToken) &&
        !string.IsNullOrWhiteSpace(this.config?.BroadcasterLogin);

    private void DrawLinkTwitchButton(SpriteBatch spriteBatch, Vector2 position)
    {
        bool linked = this.IsTwitchLinked;
        string login = this.config?.BroadcasterLogin ?? "";
        string label = linked
            ? (string.IsNullOrWhiteSpace(login) ? "Twitch Connected" : $"{login} Connected")
            : "Link Twitch  ↗";
        this.linkTwitchButtonBounds = this.DrawGmcmButton(spriteBatch, position, label, !linked);
    }

    private void DrawClearEnemiesButton(SpriteBatch spriteBatch, Vector2 position)
    {
        bool canClear = Context.IsMainPlayer && Context.IsWorldReady;
        this.clearEnemiesButtonBounds = this.DrawGmcmButton(spriteBatch, position, "Clear Enemies", canClear);
    }

    private Rectangle DrawGmcmButton(SpriteBatch spriteBatch, Vector2 position, string label, bool enabled)
    {
        Rectangle bounds = new((int)position.X + 12, (int)position.Y + 8, 380, 56);
        IClickableMenu.drawTextureBox(spriteBatch, bounds.X, bounds.Y, bounds.Width, bounds.Height, enabled ? Color.White : Color.Gray);
        Vector2 textSize = Game1.smallFont.MeasureString(label);
        Vector2 textPosition = new(bounds.Center.X - textSize.X / 2f, bounds.Center.Y - textSize.Y / 2f);
        spriteBatch.DrawString(Game1.smallFont, label, textPosition, enabled ? Game1.textColor : Color.DarkGray);
        return bounds;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (e.Button != SButton.MouseLeft || this.configMenuApi is null ||
            !this.configMenuApi.TryGetCurrentMenu(out IManifest? currentMod, out _) ||
            currentMod?.UniqueID != this.ModManifest.UniqueID)
            return;

        Point cursor = e.Cursor.ScreenPixels.ToPoint();
        if (this.spawningButtonBounds.Contains(cursor))
        {
            this.Helper.Input.Suppress(e.Button);
            this.ToggleMonsterSpawning();
        }
        else if (this.linkTwitchButtonBounds.Contains(cursor) && !this.IsTwitchLinked)
        {
            this.Helper.Input.Suppress(e.Button);
            this.StartPairingOrOpenControlCenter();
        }
        else if (this.clearEnemiesButtonBounds.Contains(cursor))
        {
            this.Helper.Input.Suppress(e.Button);
            this.ClearEnemiesImmediately();
        }
    }

    private void ToggleMonsterSpawning()
    {
        GameModsClient? client = this.gameModsClient;
        if (client?.IsConnected != true)
        {
            this.gmcmStatusMessage = "Connect Twitch first. Spawning cannot be changed while disconnected.";
            return;
        }

        bool enabled = !client.SafetyGateEnabled;
        _ = client.SetSpawnsEnabledAsync(enabled);
        this.gmcmStatusMessage = enabled
            ? "Enabling monster spawning. Administrator overrides still apply."
            : "Pausing monster spawning.";
    }

    private void ClearEnemiesImmediately()
    {
        if (!Context.IsMainPlayer)
        {
            this.gmcmStatusMessage = "Only the multiplayer host can clear loaded enemies.";
            return;
        }

        if (!Context.IsWorldReady || this.monsterSpawner is null)
        {
            this.gmcmStatusMessage = "The world is not ready. No enemies were cleared.";
            return;
        }

        int removed = this.monsterSpawner.ClearLoadedMonsters();
        this.gmcmStatusMessage = removed >= 0
            ? $"Spectrum cleared {removed} loaded enemy/enemies."
            : "Spectrum could not clear enemies because the world was not ready.";
        if (removed >= 0)
            Game1.chatBox?.addInfoMessage(this.gmcmStatusMessage);
    }

    private void StartPairingOrOpenControlCenter()
    {
        if (this.IsTwitchLinked &&
            (this.gameModsClient?.IsConnected == true ||
             this.pairingStatus.StartsWith("Twitch account authorised", StringComparison.OrdinalIgnoreCase)))
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

    private void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        if (Context.IsMainPlayer && (this.pendingSpawnCommands.Count > 0 || this.pendingClearActions.Count > 0))
            this.pendingSpawnRetryPlayers.Add(e.Player.UniqueMultiplayerID);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Context.IsWorldReady)
        {
            this.CheckLocalPlayerDeath();

            if (Context.IsMainPlayer && Game1.player is not null && e.IsMultipleOf(60))
            {
                this.CheckProgressionUnlocks();
                this.SendSessionContextToOnlineFarmhands();
            }

            this.ProcessPendingChatMessages();

            while (this.pairingResults.TryDequeue(out PairingResult? pairingResult) && pairingResult is not null)
            {
                this.config ??= new ModConfig();
                this.config.BroadcasterUserId = pairingResult.BroadcasterUserId;
                this.config.BroadcasterLogin = pairingResult.BroadcasterLogin;
                this.config.HostToken = pairingResult.HostToken;
                this.config.ConfigVersion = ModConfig.CurrentConfigVersion;
                this.Helper.WriteConfig(this.config);
                this.gameModsClient?.Dispose();
                this.wasGatewayConnected = false;
                if (string.IsNullOrWhiteSpace(this.localConnectionSessionId))
                    this.localConnectionSessionId = Guid.NewGuid().ToString("N");
                if (Context.IsMainPlayer)
                {
                    this.gameSessionId = this.localConnectionSessionId;
                    this.hostBroadcasterUserId = this.config.BroadcasterUserId;
                }
                this.gameModsClient = new GameModsClient(this.config, this.Monitor, this.localConnectionSessionId);
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

        bool gatewayConnectedNow = this.gameModsClient?.IsConnected == true;
        if (gatewayConnectedNow && !this.wasGatewayConnected)
        {
            this.lastPoolDiagnosticsSessionId = "";
            this.lastReportedHostSessionContext = "";
        }
        this.wasGatewayConnected = gatewayConnectedNow;

        bool poolOverridesChanged = false;
        if (this.gameModsClient is not null)
        {
            while (this.gameModsClient.TryDequeuePoolOverrideUpdate(out PoolOverrideUpdate? update) && update is not null)
            {
                if (Context.IsMainPlayer && update.SessionId == this.gameModsClient.ActiveSessionId)
                {
                    var parsedOverrides = new Dictionary<MonsterPool, string>();
                    foreach ((string poolName, string mode) in update.Overrides)
                    {
                        if (Enum.TryParse(poolName, ignoreCase: true, out MonsterPool pool) &&
                            Enum.IsDefined(typeof(MonsterPool), pool) &&
                            mode is "automatic" or "force_enabled" or "force_disabled")
                            parsedOverrides[pool] = mode;
                    }

                    this.monsterSpawner.SetPoolOverrides(parsedOverrides);
                    poolOverridesChanged = true;
                    this.Monitor.Log($"[Spectrum] SPAWN POOL OVERRIDES | revision {update.Revision} | {parsedOverrides.Count} explicit override(s).", LogLevel.Info);
                }
            }
        }

        if (!Context.IsMainPlayer && this.gameModsClient?.IsConnected == true &&
            !string.IsNullOrWhiteSpace(this.gameSessionId) &&
            !string.IsNullOrWhiteSpace(this.hostBroadcasterUserId) &&
            this.lastReportedHostSessionContext != this.gameSessionId)
        {
            _ = this.gameModsClient.SendSessionContextAsync(this.hostBroadcasterUserId, this.gameSessionId);
            this.lastReportedHostSessionContext = this.gameSessionId;
        }

        if (Context.IsMainPlayer && this.gameModsClient?.IsConnected == true &&
            !string.IsNullOrWhiteSpace(this.gameModsClient.ActiveSessionId) &&
            (poolOverridesChanged || this.lastPoolDiagnosticsSessionId != this.gameModsClient.ActiveSessionId))
        {
            this.SendPoolDiagnostics();
        }

        if (Context.IsMainPlayer && this.gameModsClient?.IsConnected == true && !this.gameModsClient.SpawnsEnabled &&
            (this.pendingSpawnCommands.Count > 0 || this.pendingClearActions.Count > 0))
        {
            this.pendingSpawnCommands.Clear();
            this.pendingClearActions.Clear();
            this.pendingSpawnRetryPlayers.Clear();
            this.queuedSpawnCommandIds.Clear();
            this.queuedClearActionIds.Clear();
            this.monsterSpawner.ClearPendingPlans();
            this.Monitor.Log("[Spectrum] Cleared pending spawn requests because spawning is disabled.", LogLevel.Info);
        }

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
                    this.QueueSpawnCommand(localCommand, null);
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
            PendingClearWorkItem? readyAction = null;
            int actionQueueCount = this.pendingClearActions.Count;
            for (int i = 0; i < actionQueueCount; i++)
            {
                PendingClearWorkItem candidate = this.pendingClearActions.Dequeue();
                if (!candidate.HasAttempted || this.pendingSpawnRetryPlayers.Remove(candidate.SenderPlayerId))
                {
                    readyAction = candidate;
                    break;
                }
                this.pendingClearActions.Enqueue(candidate);
            }
            if (readyAction is not null)
            {
            PendingClearWorkItem actionWork = readyAction;
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
            else if (actionWork.Name is "stardew.community_single" or "stardew.community_horde" or "stardew.community_mega_horde")
            {
                Farmer? target = Game1.getOnlineFarmers().FirstOrDefault(farmer => farmer.UniqueMultiplayerID == actionWork.SenderPlayerId);
                succeeded = target is not null && this.monsterSpawner.TrySpawnCommunityEncounter(
                    actionWork.Outcome,
                    actionWork.Difficulty,
                    target,
                    out removed,
                    out detail,
                    actionWork.ActionId,
                    actionWork.MonsterCount
                );
            }
            else
            {
                succeeded = false;
                detail = "Unsupported game action";
            }

            if (!succeeded && (actionWork.Name is "stardew.community_horde" or "stardew.community_mega_horde") && detail.StartsWith("PENDING:", StringComparison.Ordinal))
            {
                this.pendingClearActions.Enqueue(actionWork with { HasAttempted = true });
                this.Monitor.Log($"[Spectrum] COMMUNITY PENDING | {actionWork.ActionId} | waiting for the target farmer's next location change.", LogLevel.Warn);
                return;
            }

            this.queuedClearActionIds.Remove(actionWork.ActionId);
            if (succeeded)
            {
                this.completedClearActionCounts[actionWork.ActionId] = removed;
                while (this.completedClearActionCounts.Count > 500)
                    this.completedClearActionCounts.Remove(this.completedClearActionCounts.Keys.First());

                if (actionWork.Name is "stardew.community_horde" or "stardew.community_mega_horde")
                {
                    if (actionWork.Announce)
                        this.AnnounceCommunityHorde(actionWork.DonorName, actionWork.CharityName, actionWork.Name == "stardew.community_mega_horde");
                }
            }

            if (actionWork.IsLocal)
                _ = this.gameModsClient?.AcknowledgeActionAsync(actionWork.ActionId, succeeded, Math.Max(0, removed), succeeded ? null : detail, detail);
            else
                this.CompleteForwardedClearAction(actionWork.SenderPlayerId, actionWork.ActionId, succeeded, Math.Max(0, removed), succeeded ? "" : detail, detail);
            }
        }

        if (this.pendingSpawnCommands.Count == 0)
            return;

        SpawnWorkItem? readyWork = null;
        int queuedCount = this.pendingSpawnCommands.Count;
        for (int i = 0; i < queuedCount; i++)
        {
            SpawnWorkItem candidate = this.pendingSpawnCommands.Dequeue();
            long targetId = candidate.ReplyToPlayerId ?? Game1.player?.UniqueMultiplayerID ?? -1;
            if (!candidate.HasAttempted || this.pendingSpawnRetryPlayers.Remove(targetId))
            {
                readyWork = candidate;
                break;
            }
            this.pendingSpawnCommands.Enqueue(candidate);
        }
        if (readyWork is null)
            return;

        SpawnWorkItem work = readyWork;
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

        if (!work.HordeRollCompleted)
        {
            work = work with
            {
                HordeRollCompleted = true,
                IsHorde = this.monsterSpawner.ShouldTriggerHorde(command)
            };
        }

        bool spawned = this.monsterSpawner.TryExecute(
            command,
            targetFarmer,
            out int spawnedCount,
            out int requestedCount,
            out List<string> spawnedTypes,
            out string locationName,
            out string result,
            forceHorde: work.IsHorde
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
            string redeemer = DisplayValue(command.TwitchUserName);
            if (work.IsHorde)
            {
                this.AnnounceHorde(redeemer);
            }
            else
            {
                string spawnDescription = spawnedCount == 1 ? $"a {monsterSummary}" : monsterSummary;
                Game1.chatBox?.addInfoMessage($"{redeemer} spawned {spawnDescription} near {targetFarmer.Name} in {locationName}!");
            }

            this.CompleteSpawnCommand(
                work,
                true,
                null,
                spawnedTypes.Select(MonsterCatalog.GetDisplayName).ToArray(),
                spawnedCount,
                locationName,
                work.IsHorde
            );
        }
        else
        {
            // Keep the selected monster plan in the host spawner. Retry only
            // when this same farmer next changes location; never poll or reroll.
            this.pendingSpawnCommands.Enqueue(work with { HasAttempted = true });
            this.Monitor.Log($"[Spectrum] PENDING | {command.CommandId} | {Math.Max(0, requestedCount - spawnedCount)} monster(s) remain; waiting for the target farmer's next location change.", LogLevel.Warn);
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
        else if (action.Name is "stardew.community_single" or "stardew.community_horde" or "stardew.community_mega_horde")
        {
            if (this.monsterSpawner is not null && Game1.player is not null)
            {
                succeeded = this.monsterSpawner.TrySpawnCommunityEncounter(
                    action.Outcome,
                    action.Difficulty,
                    Game1.player,
                    out removed,
                    out detail,
                    action.ActionId,
                    action.MonsterCount
                );
            }
            else
            {
                succeeded = false;
                detail = "The host player or monster spawner is unavailable.";
            }
        }
        else
        {
            _ = this.gameModsClient?.AcknowledgeActionAsync(action.ActionId, false, 0, "Unsupported game action.");
            return;
        }

        if (!succeeded && (action.Name is "stardew.community_horde" or "stardew.community_mega_horde") && detail.StartsWith("PENDING:", StringComparison.Ordinal))
        {
            long localPlayerId = Game1.player?.UniqueMultiplayerID ?? -1;
            if (this.CountPendingSpawnRequests(localPlayerId) >= MaxPendingSpawnRequestsPerPlayer)
            {
                _ = this.gameModsClient?.AcknowledgeActionAsync(action.ActionId, false, 0, "The pending spawn limit for this player has been reached.");
                return;
            }
            this.pendingClearActions.Enqueue(new PendingClearWorkItem(
                action.ActionId, action.Name, action.Difficulty, action.Outcome, localPlayerId, action.MonsterCount,
                IsLocal: true, HasAttempted: true, DonorName: action.DonorName, CharityName: action.CharityName, Announce: action.Announce
            ));
            return;
        }

        if (succeeded && action.Name is "stardew.community_horde" or "stardew.community_mega_horde")
        {
            if (action.Announce)
                this.AnnounceCommunityHorde(action.DonorName, action.CharityName, action.Name == "stardew.community_mega_horde");
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
        if (this.gameModsClient is null || Game1.MasterPlayer is null ||
            string.IsNullOrWhiteSpace(this.gameSessionId))
            return;

        if (this.forwardedClearActions.Count == 0 &&
            this.gameModsClient.TryDequeueAction(out GameAction? action) &&
            action is not null)
        {
            if (action.Name is not ("stardew.clear_loaded_monsters" or "stardew.spawn_random_test" or "stardew.community_single" or "stardew.community_horde" or "stardew.community_mega_horde"))
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
                new ForwardedClearAction(
                    pending.Action.ActionId, pending.Action.Name, pending.Action.Difficulty, pending.Action.Outcome,
                    pending.Action.MonsterCount, pending.Action.DonorName, pending.Action.CharityName, pending.Action.Announce,
                    this.gameSessionId, this.config?.BroadcasterUserId ?? ""
                ),
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
            Game1.MasterPlayer is null || string.IsNullOrWhiteSpace(this.gameSessionId) ||
            DateTime.UtcNow < this.retryCommandsAtUtc)
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

            if (string.IsNullOrWhiteSpace(this.gameSessionId))
                continue;

            ForwardedSpawnCommand message = ForwardedSpawnCommand.FromCommand(
                pending.Command,
                this.config?.BroadcasterUserId ?? "",
                this.gameSessionId
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

    private int CountPendingSpawnRequests(long playerId)
    {
        int commandCount = this.pendingSpawnCommands.Count(item =>
            (item.ReplyToPlayerId ?? Game1.player?.UniqueMultiplayerID ?? -1) == playerId);
        int actionCount = this.pendingClearActions.Count(item => item.SenderPlayerId == playerId);
        return commandCount + actionCount;
    }

    private void QueueSpawnCommand(GameCommand command, long? replyToPlayerId)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId) || this.processedCommandIds.Contains(command.CommandId))
        {
            if (!string.IsNullOrWhiteSpace(command.CommandId))
                this.CompleteSpawnCommand(new SpawnWorkItem(command, replyToPlayerId), true, "Already processed.");
            return;
        }

        long targetPlayerId = replyToPlayerId ?? Game1.player?.UniqueMultiplayerID ?? -1;
        if (this.CountPendingSpawnRequests(targetPlayerId) >= MaxPendingSpawnRequestsPerPlayer)
        {
            this.Monitor.Log($"[Spectrum] PENDING LIMIT | rejected {command.CommandId} | player {targetPlayerId} already has {MaxPendingSpawnRequestsPerPlayer} pending spawn requests.", LogLevel.Warn);
            this.CompleteSpawnCommand(new SpawnWorkItem(command, replyToPlayerId), false, "The pending spawn limit for this player has been reached.");
            return;
        }

        if (this.queuedSpawnCommandIds.Add(command.CommandId))
            this.pendingSpawnCommands.Enqueue(new SpawnWorkItem(command, replyToPlayerId));
    }

    private void CompleteSpawnCommand(
        SpawnWorkItem work,
        bool success,
        string? error,
        string[]? monsterNames = null,
        int spawnedCount = 0,
        string locationName = "",
        bool isHorde = false)
    {
        if (work.ReplyToPlayerId is long playerId)
        {
            this.Helper.Multiplayer.SendMessage(
                new SpawnCommandResult(
                    work.Command.CommandId,
                    success,
                    error ?? "",
                    monsterNames,
                    spawnedCount,
                    locationName,
                    DisplayValue(work.Command.TwitchUserName),
                    isHorde),
                "spectrum.spawn.result",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: new[] { playerId }
            );
        }
        else if (success)
        {
            if (monsterNames is { Length: > 0 } && spawnedCount == monsterNames.Length)
            {
                _ = this.gameModsClient?.SendMonsterSpawnedAsync(
                    work.Command.CommandId,
                    DisplayValue(work.Command.TwitchUserName),
                    monsterNames,
                    spawnedCount,
                    locationName,
                    isHorde
                );
            }
            _ = this.gameModsClient?.AcknowledgeAsync(work.Command.CommandId);
        }
        else
        {
            this.gameModsClient?.RequeueCommand(work.Command);
        }
    }

    private void SendSessionContextToOnlineFarmhands()
    {
        if (!Context.IsMainPlayer || Game1.MasterPlayer is null)
            return;

        foreach (Farmer farmer in Game1.getOnlineFarmers())
        {
            long playerId = farmer.UniqueMultiplayerID;
            if (playerId == Game1.MasterPlayer.UniqueMultiplayerID || this.sessionContextRecipients.Contains(playerId))
                continue;

            IMultiplayerPeer? peer = this.Helper.Multiplayer.GetConnectedPlayer(playerId);
            if (peer?.GetMod(this.ModManifest.UniqueID) is not null)
                this.SendSessionContext(playerId);
        }
    }

    private void SendSessionContext(long playerId)
    {
        if (!Context.IsMainPlayer ||
            !System.Text.RegularExpressions.Regex.IsMatch(this.gameSessionId ?? "", @"^[a-fA-F0-9]{32}$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(this.hostBroadcasterUserId ?? "", @"^\d{1,30}$"))
            return;

        this.Helper.Multiplayer.SendMessage(
            new SessionContextMessage
            {
                HostSessionId = this.gameSessionId,
                HostBroadcasterUserId = this.hostBroadcasterUserId
            },
            "spectrum.session.context",
            modIDs: new[] { this.ModManifest.UniqueID },
            playerIDs: new[] { playerId }
        );
        this.sessionContextRecipients.Add(playerId);
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.ModManifest.UniqueID || !Context.IsWorldReady)
            return;

        if (e.Type == "spectrum.session.request" && Context.IsMainPlayer)
        {
            IMultiplayerPeer? peer = this.Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID);
            if (peer?.GetMod(this.ModManifest.UniqueID) is not null)
                this.SendSessionContext(e.FromPlayerID);
            return;
        }

        if (e.Type == "spectrum.session.context" &&
            !Context.IsMainPlayer &&
            Game1.MasterPlayer is not null &&
            e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            SessionContextMessage context;
            try { context = e.ReadAs<SessionContextMessage>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid host session context: {ex.Message}", LogLevel.Warn);
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(context.HostSessionId ?? "", @"^[a-fA-F0-9]{32}$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(context.HostBroadcasterUserId ?? "", @"^\d{1,30}$"))
                return;

            this.gameSessionId = context.HostSessionId.ToLowerInvariant();
            this.hostBroadcasterUserId = context.HostBroadcasterUserId;
            if (this.gameModsClient?.IsConnected == true)
            {
                _ = this.gameModsClient.SendSessionContextAsync(this.hostBroadcasterUserId, this.gameSessionId);
                this.lastReportedHostSessionContext = this.gameSessionId;
            }
            this.Monitor.Log($"[Spectrum] SESSION CONTEXT | client of broadcaster {this.hostBroadcasterUserId} | host session {this.gameSessionId}.", LogLevel.Info);
            return;
        }

        if (e.Type == "spectrum.charity.horde.announce" &&
            !Context.IsMainPlayer &&
            e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            string message;
            try { message = e.ReadAs<string>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid charity horde announcement: {ex.Message}", LogLevel.Warn);
                return;
            }
            this.ShowHordePreludeAndQueueCustomBanner(message);
            return;
        }

        if (e.Type == "spectrum.horde.announce" &&
            !Context.IsMainPlayer &&
            e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            string redeemer;
            try { redeemer = e.ReadAs<string>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid horde announcement: {ex.Message}", LogLevel.Warn);
                return;
            }

            this.ShowHordePreludeAndQueueBanner(redeemer);
            return;
        }

        if (e.Type == "spectrum.progression.unlocked" &&
            !Context.IsMainPlayer &&
            e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
        {
            string message;
            try { message = e.ReadAs<string>(); }
            catch (Exception ex)
            {
                this.Monitor.Log($"Ignored an invalid progression notification: {ex.Message}", LogLevel.Warn);
                return;
            }

            if (SupportedProgressionNotifications.Contains(message))
                Game1.addHUDMessage(new HUDMessage(message, HUDMessage.achievement_type));
            return;
        }

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
                message.HostSessionId != this.gameSessionId ||
                !System.Text.RegularExpressions.Regex.IsMatch(message.SourceBroadcasterUserId ?? "", @"^\d{1,30}$") ||
                message.Name is not ("stardew.clear_loaded_monsters" or "stardew.spawn_random_test" or "stardew.community_single" or "stardew.community_horde" or "stardew.community_mega_horde") ||
                message.Difficulty is not ("easy" or "normal" or "hard") ||
                (message.Name == "stardew.community_single" && message.Outcome != "single") ||
                (message.Name == "stardew.community_horde" && message.Outcome != "horde") ||
                (message.Name == "stardew.community_mega_horde" && message.Outcome != "mega"))
            {
                this.Monitor.Log($"Rejected forwarded Spectrum clear action from multiplayer peer {e.FromPlayerID}.", LogLevel.Warn);
                return;
            }

            if (this.completedClearActionCounts.TryGetValue(message.ActionId, out int previousCount))
            {
                this.CompleteForwardedClearAction(e.FromPlayerID, message.ActionId, true, previousCount, "Already processed.", "Already processed");
                return;
            }

            if (this.CountPendingSpawnRequests(e.FromPlayerID) >= MaxPendingSpawnRequestsPerPlayer)
            {
                this.CompleteForwardedClearAction(e.FromPlayerID, message.ActionId, false, 0, "The pending spawn limit for this player has been reached.");
                return;
            }
            if (this.queuedClearActionIds.Add(message.ActionId))
                this.pendingClearActions.Enqueue(new PendingClearWorkItem(
                    message.ActionId, message.Name, message.Difficulty, message.Outcome, e.FromPlayerID, message.MonsterCount,
                    DonorName: message.DonorName, CharityName: message.CharityName, Announce: message.Announce
                ));
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
                message.HostSessionId != this.gameSessionId ||
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
                this.CompleteSpawnCommand(new SpawnWorkItem(command, e.FromPlayerID), true, "Already processed.");
                return;
            }

            this.QueueSpawnCommand(command, e.FromPlayerID);
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
                if (result.MonsterNames is { Length: > 0 } && result.SpawnedCount == result.MonsterNames.Length)
                {
                    _ = this.gameModsClient?.SendMonsterSpawnedAsync(
                        result.CommandId,
                        DisplayValue(result.RedeemerName),
                        result.MonsterNames,
                        result.SpawnedCount,
                        DisplayValue(result.LocationName),
                        result.IsHorde
                    );
                }
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

    private static readonly HashSet<string> SupportedProgressionNotifications = new(StringComparer.Ordinal)
    {
        "The Dangerous Mines Have Awakened",
        "The Quarry Mine Has Awakened",
        "The Skull Caverns Have Awakened",
        "The Skull Caverns Have Grown More Dangerous",
        "A Greater Danger Stirs in the Skull Caverns",
        "The Mutant Bug Lair Has Awakened",
        "The Volcano Dungeon Has Awakened"
    };

    private static readonly HashSet<string> SupportedForwardedBehaviors = new(StringComparer.Ordinal)
    {
        "difficulty_random",
        "chat_monster",
        "random_count_random_monsters",
        "fixed_count_random_monsters",
        "random_count_specific_monster",
        "fixed_count_specific_monster"
    };

    private sealed record SpawnWorkItem(
        GameCommand Command,
        long? ReplyToPlayerId,
        bool HordeRollCompleted = false,
        bool IsHorde = false,
        bool HasAttempted = false
    );
    private sealed record PendingChatMessage(DateTime ShowAtUtc, string Message);
    private sealed record PendingForwardedCommand(GameCommand Command, DateTime LastSentAtUtc);
    private sealed record PendingClearWorkItem(
        string ActionId, string Name, string Difficulty, string Outcome, long SenderPlayerId,
        int MonsterCount = 10, bool IsLocal = false, bool HasAttempted = false,
        string DonorName = "", string CharityName = "", bool Announce = true
    );
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

    private void OnSpawnHordeTestCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || Game1.player is null || this.monsterSpawner is null)
        {
            this.Monitor.Log("Load a save as the host before testing a horde spawn.", LogLevel.Warn);
            return;
        }

        var testCommand = new GameCommand(
            "local-horde-test",
            "stardew-valley",
            "stardew.spawn_monster",
            "Local Test",
            "Local horde test",
            DateTimeOffset.Now.ToString("O"),
            "difficulty_random",
            "",
            "",
            "normal",
            1,
            1,
            1
        );

        bool spawned = this.monsterSpawner.TryExecute(
            testCommand,
            Game1.player,
            out int spawnedCount,
            out int requestedCount,
            out _,
            out string locationName,
            out string result,
            forceHorde: true
        );

        if (spawned && result == "SUCCESS" && spawnedCount == 10 && requestedCount == 10)
        {
            this.AnnounceHorde("LOCAL TEST");
            this.Monitor.Log($"[Spectrum] HORDE TEST PASSED | {spawnedCount} monsters | {locationName}", LogLevel.Info);
        }
        else
        {
            this.Monitor.Log($"[Spectrum] HORDE TEST FAILED | {spawnedCount}/{requestedCount} monsters | {locationName} | {result}", LogLevel.Warn);
        }
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

    private void CheckLocalPlayerDeath()
    {
        if (Game1.player is null)
            return;

        bool isDead = Game1.player.health <= 0;
        if (!isDead)
        {
            this.playerDeathLatched = false;
            return;
        }

        if (this.playerDeathLatched)
            return;

        this.playerDeathLatched = true;
        if (this.config is null)
            return;

        if (this.config.ChallengeDeathCount >= 1000000)
        {
            this.Monitor.Log("Challenge death counter reached its maximum supported value.", LogLevel.Warn);
            return;
        }

        this.config.ChallengeDeathCount++;

        try
        {
            this.Helper.WriteConfig(this.config);
        }
        catch (Exception ex)
        {
            this.Monitor.Log("Could not persist the updated challenge death count: " + ex.Message, LogLevel.Error);
        }

        _ = this.gameModsClient?.SendPlayerDiedAsync(this.config.ChallengeDeathCount);
        this.Monitor.Log(
            $"[Spectrum] CHALLENGE DEATH | {this.config.ChallengeName} | {this.config.ChallengeDeathCount}",
            LogLevel.Info
        );
    }

    private void CheckProgressionUnlocks()
    {
        if (Game1.player is null)
            return;

        MonsterProgressionSnapshot current = MonsterProgressionSnapshot.Read(Game1.player);
        MonsterProgressionSnapshot? previous = this.lastObservedProgression;
        this.lastObservedProgression = current;

        if (previous is not null && previous != current)
            this.SendPoolDiagnostics();

        // The first snapshot for a save is a baseline, so loading an already-progressed
        // farm never replays old unlock announcements.
        if (previous is null)
            return;

        if (!previous.DangerousMinesUnlocked && current.DangerousMinesUnlocked)
            this.NotifyPoolUnlocked("The Dangerous Mines Have Awakened");
        if (!previous.QuarryMineUnlocked && current.QuarryMineUnlocked)
            this.NotifyPoolUnlocked("The Quarry Mine Has Awakened");
        if (!previous.SkullCavernUnlocked && current.SkullCavernUnlocked)
            this.NotifyPoolUnlocked("The Skull Caverns Have Awakened");
        if (!previous.SkullCavern100Reached && current.SkullCavern100Reached)
            this.NotifyPoolUnlocked("The Skull Caverns Have Grown More Dangerous");
        if (!previous.DangerousSkullCavernUnlocked && current.DangerousSkullCavernUnlocked)
            this.NotifyPoolUnlocked("A Greater Danger Stirs in the Skull Caverns");
        if (!previous.BugLairUnlocked && current.BugLairUnlocked)
            this.NotifyPoolUnlocked("The Mutant Bug Lair Has Awakened");
        if (!previous.VolcanoDungeonUnlocked && current.VolcanoDungeonUnlocked)
            this.NotifyPoolUnlocked("The Volcano Dungeon Has Awakened");
    }

    private void SendPoolDiagnostics()
    {
        if (!Context.IsMainPlayer || !Context.IsWorldReady || Game1.player is null ||
            this.monsterSpawner is null || this.gameModsClient?.IsConnected != true ||
            string.IsNullOrWhiteSpace(this.gameModsClient.ActiveSessionId))
            return;

        MonsterProgressionSnapshot progression = MonsterProgressionSnapshot.Read(Game1.player, useTestOverride: false);
        IReadOnlyList<PoolDiagnostic> pools = this.monsterSpawner.GetPoolDiagnostics(progression);
        var diagnostics = new
        {
            host_name = Game1.MasterPlayer?.Name ?? Game1.player.Name,
            progression = new
            {
                stage = progression.ProgressionStageName,
                mines_floor_120_reached = progression.MinesCompleted,
                skull_cavern_floor_100_reached = progression.SkullCavern100Reached,
                skull_key_obtained = progression.SkullCavernUnlocked,
                dangerous_mines_unlocked = progression.DangerousMinesUnlocked,
                quarry_mine_unlocked = progression.QuarryMineUnlocked,
                dangerous_skull_cavern_unlocked = progression.DangerousSkullCavernUnlocked,
                bug_lair_unlocked = progression.BugLairUnlocked,
                volcano_dungeon_unlocked = progression.VolcanoDungeonUnlocked,
                combat_level = progression.CombatLevel,
                current_area = progression.CurrentAreaName,
                current_area_difficulty = progression.CurrentAreaDifficulty,
                test_override_active = MonsterProgressionSnapshot.IsTestOverrideActive
            },
            pools,
            reported_at = DateTimeOffset.UtcNow.ToString("O")
        };

        _ = this.gameModsClient.SendPoolDiagnosticsAsync(diagnostics);
        this.lastPoolDiagnosticsSessionId = this.gameModsClient.ActiveSessionId;
    }

    private void NotifyPoolUnlocked(string message)
    {
        Game1.addHUDMessage(new HUDMessage(message, HUDMessage.achievement_type));

        if (!Context.IsMainPlayer || Game1.player is null)
            return;

        long[] recipients = Game1.getOnlineFarmers()
            .Where(farmer => farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            .Select(farmer => farmer.UniqueMultiplayerID)
            .ToArray();

        if (recipients.Length > 0)
        {
            this.Helper.Multiplayer.SendMessage(
                message,
                "spectrum.progression.unlocked",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: recipients
            );
        }
    }

    private void AnnounceHorde(string redeemer)
    {
        this.ShowHordePreludeAndQueueBanner(redeemer);

        if (!Context.IsMainPlayer || Game1.player is null)
            return;

        long[] recipients = Game1.getOnlineFarmers()
            .Where(farmer => farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            .Where(farmer => this.Helper.Multiplayer.GetConnectedPlayer(farmer.UniqueMultiplayerID)?.GetMod(this.ModManifest.UniqueID) is not null)
            .Select(farmer => farmer.UniqueMultiplayerID)
            .ToArray();

        if (recipients.Length > 0)
        {
            this.Helper.Multiplayer.SendMessage(
                redeemer,
                "spectrum.horde.announce",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: recipients
            );
        }
    }

    private void AnnounceCommunityHorde(string donorName, string charityName, bool mega)
    {
        if (string.IsNullOrWhiteSpace(donorName) || string.IsNullOrWhiteSpace(charityName))
        {
            this.AnnounceHorde("SPECTRUM");
            return;
        }

        string message = $"{donorName.Trim()} donated to {charityName.Trim()} and summoned a {(mega ? "MEGA " : "")}horde!";
        this.ShowHordePreludeAndQueueCustomBanner(message);

        if (!Context.IsMainPlayer || Game1.player is null)
            return;

        long[] recipients = Game1.getOnlineFarmers()
            .Where(farmer => farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            .Where(farmer => this.Helper.Multiplayer.GetConnectedPlayer(farmer.UniqueMultiplayerID)?.GetMod(this.ModManifest.UniqueID) is not null)
            .Select(farmer => farmer.UniqueMultiplayerID)
            .ToArray();

        if (recipients.Length > 0)
        {
            this.Helper.Multiplayer.SendMessage(
                message,
                "spectrum.charity.horde.announce",
                modIDs: new[] { this.ModManifest.UniqueID },
                playerIDs: recipients
            );
        }
    }

    private void ShowHordePreludeAndQueueBanner(string redeemer)
    {
        this.ShowHordePreludeAndQueueCustomBanner($"{redeemer} HAS SPAWNED A HORDE!");
    }

    private void ShowHordePreludeAndQueueCustomBanner(string message)
    {
        Game1.chatBox?.addInfoMessage("Oh no... something is happening.");
        this.pendingChatMessages.Add(new PendingChatMessage(DateTime.UtcNow.AddSeconds(3), message));
    }

    private void ProcessPendingChatMessages()
    {
        for (int i = 0; i < this.pendingChatMessages.Count;)
        {
            PendingChatMessage pending = this.pendingChatMessages[i];
            if (DateTime.UtcNow < pending.ShowAtUtc)
            {
                i++;
                continue;
            }

            Game1.addHUDMessage(new HUDMessage(pending.Message, HUDMessage.achievement_type));
            this.pendingChatMessages.RemoveAt(i);
        }
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
        IReadOnlyList<MonsterPool> unlockedPools = MonsterLocationPools.GetUnlockedPools(progression, this.monsterSpawner?.PoolOverrides);
        IReadOnlyList<string> eligibleTypes = MonsterCatalog.GetEligibleTypes(this.monsterSpawner?.PoolOverrides);
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
