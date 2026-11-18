using Microsoft.Xna.Framework;
using StardewValley.Monsters;
using System.Text.RegularExpressions;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Discovers every vanilla monster type which the game's built-in
/// "monster Type x y" debug command can construct using its standard
/// Vector2-only constructor. Modded monster assemblies are never included.
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
            ["LavaCrab"] = 120,
            ["Mummy"] = 140,
            ["RockCrab"] = 60,
            ["RockGolem"] = 80,
            ["Serpent"] = 150,
            ["ShadowBrute"] = 160,
            ["ShadowShaman"] = 140,
            ["Skeleton"] = 70,
            ["SquidKid"] = 120
        };

    private static readonly Candidate[] Candidates = DiscoverVanillaMonsters();

    public static IReadOnlyList<string> AllTypes =>
        Candidates.Select(candidate => candidate.TypeName).ToArray();

    public static string GetDisplayName(string typeName)
    {
        return Regex.Replace(typeName, "([a-z0-9])([A-Z])", "$1 $2");
    }

    public static string GetRandomType(string difficulty = "normal")
    {
        if (Candidates.Length == 0)
            throw new InvalidOperationException("No spawnable vanilla monster types were discovered.");

        // Easy favours weaker monsters; hard makes tougher monsters more likely.
        double exponent = difficulty.Trim().ToLowerInvariant() switch
        {
            "easy" => 1.8d,
            "hard" => 0.9d,
            _ => 1.35d
        };

        double totalWeight = Candidates.Sum(candidate => GetWeight(candidate.ExpectedHealth, exponent));
        double roll = Random.Shared.NextDouble() * totalWeight;

        foreach (Candidate candidate in Candidates)
        {
            roll -= GetWeight(candidate.ExpectedHealth, exponent);
            if (roll <= 0)
                return candidate.TypeName;
        }

        return Candidates[^1].TypeName;
    }

    public static bool TryGetCanonicalType(string? value, out string monsterType)
    {
        string requested = Normalize(value);
        Candidate? match = Candidates.FirstOrDefault(candidate =>
            string.Equals(Normalize(candidate.TypeName), requested, StringComparison.OrdinalIgnoreCase));

        monsterType = match?.TypeName ?? "";
        return match is not null;
    }

    private static Candidate[] DiscoverVanillaMonsters()
    {
        // Stardew's own debug handler resolves "StardewValley.Monsters.<type>"
        // and calls Activator.CreateInstance(type, position). Match that exact
        // constructor contract, and restrict discovery to the vanilla game DLL.
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
