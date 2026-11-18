namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Vanilla monster class families grouped by the location in which Stardew
/// normally spawns them. The groups are unlock gates, not spawn-location limits:
/// all unlocked groups are combined into one global random pool.
/// 
/// Variant-specific construction (for example dangerous forms, named bats,
/// crabs, ghosts and slimes) is handled separately from these class families.
/// </summary>
internal static class MonsterLocationPools
{
    private static readonly IReadOnlyDictionary<MonsterPool, string[]> TypesByPool =
        new Dictionary<MonsterPool, string[]>
        {
            [MonsterPool.Mines] = new[]
            {
                "Bat", "Bug", "Fly", "DustSpirit", "Ghost", "Grub",
                "MetalHead", "RockCrab", "RockGolem", "ShadowBrute",
                "ShadowShaman", "Skeleton", "GreenSlime", "SquidKid"
            },
            [MonsterPool.DangerousMines] = new[]
            {
                "Bat", "BlueSquid", "Bug", "Fly", "DustSpirit", "Ghost",
                "Grub", "MetalHead", "RockCrab", "RockGolem", "ShadowBrute",
                "ShadowShaman", "Skeleton", "GreenSlime", "Shooter",
                "Leaper", "SquidKid"
            },
            [MonsterPool.QuarryMine] = new[]
            {
                "Bat", "BigSlime", "GreenSlime", "Skeleton"
            },
            [MonsterPool.SkullCavern] = new[]
            {
                "Bat", "BigSlime", "DinoMonster", "Ghost", "GreenSlime",
                "RockCrab", "Serpent"
            },
            [MonsterPool.DangerousSkullCavern] = new[]
            {
                "GreenSlime", "Serpent"
            },
            [MonsterPool.BugLair] = new[]
            {
                "Fly", "Grub"
            },
            [MonsterPool.Farm] = new[]
            {
                "RockCrab", "RockGolem"
            },
            [MonsterPool.VolcanoDungeon] = new[]
            {
                "Bat", "DwarvishSentry", "GreenSlime", "HotHead", "RockCrab"
            }
        };

    public static IReadOnlyList<MonsterPool> GetUnlockedPools(MonsterProgressionSnapshot progression)
    {
        return TypesByPool.Keys
            .Where(progression.IsUnlocked)
            .OrderBy(pool => pool)
            .ToArray();
    }

    public static HashSet<string> GetEligibleClassNames(MonsterProgressionSnapshot progression)
    {
        return GetUnlockedPools(progression)
            .SelectMany(pool => TypesByPool[pool])
            .ToHashSet(StringComparer.Ordinal);
    }
}
