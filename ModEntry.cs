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

        this.Monitor.Log("Spectrum Stardew Mod loaded. Use 'spectrum_connection_status' to check the gateway connection.", LogLevel.Info);
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

        // Keep each tick bounded if a host reconnects with several queued commands.
        for (int i = 0; i < 3; i++)
        {
            if (!this.gameModsClient.TryDequeueCommand(out GameCommand? command) || command is null)
                break;

            if (!this.processedCommandIds.Add(command.CommandId))
            {
                this.Monitor.Log($"[Spectrum] DUPLICATE | {command.CommandId}", LogLevel.Trace);
                _ = this.gameModsClient.AcknowledgeAsync(command.CommandId);
                continue;
            }

            this.processedCommandOrder.Enqueue(command.CommandId);
            while (this.processedCommandOrder.Count > 500)
                this.processedCommandIds.Remove(this.processedCommandOrder.Dequeue());

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

            if (spawned)
            {
                string redeemer = DisplayValue(command.TwitchUserName);
                string spawnDescription = spawnedCount == 1 ? $"a {monsterSummary}" : monsterSummary;
                Game1.chatBox?.addInfoMessage($"{redeemer} spawned {spawnDescription} in the {locationName}!");
            }

            // Acknowledge only after the game-thread handler has processed the command.
            _ = this.gameModsClient.AcknowledgeAsync(command.CommandId);
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
}
