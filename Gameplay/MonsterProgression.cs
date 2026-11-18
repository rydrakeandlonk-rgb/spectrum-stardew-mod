using StardewValley;

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
/// A lightweight snapshot of the active save's permanent progression.
/// This is read only when a spawn is selected, never polled in the update loop.
/// </summary>
internal sealed record MonsterProgressionSnapshot(
    bool DangerousMinesUnlocked,
    bool QuarryMineUnlocked,
    bool SkullCavernUnlocked,
    bool DangerousSkullCavernUnlocked,
    bool VolcanoDungeonUnlocked)
{
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
            DangerousMinesUnlocked: completedOrders.Contains("QiChallenge9"),
            QuarryMineUnlocked: farmer.mailReceived.Contains("gotGoldenScythe"),
            SkullCavernUnlocked: farmer.hasSkullKey,
            DangerousSkullCavernUnlocked: completedOrders.Contains("QiChallenge10"),
            VolcanoDungeonUnlocked: farmer.hasOrWillReceiveMail("willyBoatFixed")
        );
    }
}
