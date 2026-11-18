using StardewValley;
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
/// </summary>
internal sealed record MonsterProgressionSnapshot(
    bool MinesCompleted,
    bool SkullCavern100Reached,
    int CombatLevel,
    bool DangerousMinesUnlocked,
    bool QuarryMineUnlocked,
    bool SkullCavernUnlocked,
    bool DangerousSkullCavernUnlocked,
    bool VolcanoDungeonUnlocked)
{
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
            MonsterPool.BugLair => true,
            MonsterPool.Farm => true,
            MonsterPool.VolcanoDungeon => this.VolcanoDungeonUnlocked,
            _ => false
        };
    }

    public static MonsterProgressionSnapshot Read(Farmer farmer)
    {
        // Qi's challenges are repeatable Special Orders. Their permanent
        // completion history lives on the shared FarmerTeam, not questsCompleted.
        var completedOrders = farmer.team.completedSpecialOrders;

        return new MonsterProgressionSnapshot(
            MinesCompleted: farmer.deepestMineLevel >= 120,
            // Quest 30 is the completed Cryptic Note (Secret Note #10) story quest.
            // Quest history was added in Stardew 1.6.16, so read it reflectively to
            // keep the source compatible with the pinned 1.6.15 build references.
            SkullCavern100Reached: HasCompletedQuest(farmer, 30),
            CombatLevel: Math.Clamp(farmer.CombatLevel, 0, 10),
            DangerousMinesUnlocked: completedOrders.Contains("QiChallenge9"),
            QuarryMineUnlocked: farmer.mailReceived.Contains("gotGoldenScythe"),
            SkullCavernUnlocked: farmer.hasSkullKey,
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
