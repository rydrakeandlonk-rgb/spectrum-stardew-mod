using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;
using SpectrumStardewMod.Networking;

namespace SpectrumStardewMod.Gameplay;

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

    public MonsterSpawner(IMonitor monitor) => this.monitor = monitor;

    public bool TrySpawnRandomMonster()
    {
        if (!Context.IsWorldReady || Game1.player is null)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | load a save before selecting a random monster", LogLevel.Warn);
            return false;
        }

        return this.TrySpawnCandidate(MonsterCatalog.GetRandomType(), out _, out _);
    }

    public bool TrySpawnGreenSlime() =>
        MonsterCatalog.TryGetCanonicalType("Green Slime", out string id) &&
        this.TrySpawnCandidate(id, out _, out _);

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
        string candidateId;
        Func<string> chooseMonster;

        switch (command.BehaviorKey)
        {
            case "difficulty_random":
                count = 1;
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty);
                break;
            case "chat_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.UserInput, out candidateId))
                {
                    result = "FAILED: unsupported or progression-locked chat monster";
                    return false;
                }
                count = 1;
                chooseMonster = () => candidateId;
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
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId))
                {
                    result = "FAILED: unsupported or progression-locked configured monster";
                    return false;
                }
                count = GetRandomCount(command.MinCount, command.MaxCount);
                chooseMonster = () => candidateId;
                break;
            case "fixed_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId))
                {
                    result = "FAILED: unsupported or progression-locked configured monster";
                    return false;
                }
                count = ClampCount(command.Count, 1);
                chooseMonster = () => candidateId;
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
        var createdMonsters = new List<Monster>();
        GameLocation? createdInLocation = null;
        for (int i = 0; i < requestedCount; i++)
        {
            string selectedId = chooseMonster();
            if (!this.TrySpawnCandidate(selectedId, out string spawnedLocation, out Monster? createdMonster) ||
                createdMonster is null)
            {
                // A redemption is atomic: if the full requested group cannot be
                // placed, remove this attempt's earlier spawns before it is retried.
                if (createdInLocation is not null)
                {
                    foreach (Monster previousMonster in createdMonsters)
                        createdInLocation.characters.Remove(previousMonster);
                }
                createdMonsters.Clear();
                spawnedTypes.Clear();
                spawnedCount = 0;
                locationName = "";
                break;
            }

            createdInLocation ??= Game1.currentLocation;
            createdMonsters.Add(createdMonster);
            spawnedCount++;
            spawnedTypes.Add(selectedId);
            locationName = spawnedLocation;
        }

        result = spawnedCount == requestedCount ? "SUCCESS" : "FAILED";
        return spawnedCount > 0;
    }

    private static int GetRandomCount(int minimum, int maximum)
    {
        int min = ClampCount(minimum, 1);
        int max = Math.Max(min, ClampCount(maximum, 3));
        return Random.Shared.Next(min, max + 1);
    }

    private static int ClampCount(int value, int fallback) =>
        Math.Clamp(value <= 0 ? fallback : value, 1, MaximumMonstersPerRedemption);

    private bool TrySpawnCandidate(string candidateId, out string locationName, out Monster? spawnedMonster)
    {
        locationName = "";
        spawnedMonster = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | host world not ready", LogLevel.Warn);
            return false;
        }
        if (Game1.eventUp || Game1.currentLocation is null || Game1.player is null)
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

            int before = location.characters.Count;
            if (!MonsterCatalog.TryCreateMonster(candidateId, tile * 64f, out Monster? monster, out string error) || monster is null)
            {
                this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(candidateId)} | {error}", LogLevel.Warn);
                return false;
            }

            location.characters.Add(monster);
            if (location.characters.Count <= before)
            {
                this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(candidateId)} | {location.Name} | character wasn't added", LogLevel.Warn);
                return false;
            }
            spawnedMonster = monster;
            locationName = location.Name;
            return true;
        }

        this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(candidateId)} | no clear tile", LogLevel.Warn);
        return false;
    }
}
