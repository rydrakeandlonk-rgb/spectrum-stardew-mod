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
    private readonly HashSet<string> processedCommandIds = new(StringComparer.Ordinal);
    private readonly Queue<string> processedCommandOrder = new();
    private DateTime retryCommandsAtUtc = DateTime.MinValue;

    public override void Entry(IModHelper helper)
    {
        this.monsterSpawner = new MonsterSpawner(this.Monitor);

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
            "spectrum_connection_status",
            "Show the Spectrum Game Mods connection status. Usage: spectrum_connection_status",
            this.OnConnectionStatusCommand
        );

        helper.ConsoleCommands.Add(
            "spectrum_monster_pool_status",
            "Show unlocked location pools, selectable monster variants, and unresolved constructors.",
            this.OnMonsterPoolStatusCommand
        );

        this.Monitor.Log("Spectrum Stardew Mod loaded. Use 'spectrum_connection_status' to check the gateway connection.", LogLevel.Info);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.gameModsClient is null ||
            this.monsterSpawner is null ||
            !this.gameModsClient.SpawnsEnabled ||
            DateTime.UtcNow < this.retryCommandsAtUtc ||
            !Context.IsWorldReady ||
            !Context.IsMainPlayer)
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
                Game1.chatBox?.addInfoMessage($"{redeemer} spawned {spawnDescription} in the {locationName}!");

                this.processedCommandIds.Add(command.CommandId);
                this.processedCommandOrder.Enqueue(command.CommandId);
                while (this.processedCommandOrder.Count > 500)
                    this.processedCommandIds.Remove(this.processedCommandOrder.Dequeue());

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
        this.monsterSpawner?.TrySpawnRandomMonster();
    }

    private void OnConnectionStatusCommand(string command, string[] args)
    {
        bool connected = this.gameModsClient?.IsConnected == true;
        this.Monitor.Log(connected
            ? "Spectrum Game Mods is connected."
            : "Spectrum Game Mods is not connected. Check BroadcasterUserId, HostToken and the SMAPI log.", LogLevel.Info);
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
