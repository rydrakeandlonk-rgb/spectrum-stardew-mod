namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// The allowlisted vanilla monster types accepted by Stardew Valley's built-in
/// debug monster command, with approximate health values used for weighting.
/// </summary>
internal static class MonsterCatalog
{
    private sealed record Candidate(string TypeName, double ExpectedHealth);

    // These are balancing estimates, not guaranteed in-game health values.
    // Higher-health monsters receive a lower selection weight.
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

    public static string GetRandomType()
    {
        double totalWeight = Candidates.Sum(candidate => GetWeight(candidate.ExpectedHealth));
        double roll = Random.Shared.NextDouble() * totalWeight;

        foreach (Candidate candidate in Candidates)
        {
            roll -= GetWeight(candidate.ExpectedHealth);
            if (roll <= 0)
                return candidate.TypeName;
        }

        return Candidates[^1].TypeName;
    }

    private static double GetWeight(double health)
    {
        return 1d / Math.Pow(Math.Max(1d, health), 1.35d);
    }
}
