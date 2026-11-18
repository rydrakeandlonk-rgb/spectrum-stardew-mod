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
        return this.TryRunMonsterCommand(MonsterCatalog.GetRandomType(), out _);
    }

    public bool TrySpawnGreenSlime()
    {
        return this.TryRunMonsterCommand("GreenSlime", out _);
    }

    public bool TryExecute(
        GameCommand command,
        out int spawnedCount,
        out int requestedCount,
        out List<string> spawnedTypes,
        out string locationName,
        out string result)
    {
        spawnedCount = 0;
        requestedCount = 0;
        spawnedTypes = new List<string>();
        locationName = "";
        result = "FAILED: unsupported spawn behaviour";

        int count;
        string monsterType;
        Func<string> chooseMonster;

        switch (command.BehaviorKey)
        {
            case "difficulty_random":
                count = 1;
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                break;

            case "chat_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.UserInput, out monsterType))
                {
                    result = "FAILED: unsupported chat monster";
                    return false;
                }
                count = 1;
                chooseMonster = () => monsterType;
                break;

            case "random_count_random_monsters":
                count = GetRandomCount(command.MinCount, command.MaxCount);
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                break;

            case "fixed_count_random_monsters":
                count = ClampCount(command.Count, 1);
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                break;

            case "random_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out monsterType))
                {
                    result = "FAILED: unsupported configured monster";
                    return false;
                }
                count = GetRandomCount(command.MinCount, command.MaxCount);
                chooseMonster = () => monsterType;
                break;

            case "fixed_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out monsterType))
                {
                    result = "FAILED: unsupported configured monster";
                    return false;
                }
                count = ClampCount(command.Count, 1);
                chooseMonster = () => monsterType;
                break;

            default:
                return false;
        }

        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            result = "FAILED: host world not ready";
            return false;
        }

        requestedCount = Math.Clamp(count, 1, MaximumMonstersPerRedemption);
        for (int i = 0; i < requestedCount; i++)
        {
            string selectedType = chooseMonster();
            if (!this.TryRunMonsterCommand(selectedType, out string spawnedLocation))
                break;

            spawnedCount++;
            spawnedTypes.Add(selectedType);
            locationName = spawnedLocation;
        }

        result = spawnedCount == requestedCount
            ? "SUCCESS"
            : spawnedCount > 0
                ? "PARTIAL"
                : "FAILED";
        return spawnedCount > 0;
    }

    private static int GetRandomCount(int minimum, int maximum)
    {
        int min = ClampCount(minimum, 1);
        int max = Math.Max(min, ClampCount(maximum, 3));
        return Random.Shared.Next(min, max + 1);
    }

    private static int ClampCount(int value, int fallback)
    {
        return Math.Clamp(value <= 0 ? fallback : value, 1, MaximumMonstersPerRedemption);
    }

    private bool TryRunMonsterCommand(string monsterType, out string locationName)
    {
        locationName = "";

        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | host world not ready", LogLevel.Warn);
            return false;
        }

        if (Game1.eventUp || Game1.currentLocation is null || Game1.player is null || Game1.game1 is null)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | game state unavailable", LogLevel.Warn);
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
            string debugCommand = $"monster {monsterType} {tileX} {tileY}";
            int charactersBefore = location.characters.Count;
            bool handled = Game1.game1.parseDebugInput(debugCommand);
            int charactersAfter = location.characters.Count;

            // A handled parser command isn't enough: confirm the location gained
            // a character before reporting a successful spawn to the player.
            if (!handled || charactersAfter <= charactersBefore)
            {
                this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(monsterType)} | {location.Name}", LogLevel.Warn);
                return false;
            }

            locationName = location.Name;
            return true;
        }

        this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(monsterType)} | no clear tile", LogLevel.Warn);
        return false;
    }
}
