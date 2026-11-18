namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// The allowlisted vanilla monster types accepted by Stardew Valley's built-in
/// debug monster command, with approximate health values used for weighting.
/// </summary>
internal static class MonsterCatalog
{
    private sealed record Candidate(string TypeName, double ExpectedHealth);

    // These are balancing estimates, not guaranteed in-game health values.
    private static readonly Candidate[] Candidates =
    {
        new("Bat", 15),
        new("DinoMonster", 500),
        new("DustSpirit", 40),
        new("Fly", 30),
        new("Ghost", 50),
        new("GreenSlime", 24),
        new("Grub", 30),
        new("LavaCrab", 120),
        new("Mummy", 140),
        new("RockCrab", 60),
        new("RockGolem", 80),
        new("Serpent", 150),
        new("ShadowBrute", 160),
        new("ShadowShaman", 140),
        new("Skeleton", 70),
        new("SquidKid", 120)
    };

    public static IReadOnlyList<string> AllTypes => Candidates.Select(candidate => candidate.TypeName).ToArray();

    public static string GetDisplayName(string typeName)
    {
        return typeName switch
        {
            "DinoMonster" => "Dino Monster",
            "DustSpirit" => "Dust Spirit",
            "GreenSlime" => "Green Slime",
            "LavaCrab" => "Lava Crab",
            "RockCrab" => "Rock Crab",
            "RockGolem" => "Rock Golem",
            "ShadowBrute" => "Shadow Brute",
            "ShadowShaman" => "Shadow Shaman",
            "SquidKid" => "Squid Kid",
            _ => typeName
        };
    }

    public static string GetRandomType(string difficulty = "normal")
    {
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
        string requested = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray());
        Candidate? match = Candidates.FirstOrDefault(candidate =>
            string.Equals(
                new string(candidate.TypeName.Where(char.IsLetterOrDigit).ToArray()),
                requested,
                StringComparison.OrdinalIgnoreCase
            ));
        monsterType = match?.TypeName ?? "";
        return match is not null;
    }

    private static double GetWeight(double health, double exponent)
    {
        return 1d / Math.Pow(Math.Max(1d, health), exponent);
    }
}
