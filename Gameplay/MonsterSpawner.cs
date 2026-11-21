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
    private readonly Dictionary<string, PendingSpawnPlan> pendingPlans = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<MonsterPool, string> poolOverrides = new Dictionary<MonsterPool, string>();

    private sealed class PendingSpawnPlan
    {
        public List<string> RemainingTypes { get; init; } = new();
        public int RequestedCount { get; init; }
        public int SpawnedCount { get; set; }
        public List<string> SpawnedTypes { get; } = new();
        public string LocationName { get; set; } = "";
    }

    public MonsterSpawner(IMonitor monitor) => this.monitor = monitor;

    public void ClearPendingPlans() => this.pendingPlans.Clear();

    public IReadOnlyDictionary<MonsterPool, string> PoolOverrides => this.poolOverrides;

    public void SetPoolOverrides(IReadOnlyDictionary<MonsterPool, string> overrides)
    {
        this.poolOverrides = overrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        this.pendingPlans.Clear();
    }

    public IReadOnlyList<PoolDiagnostic> GetPoolDiagnostics(MonsterProgressionSnapshot progression) =>
        MonsterLocationPools.GetDiagnostics(progression, this.poolOverrides);

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

        return this.TrySpawnCandidate(MonsterCatalog.GetRandomType(difficulty, poolOverrides: this.poolOverrides), out _, out _, targetFarmer: targetFarmer);
    }

    public bool TrySpawnGreenSlime() =>
        MonsterCatalog.TryGetCanonicalType("Green Slime", out string id, poolOverrides: this.poolOverrides) &&
        this.TrySpawnCandidate(id, out _, out _);

    /// <summary>
    /// Executes a community roulette result near the specific participating
    /// farmer. Horde placement reuses the normal progression-aware horde path.
    /// </summary>
    public bool TrySpawnCommunityEncounter(string outcome, string difficulty, Farmer targetFarmer, out int spawnedCount, out string detail, string actionId = "", int monsterCount = 10)
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

        if (outcome is not ("horde" or "mega"))
            return false;

        var command = new GameCommand(
            string.IsNullOrWhiteSpace(actionId) ? "community-" + Guid.NewGuid().ToString("N") : actionId,
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

        int hordeCount = Math.Clamp(monsterCount, 1, 50);
        bool hordeSucceeded = this.TryExecute(
            command,
            targetFarmer,
            out spawnedCount,
            out _,
            out _,
            out _,
            out detail,
            forceHorde: true,
            hordeCount: hordeCount
        );

        return hordeSucceeded && spawnedCount == hordeCount;
    }

    public bool TrySpawnMonster(string monsterType, bool ignoreProgression = false)
    {
        if (!MonsterCatalog.TryGetCanonicalType(monsterType, out string candidateId, ignoreProgression, poolOverrides: this.poolOverrides))
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
        bool forceHorde = false,
        int hordeCount = 10)
    {
        spawnedCount = 0;
        requestedCount = 0;
        spawnedTypes = new List<string>();
        locationName = "";
        result = "FAILED: unsupported spawn behaviour";

        if (!Context.IsWorldReady || !Context.IsMainPlayer || targetFarmer?.currentLocation is null)
        {
            result = "FAILED: host world not ready";
            return false;
        }

        if (!this.pendingPlans.TryGetValue(command.CommandId, out PendingSpawnPlan? plan))
        {
            int count;
            string candidateId;
            Func<string> chooseMonster;

            switch (command.BehaviorKey)
            {
                case "difficulty_random":
                    count = 1;
                    chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty, useTestProgression: false, poolOverrides: this.poolOverrides);
                    break;
                case "chat_monster":
                    if (!MonsterCatalog.TryGetCanonicalType(command.UserInput, out candidateId, useTestProgression: false, poolOverrides: this.poolOverrides))
                    {
                        result = "FAILED: unsupported or progression-locked chat monster";
                        return false;
                    }
                    count = 1;
                    chooseMonster = () => candidateId;
                    break;
                case "random_count_random_monsters":
                    count = GetRandomCount(command.MinCount, command.MaxCount);
                    chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty, useTestProgression: false, poolOverrides: this.poolOverrides);
                    break;
                case "fixed_count_random_monsters":
                    count = ClampCount(command.Count, 1);
                    chooseMonster = () => MonsterCatalog.GetRandomType(command.Difficulty, useTestProgression: false, poolOverrides: this.poolOverrides);
                    break;
                case "random_count_specific_monster":
                    if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId, useTestProgression: false, poolOverrides: this.poolOverrides))
                    {
                        result = "FAILED: unsupported or progression-locked configured monster";
                        return false;
                    }
                    count = GetRandomCount(command.MinCount, command.MaxCount);
                    chooseMonster = () => candidateId;
                    break;
                case "fixed_count_specific_monster":
                    if (!MonsterCatalog.TryGetCanonicalType(command.MonsterType, out candidateId, useTestProgression: false, poolOverrides: this.poolOverrides))
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

            List<string> selectedTypes;
            if (forceHorde)
            {
                bool randomPoolRequest = command.BehaviorKey is "difficulty_random" or "fixed_count_random_monsters" or "random_count_random_monsters";
                if (!randomPoolRequest || count != 1)
                {
                    result = "FAILED: horde events require a single-monster random-pool request";
                    return false;
                }

                int safeHordeCount = Math.Clamp(hordeCount, 1, 50);
                selectedTypes = MonsterCatalog.GetRandomDistinctTypes(safeHordeCount, command.Difficulty, useTestProgression: false, poolOverrides: this.poolOverrides).ToList();
                if (selectedTypes.Count != safeHordeCount)
                {
                    result = $"FAILED: fewer than {safeHordeCount} distinct eligible monster variants are available";
                    return false;
                }
            }
            else
            {
                selectedTypes = Enumerable.Range(0, Math.Clamp(count, 1, MaximumMonstersPerRedemption))
                    .Select(_ => chooseMonster()).ToList();
            }

            plan = new PendingSpawnPlan { RemainingTypes = selectedTypes, RequestedCount = selectedTypes.Count };
            this.pendingPlans[command.CommandId] = plan;
        }

        requestedCount = plan.RequestedCount;
        GameLocation location = targetFarmer.currentLocation;
        var nearby = new List<Vector2>();
        Vector2 playerTile = targetFarmer.Tile;
        foreach (Point offset in NearbyTileOffsets)
        {
            Vector2 tile = new(playerTile.X + offset.X, playerTile.Y + offset.Y);
            if (location.CanSpawnCharacterHere(tile))
                nearby.Add(tile);
        }

        // Build one bounded candidate list for this attempt. Nearby tiles are
        // preferred; the remainder of the current map is considered only once.
        var mapWide = new List<Vector2>();
        int width = location.map?.Layers?.Count > 0 ? location.map.Layers[0].LayerWidth : 0;
        int height = location.map?.Layers?.Count > 0 ? location.map.Layers[0].LayerHeight : 0;
        if (width > 0 && height > 0)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var tile = new Vector2(x, y);
                if (location.CanSpawnCharacterHere(tile) && !nearby.Contains(tile))
                    mapWide.Add(tile);
            }
        }
        // Randomise the fallback so large groups don't always occupy the same
        // edge of the map. Revalidate each tile immediately before placement.
        for (int i = mapWide.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (mapWide[i], mapWide[j]) = (mapWide[j], mapWide[i]);
        }

        var candidates = new Queue<Vector2>(nearby.Concat(mapWide));
        while (plan.RemainingTypes.Count > 0 && candidates.Count > 0)
        {
            Vector2 tile = candidates.Dequeue();
            if (!location.CanSpawnCharacterHere(tile))
                continue;

            string selectedId = plan.RemainingTypes[0];
            if (!MonsterCatalog.TryCreateMonster(selectedId, tile * 64f, out Monster? monster, out string error, ignoreProgression: false, useTestProgression: false, poolOverrides: this.poolOverrides) || monster is null)
            {
                this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(selectedId)} | {error}", LogLevel.Warn);
                plan.RemainingTypes.RemoveAt(0);
                continue;
            }

            int before = location.characters.Count;
            location.characters.Add(monster);
            if (location.characters.Count <= before)
                continue;

            plan.RemainingTypes.RemoveAt(0);
            plan.SpawnedCount++;
            plan.SpawnedTypes.Add(selectedId);
            plan.LocationName = location.Name;
        }

        spawnedCount = plan.SpawnedCount;
        spawnedTypes = plan.SpawnedTypes.ToList();
        locationName = plan.LocationName;
        if (plan.RemainingTypes.Count == 0)
        {
            this.pendingPlans.Remove(command.CommandId);
            result = "SUCCESS";
            return true;
        }

        result = spawnedCount > 0
            ? $"PENDING: {plan.RemainingTypes.Count} monster(s) remain; waiting for another valid placement opportunity"
            : "PENDING: no valid spawn tiles are currently available";
        return false;
    }

    private static int GetRandomCount(int minimum, int maximum)
    {
        int min = ClampCount(minimum, 1);
        int max = Math.Max(min, ClampCount(maximum, 3));
        return Random.Shared.Next(min, max + 1);
    }

    private static int ClampCount(int value, int fallback) =>
        Math.Clamp(value <= 0 ? fallback : value, 1, MaximumMonstersPerRedemption);

    private bool TryCreateAtTile(
        GameLocation location,
        string candidateId,
        Vector2 tile,
        out Monster? spawnedMonster,
        out string error,
        bool ignoreProgression,
        bool useTestProgression)
    {
        spawnedMonster = null;
        error = "";
        if (!location.CanSpawnCharacterHere(tile))
            return false;

        if (!MonsterCatalog.TryCreateMonster(candidateId, tile * 64f, out Monster? monster, out error, ignoreProgression, useTestProgression, this.poolOverrides) || monster is null)
        {
            this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(candidateId)} | {error}", LogLevel.Warn);
            return false;
        }

        int before = location.characters.Count;
        location.characters.Add(monster);
        if (location.characters.Count <= before)
        {
            this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(candidateId)} | {location.Name} | character wasn't added", LogLevel.Warn);
            return false;
        }

        spawnedMonster = monster;
        return true;
    }

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
            if (this.TryCreateAtTile(location, candidateId, tile, out spawnedMonster, out string error, ignoreProgression, useTestProgression))
            {
                locationName = location.Name;
                return true;
            }
        }

        // Single-monster features use the same nearby-first, same-map fallback
        // as group encounters. The map is scanned once for this request.
        int width = location.map?.Layers?.Count > 0 ? location.map.Layers[0].LayerWidth : 0;
        int height = location.map?.Layers?.Count > 0 ? location.map.Layers[0].LayerHeight : 0;
        var fallbackTiles = new List<Vector2>();
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var tile = new Vector2(x, y);
            if (location.CanSpawnCharacterHere(tile))
                fallbackTiles.Add(tile);
        }
        for (int i = fallbackTiles.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (fallbackTiles[i], fallbackTiles[j]) = (fallbackTiles[j], fallbackTiles[i]);
        }
        foreach (Vector2 tile in fallbackTiles)
        {
            if (this.TryCreateAtTile(location, candidateId, tile, out spawnedMonster, out _, ignoreProgression, useTestProgression))
            {
                locationName = location.Name;
                return true;
            }
        }

        this.monitor.Log($"[Spectrum] SPAWN FAILED | {MonsterCatalog.GetDisplayName(candidateId)} | no valid tile anywhere on the current map", LogLevel.Warn);
        return false;
    }
}
