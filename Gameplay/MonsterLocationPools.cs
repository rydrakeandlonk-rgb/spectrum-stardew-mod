namespace SpectrumStardewMod.Gameplay;

internal static class MonsterLocationPools
{
    private static readonly IReadOnlyDictionary<MonsterPool, string[]> DefinitionsByPool =
        new Dictionary<MonsterPool, string[]>
        {
            [MonsterPool.Mines] = new[] { "green-slime", "bat", "bug", "cave-fly", "grub", "rock-crab", "stone-golem", "dust-spirit", "frost-bat", "blue-slime", "ghost", "skeleton", "lava-bat", "lava-crab", "metal-head", "red-slime", "shadow-brute", "shadow-shaman", "squid-kid" },
            [MonsterPool.DangerousMines] = new[] { "dangerous-green-slime", "dangerous-blue-slime", "dangerous-red-slime", "dangerous-purple-slime", "dangerous-copper-slime", "dangerous-iron-slime", "dangerous-haunted-skull", "blue-squid", "dangerous-bug", "dangerous-bat", "dangerous-cave-fly", "dangerous-grub", "dangerous-dust-spirit", "dangerous-frost-bat", "carbon-ghost", "putrid-ghost", "spider", "stick-bug", "dangerous-skeleton", "skeleton-mage", "dangerous-lava-crab", "dangerous-metal-head", "dangerous-shadow-brute", "dangerous-shadow-shaman", "shadow-sniper", "dangerous-squid-kid", "dangerous-stone-golem", "dangerous-rock-crab", "lava-bat" },
            [MonsterPool.QuarryMine] = new[] { "copper-slime", "haunted-skull", "iron-slime" },
            [MonsterPool.SkullCavern] = new[] { "big-slime", "carbon-ghost", "iridium-bat", "iridium-crab", "lava-bat", "pepper-rex", "purple-slime", "serpent" },
            [MonsterPool.DangerousSkullCavern] = new[] { "dangerous-purple-slime", "royal-serpent" },
            [MonsterPool.BugLair] = new[] { "mutant-fly", "mutant-grub" },
            [MonsterPool.Farm] = new[] { "bat", "truffle-crab", "wilderness-golem", "green-slime", "blue-slime", "frost-bat", "red-slime", "iridium-golem", "lava-bat", "shadow-brute" },
            [MonsterPool.VolcanoDungeon] = new[] { "dwarvish-sentry", "false-magma-cap", "hot-head", "magma-sprite", "magma-sparker", "tiger-slime" }
        };

    public static IReadOnlyList<MonsterPool> GetExpectedPools(MonsterProgressionSnapshot progression) =>
        DefinitionsByPool.Keys.Where(progression.IsUnlocked).OrderBy(pool => pool).ToArray();

    public static IReadOnlyList<MonsterPool> GetUnlockedPools(
        MonsterProgressionSnapshot progression,
        IReadOnlyDictionary<MonsterPool, string>? overrides = null) =>
        DefinitionsByPool.Keys
            .Where(pool => IsEffectivelyEnabled(pool, progression, overrides))
            .OrderBy(pool => pool)
            .ToArray();

    public static bool IsEffectivelyEnabled(
        MonsterPool pool,
        MonsterProgressionSnapshot progression,
        IReadOnlyDictionary<MonsterPool, string>? overrides = null)
    {
        string mode = overrides is not null && overrides.TryGetValue(pool, out string? configured)
            ? configured
            : "automatic";

        return mode switch
        {
            "force_enabled" => true,
            "force_disabled" => false,
            _ => progression.IsUnlocked(pool)
        };
    }

    public static HashSet<string> GetEligibleDefinitionIds(
        MonsterProgressionSnapshot progression,
        IReadOnlyDictionary<MonsterPool, string>? overrides = null) =>
        GetUnlockedPools(progression, overrides)
            .SelectMany(pool => DefinitionsByPool[pool])
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<PoolDiagnostic> GetDiagnostics(
        MonsterProgressionSnapshot progression,
        IReadOnlyDictionary<MonsterPool, string>? overrides = null)
    {
        return DefinitionsByPool.Keys.OrderBy(pool => pool).Select(pool =>
        {
            bool expected = progression.IsUnlocked(pool);
            string mode = overrides is not null && overrides.TryGetValue(pool, out string? configured)
                ? configured
                : "automatic";
            bool effective = IsEffectivelyEnabled(pool, progression, overrides);
            string reason = mode switch
            {
                "force_enabled" when !expected => "Manually enabled; progression requirement is not yet met.",
                "force_disabled" when expected => "Manually disabled despite progression being unlocked.",
                "force_disabled" => "Manually disabled.",
                "force_enabled" => "Manually enabled.",
                _ when expected => "Progression requirements are satisfied.",
                _ => GetLockedReason(pool, progression)
            };

            return new PoolDiagnostic(
                pool.ToString(),
                expected,
                effective,
                mode,
                reason,
                DefinitionsByPool[pool].Length
            );
        }).ToArray();
    }

    private static string GetLockedReason(MonsterPool pool, MonsterProgressionSnapshot progression) =>
        pool switch
        {
            MonsterPool.DangerousMines => "Danger in the Deep has not been completed.",
            MonsterPool.QuarryMine => "The Quarry bridge has not been repaired.",
            MonsterPool.SkullCavern => "The host save has not obtained the Skull Key.",
            MonsterPool.DangerousSkullCavern => "Skull Cavern Invasion has not been completed.",
            MonsterPool.BugLair => "The Mutant Bug Lair passage has not been opened.",
            MonsterPool.VolcanoDungeon => "Ginger Island has not been unlocked.",
            _ => "The host save does not meet this pool's progression requirement."
        };

    public static IReadOnlyList<string> GetPoolNamesForDefinition(string monsterId) =>
        DefinitionsByPool
            .Where(pair => pair.Value.Contains(monsterId, StringComparer.Ordinal))
            .Select(pair => pair.Key.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyDictionary<MonsterPool, string[]> GetDefinitionsByPool() => DefinitionsByPool;
}
