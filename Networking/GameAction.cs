namespace SpectrumStardewMod.Networking;

/// <summary>A validated one-shot action sent by Control Center to the game host.</summary>
internal sealed record GameAction(string ActionId, string Game, string Name, string Difficulty = "normal", string Outcome = "", int MonsterCount = 10);
