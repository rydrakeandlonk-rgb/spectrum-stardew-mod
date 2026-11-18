using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Monsters;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SpectrumStardewMod.Gameplay;

internal static class MonsterCatalog
{
    private enum PostConstruction { None, CopperSlime, IronSlime, TigerSlime, StickBug, IridiumGolem }

    private sealed record Candidate(
        string Id, string DisplayName, string ClassName, double ExpectedHealth,
        object? ConstructorArgument = null, bool Dangerous = false,
        PostConstruction Post = PostConstruction.None);

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mummy", "Armored Bug", "Lava Lurk", "Duggy", "Magma Duggy"
    };

    private static readonly Candidate[] Candidates =
    {
        C("green-slime", "Green Slime", "GreenSlime", 24, P(MonsterPool.Mines, MonsterPool.Farm)),
        C("blue-slime", "Blue Slime (Frost Jelly)", "GreenSlime", 106, P(MonsterPool.Mines, MonsterPool.Farm), 40),
        C("red-slime", "Red Slime (Sludge)", "GreenSlime", 205, P(MonsterPool.Mines, MonsterPool.Farm), 80),
        C("purple-slime", "Purple Slime (Sludge)", "GreenSlime", 410, P(MonsterPool.SkullCavern, MonsterPool.Farm), 121),
        C("copper-slime", "Copper Slime", "GreenSlime", 102, P(MonsterPool.QuarryMine), new Color(184, 115, 51), PostConstruction.CopperSlime),
        C("iron-slime", "Iron Slime", "GreenSlime", 205, P(MonsterPool.QuarryMine), new Color(170, 175, 185), PostConstruction.IronSlime),

        C("bat", "Bat", "Bat", 24, P(MonsterPool.Mines, MonsterPool.Farm)),
        C("frost-bat", "Frost Bat", "Bat", 36, P(MonsterPool.Mines, MonsterPool.Farm), 40),
        C("lava-bat", "Lava Bat", "Bat", 80, P(MonsterPool.Mines, MonsterPool.SkullCavern, MonsterPool.Farm), 80),
        C("iridium-bat", "Iridium Bat", "Bat", 300, P(MonsterPool.SkullCavern, MonsterPool.Farm), 171),
        C("haunted-skull", "Haunted Skull", "Bat", 80, P(MonsterPool.QuarryMine), 77377),
        C("magma-sprite", "Magma Sprite", "Bat", 100, P(MonsterPool.VolcanoDungeon), -555),
        C("magma-sparker", "Magma Sparker", "Bat", 150, P(MonsterPool.VolcanoDungeon), -556),

        C("bug", "Bug", "Bug", 22, P(MonsterPool.Mines), 0),
        C("cave-fly", "Cave Fly", "Fly", 30, P(MonsterPool.Mines), false),
        C("mutant-fly", "Mutant Fly", "Fly", 90, P(MonsterPool.BugLair), true),
        C("grub", "Grub", "Grub", 30, P(MonsterPool.Mines), false),
        C("mutant-grub", "Mutant Grub", "Grub", 150, P(MonsterPool.BugLair), true),

        C("rock-crab", "Rock Crab", "RockCrab", 60, P(MonsterPool.Mines)),
        C("lava-crab", "Lava Crab", "RockCrab", 120, P(MonsterPool.Mines), "Lava Crab"),
        C("iridium-crab", "Iridium Crab", "RockCrab", 200, P(MonsterPool.SkullCavern), "Iridium Crab"),
        C("truffle-crab", "Truffle Crab", "RockCrab", 200, P(MonsterPool.Farm), "Truffle Crab"),
        C("false-magma-cap", "False Magma Cap", "RockCrab", 120, P(MonsterPool.VolcanoDungeon), "False Magma Cap"),
        C("stick-bug", "Stick Bug", "RockCrab", 180, P(MonsterPool.DangerousMines), null, PostConstruction.StickBug),

        C("stone-golem", "Stone Golem", "RockGolem", 45, P(MonsterPool.Mines)),
        C("wilderness-golem", "Wilderness Golem", "RockGolem", 120, P(MonsterPool.Farm), 0),
        C("iridium-golem", "Iridium Golem", "RockGolem", 500, P(MonsterPool.Farm), 0, PostConstruction.IridiumGolem),

        C("dust-spirit", "Dust Sprite", "DustSpirit", 40, P(MonsterPool.Mines)),
        C("ghost", "Ghost", "Ghost", 50, P(MonsterPool.Mines)),
        C("carbon-ghost", "Carbon Ghost", "Ghost", 100, P(MonsterPool.DangerousMines, MonsterPool.SkullCavern), "Carbon Ghost"),
        C("putrid-ghost", "Putrid Ghost", "Ghost", 200, P(MonsterPool.DangerousMines), "Putrid Ghost"),
        C("skeleton", "Skeleton", "Skeleton", 70, P(MonsterPool.Mines), false),
        C("skeleton-mage", "Skeleton Mage", "Skeleton", 90, P(MonsterPool.DangerousMines), true),
        C("spider", "Spider", "Leaper", 150, P(MonsterPool.DangerousMines)),
        C("shadow-brute", "Shadow Brute", "ShadowBrute", 160, P(MonsterPool.Mines, MonsterPool.Farm)),
        C("shadow-shaman", "Shadow Shaman", "ShadowShaman", 140, P(MonsterPool.Mines)),
        C("shadow-sniper", "Shadow Sniper", "Shooter", 160, P(MonsterPool.DangerousMines), "Shadow Sniper"),
        C("metal-head", "Metal Head", "MetalHead", 120, P(MonsterPool.Mines), 80),
        C("squid-kid", "Squid Kid", "SquidKid", 120, P(MonsterPool.Mines)),
        C("blue-squid", "Blue Squid", "BlueSquid", 120, P(MonsterPool.DangerousMines)),
        C("big-slime", "Big Slime", "BigSlime", 180, P(MonsterPool.SkullCavern), 121),
        C("pepper-rex", "Pepper Rex", "DinoMonster", 500, P(MonsterPool.SkullCavern)),
        C("serpent", "Serpent", "Serpent", 150, P(MonsterPool.SkullCavern, MonsterPool.Farm)),
        C("royal-serpent", "Royal Serpent", "Serpent", 300, P(MonsterPool.DangerousSkullCavern), "Royal Serpent"),
        C("dwarvish-sentry", "Dwarvish Sentry", "DwarvishSentry", 100, P(MonsterPool.VolcanoDungeon)),
        C("hot-head", "Hot Head", "HotHead", 200, P(MonsterPool.VolcanoDungeon)),
        C("tiger-slime", "Tiger Slime", "GreenSlime", 415, P(MonsterPool.VolcanoDungeon), null, PostConstruction.TigerSlime),

        C("dangerous-green-slime", "Dangerous Green Slime", "GreenSlime", 300, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-blue-slime", "Dangerous Blue Slime", "GreenSlime", 350, P(MonsterPool.DangerousMines), 40, PostConstruction.None, true),
        C("dangerous-red-slime", "Dangerous Red Slime", "GreenSlime", 450, P(MonsterPool.DangerousMines), 80, PostConstruction.None, true),
        C("dangerous-purple-slime", "Dangerous Purple Slime", "GreenSlime", 650, P(MonsterPool.DangerousMines, MonsterPool.DangerousSkullCavern), 121, PostConstruction.None, true),
        C("dangerous-copper-slime", "Dangerous Copper Slime", "GreenSlime", 350, P(MonsterPool.DangerousMines), new Color(184, 115, 51), PostConstruction.CopperSlime, true),
        C("dangerous-iron-slime", "Dangerous Iron Slime", "GreenSlime", 450, P(MonsterPool.DangerousMines), new Color(170, 175, 185), PostConstruction.IronSlime, true),
        C("dangerous-bat", "Dangerous Bat", "Bat", 300, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-haunted-skull", "Dangerous Haunted Skull", "Bat", 350, P(MonsterPool.DangerousMines), 77377, PostConstruction.None, true),
        C("dangerous-bug", "Dangerous Bug", "Bug", 300, P(MonsterPool.DangerousMines), 0, PostConstruction.None, true),
        C("dangerous-cave-fly", "Dangerous Cave Fly", "Fly", 300, P(MonsterPool.DangerousMines), false, PostConstruction.None, true),
        C("dangerous-grub", "Dangerous Grub", "Grub", 300, P(MonsterPool.DangerousMines), false, PostConstruction.None, true),
        C("dangerous-dust-spirit", "Dangerous Dust Sprite", "DustSpirit", 300, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-frost-bat", "Dangerous Frost Bat", "Bat", 320, P(MonsterPool.DangerousMines), 40, PostConstruction.None, true),
        C("dangerous-rock-crab", "Dangerous Rock Crab", "RockCrab", 350, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-lava-crab", "Dangerous Lava Crab", "RockCrab", 400, P(MonsterPool.DangerousMines), "Lava Crab", PostConstruction.None, true),
        C("dangerous-stone-golem", "Dangerous Stone Golem", "RockGolem", 300, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-metal-head", "Dangerous Metal Head", "MetalHead", 400, P(MonsterPool.DangerousMines), 80, PostConstruction.None, true),
        C("dangerous-shadow-brute", "Dangerous Shadow Brute", "ShadowBrute", 450, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-shadow-shaman", "Dangerous Shadow Shaman", "ShadowShaman", 400, P(MonsterPool.DangerousMines), null, PostConstruction.None, true),
        C("dangerous-skeleton", "Dangerous Skeleton", "Skeleton", 350, P(MonsterPool.DangerousMines), false, PostConstruction.None, true),
        C("dangerous-squid-kid", "Dangerous Squid Kid", "SquidKid", 400, P(MonsterPool.DangerousMines, MonsterPool.DangerousSkullCavern), null, PostConstruction.None, true)
    };

    private static readonly IReadOnlyDictionary<string, Candidate> CandidatesById =
        Candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);

    public static IReadOnlyList<string> AllTypes => Candidates.Select(candidate => candidate.DisplayName).ToArray();
    public static IReadOnlyList<string> GetEligibleTypes() => GetEligibleCandidates().Select(candidate => candidate.DisplayName).ToArray();

    public static IReadOnlyList<string> GetCatalogValidationIssues()
    {
        var issues = new List<string>();
        IReadOnlyDictionary<MonsterPool, string[]> pools = MonsterLocationPools.GetDefinitionsByPool();
        HashSet<string> pooledIds = pools.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);

        foreach (string id in pooledIds.Where(id => !CandidatesById.ContainsKey(id)))
            issues.Add($"Pool references missing definition: {id}");

        foreach (Candidate candidate in Candidates)
        {
            if (!pooledIds.Contains(candidate.Id))
                issues.Add($"Definition isn't assigned to a location pool: {candidate.Id}");
            if (ExcludedNames.Contains(candidate.DisplayName) || ExcludedNames.Contains(candidate.ClassName))
                issues.Add($"Excluded monster entered the catalogue: {candidate.DisplayName}");
            if (!HasSupportedDefinition(candidate))
                issues.Add($"Missing constructor or setup support: {candidate.DisplayName} ({candidate.ClassName})");
        }

        return issues.OrderBy(issue => issue, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string GetDisplayName(string typeName) =>
        FindCandidate(typeName)?.DisplayName ?? Regex.Replace(typeName, "([a-z0-9])([A-Z])", "$1 $2");

    public static string GetRandomType(string difficulty = "normal")
    {
        Candidate[] eligible = GetEligibleCandidates().ToArray();
        if (eligible.Length == 0)
            throw new InvalidOperationException("No progression-eligible monster definitions are available.");

        double exponent = difficulty.Trim().ToLowerInvariant() switch
        {
            "easy" => 1.8d,
            "hard" => 0.9d,
            _ => 1.35d
        };
        double totalWeight = eligible.Sum(candidate => GetWeight(candidate.ExpectedHealth * (candidate.Dangerous ? 1.5d : 1d), exponent));
        double roll = Random.Shared.NextDouble() * totalWeight;
        foreach (Candidate candidate in eligible)
        {
            roll -= GetWeight(candidate.ExpectedHealth * (candidate.Dangerous ? 1.5d : 1d), exponent);
            if (roll <= 0)
                return candidate.Id;
        }
        return eligible[^1].Id;
    }

    public static bool TryGetCanonicalType(string? value, out string monsterType)
    {
        Candidate? candidate = FindCandidate(value);
        bool eligible = candidate is not null && GetEligibleCandidates().Any(item => item.Id == candidate.Id);
        monsterType = eligible ? candidate!.Id : "";
        return eligible;
    }

    public static bool TryCreateMonster(string candidateId, Vector2 position, out Monster? monster, out string error)
    {
        monster = null;
        error = "";
        if (!CandidatesById.TryGetValue(candidateId, out Candidate? candidate))
        {
            error = "Unknown spawn definition.";
            return false;
        }
        if (!GetEligibleCandidates().Any(item => item.Id == candidate.Id))
        {
            error = "Monster is not unlocked in this save.";
            return false;
        }
        if (!TryGetConstructor(candidate, out ConstructorInfo? constructor))
        {
            error = $"No supported vanilla constructor for {candidate.DisplayName}.";
            return false;
        }

        try
        {
            object? constructorArgument = candidate.Id == "wilderness-golem"
                ? Game1.player?.CombatLevel ?? 0
                : candidate.ConstructorArgument;
            object?[] args = constructorArgument is null
                ? new object?[] { position }
                : new object?[] { position, constructorArgument };
            monster = constructor!.Invoke(args) as Monster;
            if (monster is null)
            {
                error = $"Constructor did not create a Monster for {candidate.DisplayName}.";
                return false;
            }
            ApplyPostConstruction(candidate, monster);
            if (candidate.Dangerous)
                ApplyDangerousVariant(monster);
            return true;
        }
        catch (Exception ex)
        {
            monster = null;
            error = $"{candidate.DisplayName} construction failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static Candidate[] GetEligibleCandidates() =>
        GetUnlockedCandidates().Where(HasSupportedDefinition).ToArray();

    private static Candidate[] GetUnlockedCandidates()
    {
        if (Game1.player is null)
            return Array.Empty<Candidate>();
        HashSet<string> ids = MonsterLocationPools.GetEligibleDefinitionIds(MonsterProgressionSnapshot.Read(Game1.player));
        return Candidates.Where(candidate =>
            ids.Contains(candidate.Id) &&
            !ExcludedNames.Contains(candidate.DisplayName) &&
            !ExcludedNames.Contains(candidate.ClassName))
            .ToArray();
    }

    private static Candidate? FindCandidate(string? value)
    {
        string normalized = Normalize(value);
        if (normalized.Length == 0)
            return null;

        Candidate? exact = Candidates.FirstOrDefault(candidate =>
            Normalize(candidate.Id) == normalized ||
            Normalize(candidate.DisplayName) == normalized ||
            Normalize(candidate.DisplayName.Split('(')[0].Trim()) == normalized ||
            Normalize(candidate.ClassName) == normalized);
        if (exact is not null)
            return exact;

        Candidate[] aliases = Candidates.Where(candidate =>
        {
            string alias = Normalize(GetParentheticalAlias(candidate.DisplayName));
            return alias.Length > 0 && alias == normalized;
        }).ToArray();

        // Avoid silently mapping ambiguous aliases such as "Sludge" to one slime colour.
        return aliases.Length == 1 ? aliases[0] : null;
    }

    private static bool HasSupportedDefinition(Candidate candidate)
    {
        if (!TryGetConstructor(candidate, out _))
            return false;

        return candidate.Post switch
        {
            PostConstruction.None => true,
            PostConstruction.CopperSlime or PostConstruction.IronSlime =>
                typeof(GreenSlime).GetField("color", BindingFlags.Instance | BindingFlags.Public) is not null &&
                typeof(Monster).GetMethod("parseMonsterInfo", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            PostConstruction.TigerSlime => typeof(GreenSlime).GetMethod("makeTigerSlime") is not null,
            PostConstruction.StickBug => typeof(RockCrab).GetMethod("makeStickBug") is not null,
            PostConstruction.IridiumGolem =>
                typeof(Monster).GetMethod("parseMonsterInfo", BindingFlags.Instance | BindingFlags.NonPublic) is not null,
            _ => false
        };
    }

    private static bool TryGetConstructor(Candidate candidate, out ConstructorInfo? constructor)
    {
        Type? type = typeof(Monster).Assembly.GetType($"StardewValley.Monsters.{candidate.ClassName}");
        constructor = null;
        if (type is null || !typeof(Monster).IsAssignableFrom(type))
            return false;
        Type[] signature = candidate.ConstructorArgument is null
            ? new[] { typeof(Vector2) }
            : new[] { typeof(Vector2), candidate.ConstructorArgument.GetType() };
        constructor = type.GetConstructor(signature);
        return constructor is not null;
    }

    private static void ApplyPostConstruction(Candidate candidate, Monster monster)
    {
        switch (candidate.Post)
        {
            case PostConstruction.CopperSlime:
                ConfigureNamedSlime((GreenSlime)monster, "Copper Slime");
                break;
            case PostConstruction.IronSlime:
                ConfigureNamedSlime((GreenSlime)monster, "Iron Slime");
                break;
            case PostConstruction.TigerSlime:
                ((GreenSlime)monster).makeTigerSlime();
                break;
            case PostConstruction.StickBug:
                ((RockCrab)monster).makeStickBug();
                break;
            case PostConstruction.IridiumGolem:
                ConfigureIridiumGolem((RockGolem)monster);
                break;
        }
    }

    private static void ConfigureNamedSlime(GreenSlime slime, string name)
    {
        slime.Name = name;
        ParseMonsterInfo(slime, name);
        slime.reloadSprite();
    }

    private static void ConfigureIridiumGolem(RockGolem golem)
    {
        golem.Name = "Iridium Golem";
        ParseMonsterInfo(golem, "Iridium Golem");
        golem.reloadSprite();
        golem.Speed *= 2;
        golem.Health += 400;
        golem.DamageToFarmer += 10;
        golem.ExperienceGained += 10;
    }

    private static void ParseMonsterInfo(Monster monster, string name)
    {
        MethodInfo? method = typeof(Monster).GetMethod("parseMonsterInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(monster, new object[] { name });
    }

    private static void ApplyDangerousVariant(Monster monster)
    {
        monster.BuffForAdditionalDifficulty(1);
        string assetName = monster.Sprite.textureName + "_dangerous";
        if (!Game1.content.DoesAssetExist<Texture2D>(assetName))
            return;
        try { monster.Sprite.LoadTexture(assetName); }
        catch
        {
            // Keep the dangerous stat variant even if its optional texture fails to load.
        }
    }

    private static Candidate C(string id, string display, string type, double health, MonsterPool[] pools,
        object? argument = null, PostConstruction post = PostConstruction.None, bool dangerous = false) =>
        new(id, display, type, health, argument, dangerous, post);

    private static MonsterPool[] P(params MonsterPool[] pools) => pools;
    private static string GetParentheticalAlias(string value)
    {
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        return open >= 0 && close > open ? value.Substring(open + 1, close - open - 1).Trim() : "";
    }

    private static string Normalize(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).ToArray());
    private static double GetWeight(double health, double exponent) => 1d / Math.Pow(Math.Max(1d, health), exponent);
}
