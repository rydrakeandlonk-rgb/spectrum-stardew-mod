namespace SpectrumStardewMod.Networking;

/// <summary>A validated, typed command received from Spectrum Game Mods.</summary>
internal sealed record GameCommand(
    string CommandId,
    string Game,
    string Name,
    string TwitchUserName,
    string RewardTitle,
    string RedeemedAt,
    string BehaviorKey,
    string UserInput,
    string MonsterType,
    string Difficulty,
    int Count,
    int MinCount,
    int MaxCount
);
