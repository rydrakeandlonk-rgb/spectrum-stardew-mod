using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Monsters;
using System.Text.RegularExpressions;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Discovers vanilla monster classes supported by the built-in Vector2
/// constructor, then filters them through the active save's unlocked pools.
/// Variant-specific construction is being added as a separate layer.
/// </summary>
internal static class MonsterCatalog
{
    private sealed record Candidate(string TypeName, double ExpectedHealth);

    // Approximate balancing values for familiar monsters. Newly discovered
    // vanilla types use a neutral fallback until balancing data is added.
    private static readonly IReadOnlyDictionary<string, double> ExpectedHealthByType =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Bat"] = 15,
            ["DinoMonster"] = 500,
            ["DustSpirit"] = 40,
            ["Fly"] = 30,
            ["Ghost"] = 50,
            ["GreenSlime"] = 24,
            ["Grub"] = 30,
            ["RockCrab"] = 60,
            ["RockGolem"] = 80,
            ["Serpent"] = 150,
            ["ShadowBrute"] = 160,
            ["ShadowShaman"] = 140,
            ["SquidKid"] = 120
        };

    private static readonly Candidate[] Candidates = DiscoverVanillaMonsters();

    public static IReadOnlyList<string> AllTypes =>
        Candidates.Select(candidate => candidate.TypeName).ToArray();

    public static IReadOnlyList<string> GetEligibleTypes()
    {
        if (Game1.player is null)
            return Array.Empty<string>();

        HashSet<string> eligibleClassNames =
            MonsterLocationPools.GetEligibleClassNames(MonsterProgression.Read(Game1.player));

        return Candidates
            .Where(candidate => eligibleClassNames.Contains(candidate.TypeName))
            .Select(candidate => candidate.TypeName)
            .ToArray();
    }

    public static IReadOnlyList<string> GetUnresolvedEligibleClassNames()
    {
        if (Game1.player is null)
            return Array.Empty<string>();

        HashSet<string> eligibleClassNames =
            MonsterLocationPools.GetEligibleClassNames(MonsterProgression.Read(Game1.player));
        HashSet<string> discoveredClassNames =
            Candidates.Select(candidate => candidate.TypeName).ToHashSet(StringComparer.Ordinal);

        return eligibleClassNames
            .Where(typeName => !discoveredClassNames.Contains(typeName))
            .OrderBy(typeName => typeName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetDisplayName(string typeName)
    {
        return Regex.Replace(typeName, "([a-z0-9])([A-Z])", "$1 $2");
    }

    public static string GetRandomType(string difficulty = "normal")
    {
        if (Game1.player is null)
            throw new InvalidOperationException("A save must be loaded before selecting a progression-eligible monster.");

        HashSet<string> eligibleClassNames =
            MonsterLocationPools.GetEligibleClassNames(MonsterProgression.Read(Game1.player));
        Candidate[] eligibleCandidates = Candidates
            .Where(candidate => eligibleClassNames.Contains(candidate.TypeName))
            .ToArray();

        if (eligibleCandidates.Length == 0)
            throw new InvalidOperationException("No progression-eligible vanilla monster classes were discovered.");

        // Easy favours weaker monsters; hard makes tougher monsters more likely.
        double exponent = difficulty.Trim().ToLowerInvariant() switch
        {
            "easy" => 1.8d,
            "hard" => 0.9d,
            _ => 1.35d
        };

        double totalWeight = eligibleCandidates.Sum(candidate => GetWeight(candidate.ExpectedHealth, exponent));
        double roll = Random.Shared.NextDouble() * totalWeight;

        foreach (Candidate candidate in eligibleCandidates)
        {
            roll -= GetWeight(candidate.ExpectedHealth, exponent);
            if (roll <= 0)
                return candidate.TypeName;
        }

        return eligibleCandidates[^1].TypeName;
    }

    public static bool TryGetCanonicalType(string? value, out string monsterType)
    {
        string requested = Normalize(value);
        HashSet<string> eligibleClassNames = Game1.player is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : MonsterLocationPools.GetEligibleClassNames(MonsterProgression.Read(Game1.player));

        Candidate? match = Candidates.FirstOrDefault(candidate =>
            eligibleClassNames.Contains(candidate.TypeName) &&
            string.Equals(Normalize(candidate.TypeName), requested, StringComparison.OrdinalIgnoreCase));

        monsterType = match?.TypeName ?? "";
        return match is not null;
    }

    private static Candidate[] DiscoverVanillaMonsters()
    {
        // Match the game's standard "monster Type x y" constructor contract.
        // Modded assemblies are never included.
        return typeof(Monster).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                !type.ContainsGenericParameters &&
                type.Assembly == typeof(Monster).Assembly &&
                type.Namespace == typeof(Monster).Namespace &&
                typeof(Monster).IsAssignableFrom(type) &&
                type.GetConstructor(new[] { typeof(Vector2) }) is not null)
            .Where(type => type.Name is not "Duggy" and not "LavaLurk" and not "Mummy")
            .Select(type => new Candidate(
                type.Name,
                ExpectedHealthByType.TryGetValue(type.Name, out double health) ? health : 100d))
            .OrderBy(candidate => candidate.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Normalize(string? value)
    {
        return new string((value ?? "").Where(char.IsLetterOrDigit).ToArray());
    }

    private static double GetWeight(double health, double exponent)
    {
        return 1d / Math.Pow(Math.Max(1d, health), exponent);
    }
}
