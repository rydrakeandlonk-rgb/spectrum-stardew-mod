using System.Diagnostics;
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
    private readonly HashSet<string> processedCommandIds = new(StringComparer.Ordinal);
    private readonly Queue<string> processedCommandOrder = new();
    private const string ProcessedCommandGlobalKey = "processed-spectrum-command-ids";
    private bool processedCommandsLoaded;
    private DateTime retryCommandsAtUtc = DateTime.MinValue;
    private Rectangle spectrumHudButtonBounds;

    public override void Entry(IModHelper helper)
    {
        this.monsterSpawner = new MonsterSpawner(this.Monitor);

        this.LoadProcessedCommands(helper);

        ModConfig config = helper.ReadConfig<ModConfig>();
        if (!string.IsNullOrWhiteSpace(config.BroadcasterUserId) &&
            !string.IsNullOrWhiteSpace(config.HostToken))
        {
            this.gameModsClient = new GameModsClient(config, this.Monitor);
        }
        else
        {
            this.Monitor.Log("Spectrum Game Mods connection is not paired yet. Local test commands remain available.", LogLevel.Info);
        }

        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Display.RenderedHud += this.OnRenderedHud;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;

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

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null)
            return;

        this.spectrumHudButtonBounds = new Rectangle(
            Math.Max(12, Game1.uiViewport.Width - 172),
            18,
            154,
            42
        );
        e.SpriteBatch.Draw(Game1.staminaRect, this.spectrumHudButtonBounds, new Color(67, 46, 98, 225));
        const string label = "Spectrum";
        Vector2 textSize = Game1.smallFont.MeasureString(label);
        Vector2 textPosition = new(
            this.spectrumHudButtonBounds.X + (this.spectrumHudButtonBounds.Width - textSize.X) / 2f,
            this.spectrumHudButtonBounds.Y + (this.spectrumHudButtonBounds.Height - textSize.Y) / 2f
        );
        e.SpriteBatch.DrawString(Game1.smallFont, label, textPosition, Color.White);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null || e.Button != SButton.MouseLeft)
            return;

        Vector2 cursor = e.Cursor.GetScaledScreenPixels();
        if (!this.spectrumHudButtonBounds.Contains((int)cursor.X, (int)cursor.Y))
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
            () => this.gameModsClient?.SpawnsEnabled == true,
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
                    Game1.addHUDMessage(new HUDMessage(
                        $"Spectrum cleared {removed} loaded monster(s).",
                        Color.LightGreen,
                        3000f
                    ));
                }
            },
            this.OpenControlCenter
        );
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
            Game1.addHUDMessage(new HUDMessage(
                "Open Spectrum Control Center in your browser to pair this host.",
                Color.White,
                4000f
            ));
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
        if (this.gameModsClient is null ||
            this.monsterSpawner is null ||
            !Context.IsWorldReady ||
            !Context.IsMainPlayer)
        {
            return;
        }

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

        if (!this.gameModsClient.SpawnsEnabled ||
            DateTime.UtcNow < this.retryCommandsAtUtc)
        {
            return;
        }

        // Process one redemption at a time so Stop Spawns takes effect cleanly.
        for (int i = 0; i < 1; i++)
        {
            if (!this.gameModsClient.SpawnsEnabled ||
                !this.gameModsClient.TryDequeueCommand(out GameCommand? command) || command is null)
                break;

            if (this.processedCommandIds.Contains(command.CommandId))
            {
                this.Monitor.Log($"[Spectrum] DUPLICATE | {command.CommandId}", LogLevel.Trace);
                if (!this.TryPersistProcessedCommands())
                {
                    this.gameModsClient.RequeueCommand(command);
                    this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
                    break;
                }

                _ = this.gameModsClient.AcknowledgeAsync(command.CommandId);
                continue;
            }

            this.Monitor.Log(
                $"[Spectrum] REDEEM | {DisplayValue(command.TwitchUserName)} | {DisplayValue(command.RewardTitle)} | {FormatRedeemedAt(command.RedeemedAt)} | {command.CommandId}",
                LogLevel.Info
            );

            bool spawned = this.monsterSpawner.TryExecute(
                command,
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
                $"[Spectrum] SPAWN | {(string.IsNullOrWhiteSpace(monsterSummary) ? "none" : monsterSummary)} | {(string.IsNullOrWhiteSpace(locationName) ? "n/a" : locationName)} | {spawnedCount}/{requestedCount} | {result}",
                spawned && result == "SUCCESS" ? LogLevel.Info : LogLevel.Warn
            );

            if (spawned && result == "SUCCESS" && spawnedCount == requestedCount)
            {
                string redeemer = DisplayValue(command.TwitchUserName);
                string spawnDescription = spawnedCount == 1 ? $"a {monsterSummary}" : monsterSummary;
                if (this.gameModsClient.Settings.ChatResponses)
                    Game1.chatBox?.addInfoMessage($"{redeemer} spawned {spawnDescription} in the {locationName}!");

                // Persist before acknowledgement so a reconnect or game restart
                // cannot normally replay a redemption that already spawned.
                if (!this.RememberProcessedCommand(command.CommandId))
                {
                    this.gameModsClient.RequeueCommand(command);
                    this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
                    break;
                }

                _ = this.gameModsClient.AcknowledgeAsync(command.CommandId);
            }
            else
            {
                // Keep the durable server-side command pending and retry locally after a short delay.
                this.gameModsClient.RequeueCommand(command);
                this.retryCommandsAtUtc = DateTime.UtcNow.AddSeconds(10);
                this.Monitor.Log($"[Spectrum] RETRY | {command.CommandId} | Spawn was not completed; retrying in 10 seconds.", LogLevel.Warn);
                break;
            }
        }
    }

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
            this.Monitor.Log("Spectrum Game Mods is not paired. Add BroadcasterUserId and HostToken to the local mod config.", LogLevel.Warn);
            return;
        }

        if (!this.gameModsClient.IsConnected)
        {
            this.Monitor.Log("Spectrum Game Mods is not connected. Check GatewayUrl, BroadcasterUserId, HostToken and the SMAPI log.", LogLevel.Warn);
            return;
        }

        this.Monitor.Log(
            this.gameModsClient.SpawnsEnabled
                ? "Spectrum Game Mods is connected; monster spawns are ENABLED by Control Center."
                : "Spectrum Game Mods is connected; monster spawns are PAUSED by Control Center.",
            LogLevel.Info
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
