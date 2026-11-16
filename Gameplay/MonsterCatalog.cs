using System.Reflection;
using Microsoft.Xna.Framework;
using StardewValley.Monsters;

namespace SpectrumStardewMod.Gameplay;

/// <summary>
/// Builds a safe allowlisted pool of vanilla monsters and chooses from it with
/// inverse-health weighting. A monster with more base health receives a lower
/// chance of selection. Types that cannot be constructed on the installed game
/// version are skipped rather than breaking the redemption.
/// </summary>
internal static class MonsterCatalog
{
    private sealed record Candidate(string TypeName, double ExpectedHealth);

    private static readonly Candidate[] Candidates =
    {
        new("GreenSlime", 24),
        new("Bat", 15),
        new("FrostBat", 30),
        new("Fly", 30),
        new("Grub", 30),
        new("DustSpirit", 50),
        new("RockGolem", 80),
        new("ShadowBrute", 160),
        new("ShadowShaman", 140),
        new("Serpent", 150),
        new("Mummy", 140),
        new("LavaCrab", 120),
        new("PepperRex", 500)
    };

    public static bool TryCreateRandom(Vector2 pixelPosition, out Monster? monster)
    {
        var available = new List<(Monster Monster, double Weight)>();
        var assembly = typeof(GreenSlime).Assembly;

        foreach (Candidate candidate in Candidates)
        {
            try
            {
                Type? type = assembly.GetType("StardewValley.Monsters." + candidate.TypeName);
                if (type is null || !typeof(Monster).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                Monster? instance = CreateMonster(type, pixelPosition);
                if (instance is null)
                    continue;

                double health = ReadHealth(instance) ?? candidate.ExpectedHealth;
                // Inverse power weighting makes high-HP monsters substantially rarer
                // while retaining a non-zero chance for every available candidate.
                double weight = 1d / Math.Pow(Math.Max(1d, health), 1.35d);
                available.Add((instance, weight));
            }
            catch
            {
                // A game update can change a monster constructor. Skip that type.
            }
        }

        if (available.Count == 0)
        {
            monster = null;
            return false;
        }

        double totalWeight = available.Sum(item => item.Weight);
        double roll = Random.Shared.NextDouble() * totalWeight;

        foreach ((Monster candidate, double weight) in available)
        {
            roll -= weight;
            if (roll <= 0)
            {
                monster = candidate;
                return true;
            }
        }

        monster = available[^1].Monster;
        return true;
    }

    private static Monster? CreateMonster(Type type, Vector2 pixelPosition)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(Vector2))
                continue;

            var arguments = new object?[parameters.Length];
            arguments[0] = pixelPosition;

            bool supported = true;
            for (int i = 1; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                if (parameter.HasDefaultValue)
                    arguments[i] = parameter.DefaultValue;
                else if (parameter.ParameterType.IsValueType)
                    arguments[i] = Activator.CreateInstance(parameter.ParameterType);
                else if (!parameter.ParameterType.IsByRef)
                    arguments[i] = null;
                else
                    supported = false;
            }

            if (!supported)
                continue;

            try
            {
                if (constructor.Invoke(arguments) is Monster created)
                    return created;
            }
            catch
            {
                // Try another constructor for this allowlisted monster.
            }
        }

        return null;
    }

    private static double? ReadHealth(Monster monster)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
        object? value = monster.GetType().GetProperty("Health", Flags)?.GetValue(monster)
            ?? monster.GetType().GetField("Health", Flags)?.GetValue(monster);

        return value is IConvertible convertible
            ? Math.Max(1d, convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture))
            : null;
    }
}
