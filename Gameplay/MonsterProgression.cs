using StardewValley;
using StardewValley.Locations;
using System.Collections;
using System.Reflection;

namespace SpectrumStardewMod.Gameplay;

internal enum MonsterPool
{
    Mines,
    DangerousMines,
    QuarryMine,
    SkullCavern,
    DangerousSkullCavern,
    BugLair,
    Farm,
    VolcanoDungeon
}

/// <summary>
/// A lightweight snapshot of the active host player's permanent progression.
/// This is read only when a monster is selected, never polled in the update loop.
/// The optional developer test snapshot is memory-only and never writes save data.
/// </summary>
internal sealed record MonsterProgressionSnapshot(
    bool MinesCompleted,
    bool SkullCavern100Reached,
    int CombatLevel,
    string CurrentAreaName,
    int CurrentAreaDifficulty,
    bool DangerousMinesUnlocked,
    bool QuarryMineUnlocked,
    bool SkullCavernUnlocked,
    bool BugLairUnlocked,
    bool DangerousSkullCavernUnlocked,
    bool VolcanoDungeonUnlocked)
{
    private static MonsterProgressionSnapshot? testOverride;

    public static bool IsTestOverrideActive => testOverride is not null;

    public int ProgressionStage => this.SkullCavern100Reached ? 2 : this.MinesCompleted ? 1 : 0;

    public string ProgressionStageName => this.ProgressionStage switch
    {
        0 => "Early game (Mines 120 not reached)",
        1 => "Mines complete (Skull Cavern 100 not reached)",
        _ => "Advanced (Skull Cavern 100 reached)"
    };

    public bool IsUnlocked(MonsterPool pool)
    {
        return pool switch
        {
            MonsterPool.Mines => true,
            MonsterPool.DangerousMines => this.DangerousMinesUnlocked,
            MonsterPool.QuarryMine => this.QuarryMineUnlocked,
            MonsterPool.SkullCavern => this.SkullCavernUnlocked,
            MonsterPool.DangerousSkullCavern => this.DangerousSkullCavernUnlocked,
            MonsterPool.BugLair => this.BugLairUnlocked,
            MonsterPool.Farm => true,
            MonsterPool.VolcanoDungeon => this.VolcanoDungeonUnlocked,
            _ => false
        };
    }

    public static MonsterProgressionSnapshot Read(Farmer farmer) =>
        testOverride ?? ReadActual(farmer);

    /// <summary>
    /// Applies a temporary, in-memory progression profile for testing.
    /// It never changes Farmer fields, mail, quests, special orders, or save data.
    /// </summary>
    public static bool TrySetTestPreset(string preset, Farmer farmer, out string error)
    {
        MonsterProgressionSnapshot actual = ReadActual(farmer);
        MonsterProgressionSnapshot early = actual with
        {
            MinesCompleted = false,
            SkullCavern100Reached = false,
            DangerousMinesUnlocked = false,
            QuarryMineUnlocked = false,
            SkullCavernUnlocked = false,
            BugLairUnlocked = false,
            DangerousSkullCavernUnlocked = false,
            VolcanoDungeonUnlocked = false
        };

        MonsterProgressionSnapshot? nextOverride = preset.Trim().ToLowerInvariant() switch
        {
            "early" => early,
            "mines" => early with { MinesCompleted = true },
            "skull100" => early with
            {
                MinesCompleted = true,
                SkullCavern100Reached = true,
                SkullCavernUnlocked = true
            },
            "danger-mines" => early with { DangerousMinesUnlocked = true },
            "quarry" => early with { QuarryMineUnlocked = true },
            "skull-cavern" => early with { SkullCavernUnlocked = true },
            "danger-skull" => early with { DangerousSkullCavernUnlocked = true },
            "bug-lair" => early with { BugLairUnlocked = true },
            "volcano" => early with { VolcanoDungeonUnlocked = true },
            "all" => actual with
            {
                MinesCompleted = true,
                SkullCavern100Reached = true,
                DangerousMinesUnlocked = true,
                QuarryMineUnlocked = true,
                SkullCavernUnlocked = true,
                BugLairUnlocked = true,
                DangerousSkullCavernUnlocked = true,
                VolcanoDungeonUnlocked = true
            },
            _ => null
        };

        if (nextOverride is null)
        {
            error = "Unknown preset. Use early, mines, skull100, danger-mines, quarry, skull-cavern, danger-skull, bug-lair, volcano, or all.";
            return false;
        }

        testOverride = nextOverride;

        error = "";
        return true;
    }

    public static void ClearTestOverride() => testOverride = null;

    private static MonsterProgressionSnapshot ReadActual(Farmer farmer)
    {
        // Qi's challenges are repeatable Special Orders. Their permanent
        // completion history lives on the shared FarmerTeam, not questsCompleted.
        var completedOrders = farmer.team.completedSpecialOrders;
        string currentAreaName = "Other location";
        int currentAreaDifficulty = 0;

        if (Game1.currentLocation is MineShaft shaft)
        {
            if (shaft.mineLevel == 77377)
            {
                currentAreaName = "Quarry Mine";
            }
            else if (shaft.mineLevel >= 121)
            {
                currentAreaName = "Skull Cavern";
                currentAreaDifficulty = Math.Clamp((int)Game1.netWorldState.Value.SkullCavesDifficulty, 0, 2);
            }
            else if (shaft.mineLevel is >= 1 and <= 120)
            {
                currentAreaName = "Mines";
                currentAreaDifficulty = Math.Clamp((int)Game1.netWorldState.Value.MinesDifficulty, 0, 2);
            }
        }

        return new MonsterProgressionSnapshot(
            MinesCompleted: farmer.deepestMineLevel >= 120,
            // Quest 30 is the completed Cryptic Note (Secret Note #10) story quest.
            // Quest history was added in Stardew 1.6.16, so read it reflectively to
            // keep the source compatible with the pinned 1.6.15 build references.
            // Older supported game builds don't expose questsCompleted. Their
            // permanent qiCave mail flag is set when the floor-100 reward milk
            // has been consumed, so use it as a version-compatible fallback.
            SkullCavern100Reached: HasCompletedQuest(farmer, 30) || farmer.mailReceived.Contains("qiCave"),
            CombatLevel: Math.Clamp(farmer.CombatLevel, 0, 10),
            CurrentAreaName: currentAreaName,
            CurrentAreaDifficulty: currentAreaDifficulty,
            DangerousMinesUnlocked: completedOrders.Contains("QiChallenge9"),
            // Quarry Mine access comes from repairing the Quarry bridge, not
            // collecting the Golden Scythe at the end of the mine.
            QuarryMineUnlocked: farmer.hasOrWillReceiveMail("ccCraftsRoom") ||
                farmer.hasOrWillReceiveMail("jojaCraftsRoom"),
            SkullCavernUnlocked: farmer.hasSkullKey,
            BugLairUnlocked: farmer.mailReceived.Contains("krobusUnseal"),
            DangerousSkullCavernUnlocked: completedOrders.Contains("QiChallenge10"),
            VolcanoDungeonUnlocked: farmer.hasOrWillReceiveMail("willyBoatFixed")
        );
    }

    private static bool HasCompletedQuest(Farmer farmer, int questId)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object? completed = typeof(Farmer).GetProperty("questsCompleted", Flags)?.GetValue(farmer)
            ?? typeof(Farmer).GetField("questsCompleted", Flags)?.GetValue(farmer);

        if (completed is IEnumerable<int> ids)
            return ids.Contains(questId);

        if (completed is IEnumerable values)
        {
            foreach (object? value in values)
            {
                if (value is int id && id == questId)
                    return true;
                if (int.TryParse(value?.ToString(), out id) && id == questId)
                    return true;
            }
        }

        return false;
    }
}
