using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using SpectrumStardewMod.Networking;

namespace SpectrumStardewMod.Gameplay;

/// <summary>Routes approved monster types through Stardew's built-in debug command parser.</summary>
/// <remarks>
/// All command execution must happen on the game thread and on the host.
/// Network callbacks only enqueue typed requests for a later game tick.
/// </remarks>
internal sealed class MonsterSpawner
{
    private static readonly Point[] NearbyTileOffsets =
    {
        new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1),
        new Point(1, 1), new Point(-1, 1), new Point(1, -1), new Point(-1, -1),
        new Point(2, 0), new Point(-2, 0), new Point(0, 2), new Point(0, -2)
    };

    private const int MaximumMonstersPerRedemption = 10;
    private readonly IMonitor monitor;

    public MonsterSpawner(IMonitor monitor)
    {
        this.monitor = monitor;
    }

    public bool TrySpawnRandomMonster()
    {
        return this.TryRunMonsterCommand(MonsterCatalog.GetRandomType(), "Random monster");
    }

    public bool TrySpawnGreenSlime()
    {
        return this.TryRunMonsterCommand("GreenSlime", "Test Green Slime");
    }

    public bool TryExecute(GameCommand command, out int spawnedCount, out string result)
    {
        spawnedCount = 0;
        result = "Unsupported spawn behaviour.";

        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            result = "Only the multiplayer host can spawn monsters in a loaded save.";
            this.monitor.Log(result, LogLevel.Warn);
            return false;
        }

        int count;
        string monsterType;
        Func<string> chooseMonster;

        switch (command.BehaviorKey)
        {
            case "difficulty_random":
                count = 1;
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                result = "Difficulty-weighted random monster";
                break;

            case "chat_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.UserInput, out monsterType))
                {
                    result = "Chat selection was not a supported vanilla monster name.";
                    this.monitor.Log(result, LogLevel.Warn);
                    return false;
                }
                count = 1;
                chooseMonster = () => monsterType;
                result = "Chat-selected monster";
                break;

            case "random_count_random_monsters":
                count = Random.Shared.Next(
                    ClampCount(command.MinCount, 1),
                    ClampCount(command.MaxCount, 3) + 1
                );
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                result = "Random number of random monsters";
                break;

            case "fixed_count_random_monsters":
                count = ClampCount(command.Count, 1);
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                result = "Fixed number of random monsters";
                break;

            case "random_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out monsterType))
                {
                    result = "The configured monster is not supported.";
                    this.monitor.Log(result, LogLevel.Warn);
                    return false;
                }
                count = Random.Shared.Next(
                    ClampCount(command.MinCount, 1),
                    ClampCount(command.MaxCount, 3) + 1
                );
                chooseMonster = () => monsterType;
                result = "Random number of a specific monster";
                break;

            case "fixed_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out monsterType))
                {
                    result = "The configured monster is not supported.";
                    this.monitor.Log(result, LogLevel.Warn);
                    return false;
                }
                count = ClampCount(command.Count, 1);
                chooseMonster = () => monsterType;
                result = "Fixed number of a specific monster";
                break;

            default:
                result = "Unknown spawn behaviour.";
                this.monitor.Log(result, LogLevel.Warn);
                return false;
        }

        count = Math.Clamp(count, 1, MaximumMonstersPerRedemption);
        for (int i = 0; i < count; i++)
        {
            if (!this.TryRunMonsterCommand(chooseMonster(), result))
                break;
            spawnedCount++;
        }

        result = spawnedCount == count
            ? $"{result}: {spawnedCount} monster(s) spawned."
            : $"{result}: {spawnedCount} of {count} monster(s) spawned.";
        return spawnedCount > 0;
    }

    private static int ClampCount(int value, int fallback)
    {
        return Math.Clamp(value <= 0 ? fallback : value, 1, MaximumMonstersPerRedemption);
    }

    private bool TryRunMonsterCommand(string monsterType, string description)
    {
        if (!Context.IsWorldReady)
        {
            this.monitor.Log("Spawn rejected: no save is currently loaded.", LogLevel.Warn);
            return false;
        }

        if (!Context.IsMainPlayer)
        {
            this.monitor.Log("Spawn rejected: only the multiplayer host can create monsters.", LogLevel.Warn);
            return false;
        }

        if (Game1.eventUp || Game1.currentLocation is null || Game1.player is null || Game1.game1 is null)
        {
            this.monitor.Log("Spawn rejected: the current game state isn't suitable for a spawn.", LogLevel.Warn);
            return false;
        }

        GameLocation location = Game1.currentLocation;
        Vector2 playerTile = Game1.player.Tile;

        foreach (Point offset in NearbyTileOffsets)
        {
            Vector2 tile = new(playerTile.X + offset.X, playerTile.Y + offset.Y);
            if (!location.CanSpawnCharacterHere(tile))
                continue;

            int tileX = (int)tile.X;
            int tileY = (int)tile.Y;
            string command = $"monster {monsterType} {tileX} {tileY}";
            bool handled = Game1.game1.parseDebugInput(command);

            if (!handled)
            {
                this.monitor.Log($"Stardew rejected the monster command: {command}", LogLevel.Warn);
                return false;
            }

            this.monitor.Log($"{description} ({monsterType}) requested at {location.Name}, tile {tileX},{tileY}.", LogLevel.Info);
            return true;
        }

        this.monitor.Log("Spawn rejected: couldn't find a clear tile near the player.", LogLevel.Warn);
        return false;
    }
}
