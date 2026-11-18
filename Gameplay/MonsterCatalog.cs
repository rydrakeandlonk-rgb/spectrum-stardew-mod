using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Monsters;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;

namespace SpectrumStardewMod.Gameplay;

internal static class MonsterCatalog
{
    private enum PostConstruction { None, CopperSlime, IronSlime, TigerSlime, StickBug, IridiumGolem }

    private sealed record Candidate(
        string Id, string DisplayName, string ClassName, double WeightingHealth,
        object? ConstructorArgument = null, bool Dangerous = false,
        PostConstruction Post = PostConstruction.None);

    private sealed record MonsterThreatProfile(
        double Health, double Damage, double Defense, double Speed,
        double Experience, double MissChance, bool IsGlider);

    private static readonly HashSet<string> ExcludedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mummy", "Armored Bug", "Lava Lurk", "Duggy", "Magma Duggy"
    };

    // Keep the complete slime roster explicit so a future catalogue cleanup
    // can't silently drop a colour or its dangerous counterpart.
    private static readonly string[] RequiredSlimeDefinitionIds =
    {
        "green-slime", "blue-slime", "red-slime", "purple-slime",
        "copper-slime", "iron-slime", "tiger-slime",
        "dangerous-green-slime", "dangerous-blue-slime", "dangerous-red-slime",
        "dangerous-purple-slime", "dangerous-copper-slime", "dangerous-iron-slime"
    };

    private static readonly string[] RequiredRestoredDefinitionIds =
    {
        "skeleton-mage", "dangerous-armored-bug"
    };

    private static readonly (string Id, MonsterPool Pool)[] RequiredPoolAssignments =
    {
        ("green-slime", MonsterPool.Mines),
        ("blue-slime", MonsterPool.Mines),
        ("red-slime", MonsterPool.Mines),
        ("purple-slime", MonsterPool.SkullCavern),
        ("copper-slime", MonsterPool.QuarryMine),
        ("iron-slime", MonsterPool.QuarryMine),
        ("tiger-slime", MonsterPool.VolcanoDungeon),
        ("dangerous-green-slime", MonsterPool.DangerousMines),
        ("dangerous-blue-slime", MonsterPool.DangerousMines),
        ("dangerous-red-slime", MonsterPool.DangerousMines),
        ("dangerous-purple-slime", MonsterPool.DangerousMines),
        ("dangerous-copper-slime", MonsterPool.DangerousMines),
        ("dangerous-iron-slime", MonsterPool.DangerousMines),
        ("skeleton-mage", MonsterPool.DangerousMines),
        ("dangerous-armored-bug", MonsterPool.DangerousSkullCavern),
        ("dangerous-squid-kid", MonsterPool.DangerousMines)
    };

    private static readonly (string Id, MonsterPool Pool)[] ForbiddenPoolAssignments =
    {
        ("dangerous-squid-kid", MonsterPool.DangerousSkullCavern)
    };

    private static readonly Candidate[] Candidates =
    {
        C("green-slime", "Green Slime", "GreenSlime", 24),
        C("blue-slime", "Blue Slime (Frost Jelly)", "GreenSlime", 106, 40),
        C("red-slime", "Red Slime (Sludge)", "GreenSlime", 205, 80),
        C("purple-slime", "Purple Slime (Sludge)", "GreenSlime", 410, 121),
        C("copper-slime", "Copper Slime", "GreenSlime", 102, null, PostConstruction.CopperSlime),
        C("iron-slime", "Iron Slime", "GreenSlime", 205, null, PostConstruction.IronSlime),
        C("dangerous-copper-slime", "Dangerous Copper Slime", "GreenSlime", 360, null, PostConstruction.CopperSlime, true),
        C("dangerous-iron-slime", "Dangerous Iron Slime", "GreenSlime", 520, null, PostConstruction.IronSlime, true),

        C("bat", "Bat", "Bat", 24),
        C("frost-bat", "Frost Bat", "Bat", 36, 40),
        C("lava-bat", "Lava Bat", "Bat", 80, 80),
        C("iridium-bat", "Iridium Bat", "Bat", 300, 171),
        C("haunted-skull", "Haunted Skull", "Bat", 80, 77377),
        C("magma-sprite", "Magma Sprite", "Bat", 100, -555),
        C("magma-sparker", "Magma Sparker", "Bat", 150, -556),

        C("bug", "Bug", "Bug", 22, 0),
        C("cave-fly", "Cave Fly", "Fly", 30, false),
        C("mutant-fly", "Mutant Fly", "Fly", 90, true),
        C("grub", "Grub", "Grub", 30, false),
        C("mutant-grub", "Mutant Grub", "Grub", 150, true),

        C("rock-crab", "Rock Crab", "RockCrab", 60),
        C("lava-crab", "Lava Crab", "RockCrab", 120, "Lava Crab"),
        C("iridium-crab", "Iridium Crab", "RockCrab", 200, "Iridium Crab"),
        C("truffle-crab", "Truffle Crab", "RockCrab", 200, "Truffle Crab"),
        C("false-magma-cap", "False Magma Cap", "RockCrab", 120, "False Magma Cap"),
        C("stick-bug", "Stick Bug", "RockCrab", 180, null, PostConstruction.StickBug),

        C("stone-golem", "Stone Golem", "RockGolem", 45),
        C("wilderness-golem", "Wilderness Golem", "RockGolem", 120, 0),
        C("iridium-golem", "Iridium Golem", "RockGolem", 500, 0, PostConstruction.IridiumGolem),

        C("dust-spirit", "Dust Sprite", "DustSpirit", 40),
        C("ghost", "Ghost", "Ghost", 50),
        C("carbon-ghost", "Carbon Ghost", "Ghost", 100, "Carbon Ghost"),
        C("putrid-ghost", "Putrid Ghost", "Ghost", 200, "Putrid Ghost"),
        C("skeleton", "Skeleton", "Skeleton", 70, false),
        C("skeleton-mage", "Skeleton Mage", "Skeleton", 95, true),

        C("spider", "Spider", "Leaper", 150),
        C("shadow-brute", "Shadow Brute", "ShadowBrute", 160),
        C("shadow-shaman", "Shadow Shaman", "ShadowShaman", 140),
        C("shadow-sniper", "Shadow Sniper", "Shooter", 160, "Shadow Sniper"),
        C("metal-head", "Metal Head", "MetalHead", 120, 80),
        C("squid-kid", "Squid Kid", "SquidKid", 120),
        C("blue-squid", "Blue Squid", "BlueSquid", 120),
        C("big-slime", "Big Slime", "BigSlime", 180, 121),
        C("pepper-rex", "Pepper Rex", "DinoMonster", 500),
        C("serpent", "Serpent", "Serpent", 150),
        C("royal-serpent", "Royal Serpent", "Serpent", 300, "Royal Serpent"),
        C("dwarvish-sentry", "Dwarvish Sentry", "DwarvishSentry", 100),
        C("hot-head", "Hot Head", "HotHead", 200),
        C("tiger-slime", "Tiger Slime", "GreenSlime", 415, null, PostConstruction.TigerSlime),

        C("dangerous-green-slime", "Dangerous Green Slime", "GreenSlime", 300, null, PostConstruction.None, true),
        C("dangerous-blue-slime", "Dangerous Blue Slime", "GreenSlime", 350, 40, PostConstruction.None, true),
        C("dangerous-red-slime", "Dangerous Red Slime", "GreenSlime", 450, 80, PostConstruction.None, true),
        C("dangerous-purple-slime", "Dangerous Purple Slime", "GreenSlime", 650, 121, PostConstruction.None, true),

        C("dangerous-bat", "Dangerous Bat", "Bat", 300, null, PostConstruction.None, true),
        C("dangerous-haunted-skull", "Dangerous Haunted Skull", "Bat", 350, 77377, PostConstruction.None, true),
        C("dangerous-bug", "Dangerous Bug", "Bug", 300, 0, PostConstruction.None, true),
        C("dangerous-armored-bug", "Dangerous Armored Bug", "Bug", 400, 121, PostConstruction.None, true),
        C("dangerous-cave-fly", "Dangerous Cave Fly", "Fly", 300, false, PostConstruction.None, true),
        C("dangerous-grub", "Dangerous Grub", "Grub", 300, false, PostConstruction.None, true),
        C("dangerous-dust-spirit", "Dangerous Dust Sprite", "DustSpirit", 300, null, PostConstruction.None, true),
        C("dangerous-frost-bat", "Dangerous Frost Bat", "Bat", 320, 40, PostConstruction.None, true),
        C("dangerous-rock-crab", "Dangerous Rock Crab", "RockCrab", 350, null, PostConstruction.None, true),
        C("dangerous-lava-crab", "Dangerous Lava Crab", "RockCrab", 400, "Lava Crab", PostConstruction.None, true),
        C("dangerous-stone-golem", "Dangerous Stone Golem", "RockGolem", 300, null, PostConstruction.None, true),
        C("dangerous-metal-head", "Dangerous Metal Head", "MetalHead", 400, 80, PostConstruction.None, true),
        C("dangerous-shadow-brute", "Dangerous Shadow Brute", "ShadowBrute", 450, null, PostConstruction.None, true),
        C("dangerous-shadow-shaman", "Dangerous Shadow Shaman", "ShadowShaman", 400, null, PostConstruction.None, true),
        C("dangerous-skeleton", "Dangerous Skeleton", "Skeleton", 350, false, PostConstruction.None, true),
        C("dangerous-squid-kid", "Dangerous Squid Kid", "SquidKid", 400, null, PostConstruction.None, true)
    };

    private static readonly IReadOnlyDictionary<string, Candidate> CandidatesById =
        Candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);

    private static Dictionary<string, MonsterThreatProfile>? ThreatProfiles;
    private static bool ThreatProfilesLoaded;

    public static IReadOnlyList<string> AllTypes => Candidates.Select(candidate => candidate.DisplayName).ToArray();
    public static IReadOnlyList<string> GetEligibleTypes() => GetEligibleCandidates().Select(candidate => candidate.DisplayName).ToArray();

    public static IReadOnlyList<string> GetThreatDataFallbacks()
    {
        EnsureThreatProfilesLoaded();
        return Candidates
            .Where(candidate => ThreatProfiles is null || !ThreatProfiles.ContainsKey(GetMonsterDataName(candidate)))
            .Select(candidate => $"{candidate.DisplayName} -> {GetMonsterDataName(candidate)}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetCatalogValidationIssues()
    {
        var issues = new List<string>();
        IReadOnlyDictionary<MonsterPool, string[]> pools = MonsterLocationPools.GetDefinitionsByPool();
        HashSet<string> pooledIds = pools.Values.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);

        foreach (string id in pooledIds.Where(id => !CandidatesById.ContainsKey(id)))
            issues.Add($"Pool references missing definition: {id}");

        foreach (string id in RequiredSlimeDefinitionIds.Concat(RequiredRestoredDefinitionIds))
        {
            if (!CandidatesById.ContainsKey(id))
                issues.Add($"Required roster definition is missing: {id}");
            else if (!pooledIds.Contains(id))
                issues.Add($"Required roster definition has no location pool: {id}");
        }

        foreach ((string id, MonsterPool expectedPool) in RequiredPoolAssignments)
        {
            if (!pools.TryGetValue(expectedPool, out string[]? assignedIds) || !assignedIds.Contains(id, StringComparer.Ordinal))
                issues.Add($"Required roster definition is missing from {expectedPool}: {id}");
        }

        foreach ((string id, MonsterPool forbiddenPool) in ForbiddenPoolAssignments)
        {
            if (pools.TryGetValue(forbiddenPool, out string[]? assignedIds) && assignedIds.Contains(id, StringComparer.Ordinal))
                issues.Add($"Roster definition is assigned to an unapproved pool {forbiddenPool}: {id}");
        }

        foreach (Candidate candidate in Candidates)
        {
            if (!pooledIds.Contains(candidate.Id))
                issues.Add($"Definition isn't assigned to a location pool: {candidate.Id}");
            if (IsExcludedCandidate(candidate))
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

        MonsterProgressionSnapshot progression = MonsterProgressionSnapshot.Read(Game1.player!);
        string selectedDifficulty = difficulty.Trim().ToLowerInvariant();
        double exponent = selectedDifficulty switch
        {
            "easy" => 1.8d,
            "hard" => 0.9d,
            _ => 1.35d
        };

        // Progression milestones establish the main threat target. Combat Level
        // is a smaller adjustment within that stage, not a substitute for progress.
        double targetThreat = progression.ProgressionStage switch
        {
            0 => 75d,
            1 => 155d,
            _ => 260d
        };
        targetThreat *= 1d + progression.CombatLevel * 0.025d;
        // Shrine difficulty affects selection only inside its matching generated area.
        targetThreat *= 1d + progression.CurrentAreaDifficulty * 0.12d;
        targetThreat *= selectedDifficulty switch
        {
            "easy" => 0.75d,
            "hard" => 1.35d,
            _ => 1d
        };

        double totalWeight = eligible.Sum(candidate => GetThreatWeight(candidate, targetThreat, exponent));
        double roll = Random.Shared.NextDouble() * totalWeight;
        foreach (Candidate candidate in eligible)
        {
            roll -= GetThreatWeight(candidate, targetThreat, exponent);
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
            !IsExcludedCandidate(candidate))
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

    private static bool IsExcludedCandidate(Candidate candidate)
    {
        // Dangerous forms inherit the exclusion of their base monster, except
        // Dangerous Armored Bug, which is explicitly part of the approved roster.
        if (candidate.Id == "dangerous-armored-bug")
            return false;

        string baseName = candidate.DisplayName.StartsWith("Dangerous ", StringComparison.Ordinal)
            ? candidate.DisplayName.Substring("Dangerous ".Length)
            : candidate.DisplayName;

        return ExcludedNames.Contains(baseName) || ExcludedNames.Contains(candidate.ClassName);
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
                ConfigureNamedSlime((GreenSlime)monster, "Copper Slime", new Color(184, 115, 51));
                break;
            case PostConstruction.IronSlime:
                ConfigureNamedSlime((GreenSlime)monster, "Iron Slime", new Color(170, 175, 185));
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

    private static void ConfigureNamedSlime(GreenSlime slime, string name, Color color)
    {
        // Copper and Iron Slime are custom catalogue variants, not keys in
        // vanilla Data/Monsters. Keep the stats initialized by GreenSlime's
        // supported constructor, then apply only the custom name and tint.
        // No custom-name lookup is needed; these variants inherit base stats.
        slime.Name = name;
        slime.color.Value = color;
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

    private static Candidate C(string id, string display, string type, double health,
        object? argument = null, PostConstruction post = PostConstruction.None, bool dangerous = false) =>
        new(id, display, type, health, argument, dangerous, post);

    private static string GetParentheticalAlias(string value)
    {
        int open = value.IndexOf('(');
        int close = value.LastIndexOf(')');
        return open >= 0 && close > open ? value.Substring(open + 1, close - open - 1).Trim() : "";
    }

    private static string Normalize(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).ToArray());
    private static double GetThreatWeight(Candidate candidate, double targetThreat, double exponent)
    {
        double threat = GetThreatScore(candidate) * (candidate.Dangerous ? 1.5d : 1d);
        double distance = Math.Abs(Math.Log(Math.Max(1d, threat) / Math.Max(1d, targetThreat)));
        return 1d / Math.Pow(1d + distance, exponent);
    }

    private static double GetThreatScore(Candidate candidate)
    {
        EnsureThreatProfilesLoaded();
        string dataName = GetMonsterDataName(candidate);
        if (ThreatProfiles is not null && ThreatProfiles.TryGetValue(dataName, out MonsterThreatProfile? profile))
        {
            // Health, attack, defense, speed, experience, miss chance and glider
            // behaviour all contribute. This is a selection score, not a stat edit.
            return Math.Max(1d,
                profile.Health * 0.30d +
                profile.Damage * 4d +
                profile.Defense * 5d +
                profile.Speed * 4d +
                profile.Experience * 2d +
                profile.MissChance * 20d +
                (profile.IsGlider ? 10d : 0d));
        }

        // Keep entries selectable if a game update renames or reshapes its data.
        return Math.Max(1d, candidate.WeightingHealth * 2d);
    }

    private static void EnsureThreatProfilesLoaded()
    {
        if (ThreatProfilesLoaded)
            return;

        ThreatProfilesLoaded = true;
        try
        {
            Dictionary<string, string> data = Game1.content.Load<Dictionary<string, string>>("Data/Monsters");
            var profiles = new Dictionary<string, MonsterThreatProfile>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, string raw) in data)
            {
                string[] fields = raw.Split('/');
                if (fields.Length < 14 ||
                    !TryReadNumber(fields, 0, out double health) ||
                    !TryReadNumber(fields, 1, out double damage) ||
                    !TryReadNumber(fields, 7, out double defense) ||
                    !TryReadNumber(fields, 10, out double speed) ||
                    !TryReadNumber(fields, 11, out double missChance) ||
                    !TryReadNumber(fields, 13, out double experience))
                    continue;

                // Ghost subclasses override the raw data flag and glide in-game.
                bool isGlider = fields[4].Equals("true", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Ghost", StringComparison.OrdinalIgnoreCase);
                profiles[name] = new MonsterThreatProfile(
                    health, damage, defense, speed, experience, missChance, isGlider);
            }

            ThreatProfiles = profiles;
        }
        catch
        {
            // The catalogue's estimates remain a safe fallback if the data asset changes.
            ThreatProfiles = new Dictionary<string, MonsterThreatProfile>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool TryReadNumber(string[] fields, int index, out double value) =>
        double.TryParse(fields[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string GetMonsterDataName(Candidate candidate)
    {
        string alias = GetParentheticalAlias(candidate.DisplayName);
        if (alias.Length > 0)
            return alias;

        if (candidate.Id.EndsWith("blue-slime", StringComparison.Ordinal))
            return "Frost Jelly";
        if (candidate.Id.EndsWith("red-slime", StringComparison.Ordinal) ||
            candidate.Id.EndsWith("purple-slime", StringComparison.Ordinal))
            return "Sludge";
        if (candidate.Id.EndsWith("copper-slime", StringComparison.Ordinal))
            return "Copper Slime";
        if (candidate.Id.EndsWith("iron-slime", StringComparison.Ordinal))
            return "Iron Slime";
        if (candidate.Id.EndsWith("cave-fly", StringComparison.Ordinal))
            return "Fly";
        if (candidate.Id.EndsWith("dust-spirit", StringComparison.Ordinal))
            return "Dust Spirit";

        string name = candidate.DisplayName.StartsWith("Dangerous ", StringComparison.Ordinal)
            ? candidate.DisplayName.Substring("Dangerous ".Length)
            : candidate.DisplayName;

        // Several catalogue entries are runtime variants of another vanilla
        // monster record rather than independent Data/Monsters entries.
        return name switch
        {
            "Dust Sprite" => "Dust Spirit",
            "Cave Fly" => "Fly",
            "Haunted Skull" => "Bat",
            "Stick Bug" => "Rock Crab",
            "Truffle Crab" => "Rock Crab",
            "Royal Serpent" => "Serpent",
            "Skeleton" => "Skeleton Warrior",
            "Dangerous Skeleton" => "Skeleton Warrior",
            _ => name
        };
    }
}
