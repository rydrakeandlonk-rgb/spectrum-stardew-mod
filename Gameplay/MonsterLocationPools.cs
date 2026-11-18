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
            [MonsterPool.DangerousSkullCavern] = new[] { "dangerous-armored-bug", "dangerous-purple-slime", "royal-serpent" },
            [MonsterPool.BugLair] = new[] { "mutant-fly", "mutant-grub" },
            [MonsterPool.Farm] = new[] { "bat", "truffle-crab", "wilderness-golem", "green-slime", "blue-slime", "frost-bat", "red-slime", "iridium-golem", "lava-bat", "iridium-bat", "purple-slime", "shadow-brute", "serpent" },
            [MonsterPool.VolcanoDungeon] = new[] { "dwarvish-sentry", "false-magma-cap", "hot-head", "magma-sprite", "magma-sparker", "tiger-slime" }
        };

    public static IReadOnlyList<MonsterPool> GetUnlockedPools(MonsterProgressionSnapshot progression) =>
        DefinitionsByPool.Keys.Where(progression.IsUnlocked).OrderBy(pool => pool).ToArray();

    public static HashSet<string> GetEligibleDefinitionIds(MonsterProgressionSnapshot progression) =>
        GetUnlockedPools(progression).SelectMany(pool => DefinitionsByPool[pool]).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyDictionary<MonsterPool, string[]> GetDefinitionsByPool() => DefinitionsByPool;
}
