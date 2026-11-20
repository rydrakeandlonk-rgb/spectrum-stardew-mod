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

    public bool ShouldTriggerHorde(GameCommand command)
    {
        bool isSingleRandomPoolRequest = command.BehaviorKey switch
        {
            "difficulty_random" => true,
            "fixed_count_random_monsters" => command.Count == 1,
            "random_count_random_monsters" => command.MinCount == 1 && command.MaxCount == 1,
            _ => false
        };

        // Temporary playtest odds: one horde per 100 eligible single-monster redemptions.
        return isSingleRandomPoolRequest && Random.Shared.Next(100) == 0;
    }

    public bool TrySpawnRandomMonster(string difficulty = "normal", Farmer? targetFarmer = null)
    {
        targetFarmer ??= Game1.player;
        if (!Context.IsWorldReady || targetFarmer is null)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | load a save before selecting a random monster", LogLevel.Warn);
            return false;
        }

        return this.TrySpawnCandidate(MonsterCatalog.GetRandomType(difficulty), out _, out _, targetFarmer: targetFarmer);
    }

    public bool TrySpawnGreenSlime() =>
        MonsterCatalog.TryGetCanonicalType("Green Slime", out string id) &&
        this.TrySpawnCandidate(id, out _, out _);

    /// <summary>
    /// Executes a community roulette result near the specific participating
    /// farmer. Horde placement reuses the normal progression-aware horde path.
    /// </summary>
    public bool TrySpawnCommunityEncounter(string outcome, string difficulty, Farmer targetFarmer, out int spawnedCount, out string detail)
    {
        spawnedCount = 0;
        detail = "Unsupported community encounter.";

        if (targetFarmer is null || !Context.IsWorldReady || !Context.IsMainPlayer)
        {
            detail = "The host world or target player is unavailable.";
            return false;
        }

        if (outcome == "single")
        {
            bool succeeded = this.TrySpawnRandomMonster(difficulty, targetFarmer);
            spawnedCount = succeeded ? 1 : 0;
            detail = succeeded ? "One progression-eligible enemy spawned near the player." : "No eligible enemy or safe spawn tile was available.";
            return succeeded;
        }

        if (outcome != "horde")
            return false;

        var command = new GameCommand(
            "community-" + Guid.NewGuid().ToString("N"),
            "stardew-valley",
            "stardew.spawn_monster",
            "Community",
            "Community Roulette",
            "",
            "difficulty_random",
            "",
            "",
            difficulty,
            1,
            1,
            1
        );

        bool hordeSucceeded = this.TryExecute(
            command,
            targetFarmer,
            out spawnedCount,
            out _,
            out _,
            out _,
            out detail,
            forceHorde: true
        );

        return hordeSucceeded && spawnedCount == 10;
    }

    public bool TrySpawnMonster(string monsterType, bool ignoreProgression = false)
    {
        if (!MonsterCatalog.TryGetCanonicalType(monsterType, out string candidateId, ignoreProgression))
        {
            this.monitor.Log($"[Spectrum] TEST SPAWN FAILED | unknown, excluded, unsupported, or progression-locked monster: {monsterType}", LogLevel.Warn);
            return false;
        }

        if (ignoreProgression)
            this.monitor.Log($"[Spectrum] TEST OVERRIDE | spawning {MonsterCatalog.GetDisplayName(candidateId)} without progression gating; save data is unchanged.", LogLevel.Warn);

        return this.TrySpawnCandidate(candidateId, out _, out _, ignoreProgression);
    }

    /// <summary>
    /// Removes every currently loaded hostile Monster from the host's loaded
    /// locations. NPCs, farm animals and other non-monster characters remain.
    /// </summary>
    public int ClearLoadedMonsters()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            this.monitor.Log("[Spectrum] CLEAR FAILED | the host world is not ready", LogLevel.Warn);
            return -1;
        }

        var locations = new HashSet<GameLocation>(Game1.locations);
        if (Game1.currentLocation is not null)
            locations.Add(Game1.currentLocation);
        foreach (Farmer farmer in Game1.getOnlineFarmers())
        {
            if (farmer.currentLocation is not null)
                locations.Add(farmer.currentLocation);
        }

        int removed = 0;
        foreach (GameLocation location in locations)
        {
            List<Monster> monsters = location.characters.OfType<Monster>().ToList();
            foreach (Monster monster in monsters)
            {
                if (location.characters.Remove(monster))
                    removed++;
            }
        }

        this.monitor.Log($"[Spectrum] CLEAR | removed {removed} hostile monster(s) across {locations.Count} loaded location(s).", LogLevel.Info);
        return removed;
    }

    public bool TryExecute(
        GameCommand command,
        Farmer targetFarmer,
        out int spawnedCount,
        out int requestedCount,
        out List<string> spawnedTypes,
        out string locationName,
        out string result,
        bool forceHorde = false)
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
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty, useTestProgression: false);
                break;
            case "chat_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.UserInput, out candidateId, useTestProgression: false))
                {
                    result = "FAILED: unsupported or progression-locked chat monster";
                    return false;
                }
                count = 1;
                chooseMonster = () => candidateId;
                break;
            case "random_count_random_monsters":
                count = GetRandomCount(command.MinCount, command.MaxCount);
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty, useTestProgression: false);
                break;
            case "fixed_count_random_monsters":
                count = ClampCount(command.Count, 1);
                chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty, useTestProgression: false);
                break;
            case "random_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId, useTestProgression: false))
                {
                    result = "FAILED: unsupported or progression-locked configured monster";
                    return false;
                }
                count = GetRandomCount(command.MinCount, command.MaxCount);
                chooseMonster = () => candidateId;
                break;
            case "fixed_count_specific_monster":
                if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId, useTestProgression: false))
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

        List<string>? hordeTypes = null;
        if (forceHorde)
        {
            bool randomPoolRequest = command.BehaviorKey is "difficulty_random" or "fixed_count_random_monsters" or "random_count_random_monsters";
            if (!randomPoolRequest || count != 1)
            {
                result = "FAILED: horde events require a single-monster random-pool request";
                return false;
            }

            hordeTypes = MonsterCatalog.GetRandomDistinctTypes(10, command.Difficulty, useTestProgression: false).ToList();
            if (hordeTypes.Count != 10)
            {
                result = "FAILED: fewer than ten distinct eligible monster variants are available";
                return false;
            }

            int hordeIndex = 0;
            chooseMonster = () => hordeTypes[hordeIndex++];
            count = 10;
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
            if (!this.TrySpawnCandidate(selectedId, out string spawnedLocation, out Monster? createdMonster, useTestProgression: false, targetFarmer: targetFarmer) ||
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

            createdInLocation ??= targetFarmer.currentLocation;
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

    private bool TrySpawnCandidate(string candidateId, out string locationName, out Monster? spawnedMonster, bool ignoreProgression = false, bool useTestProgression = true, Farmer? targetFarmer = null)
    {
        locationName = "";
        spawnedMonster = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | host world not ready", LogLevel.Warn);
            return false;
        }
        targetFarmer ??= Game1.player;
        if (Game1.eventUp || targetFarmer?.currentLocation is null)
        {
            this.monitor.Log("[Spectrum] SPAWN FAILED | target farmer or location unavailable", LogLevel.Warn);
            return false;
        }

        GameLocation location = targetFarmer.currentLocation;
        Vector2 playerTile = targetFarmer.Tile;
        foreach (Point offset in NearbyTileOffsets)
        {
            Vector2 tile = new(playerTile.X + offset.X, playerTile.Y + offset.Y);
            if (!location.CanSpawnCharacterHere(tile))
                continue;

            int before = location.characters.Count;
            if (!MonsterCatalog.TryCreateMonster(candidateId, tile * 64f, out Monster? monster, out string error, ignoreProgression, useTestProgression) || monster is null)
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
