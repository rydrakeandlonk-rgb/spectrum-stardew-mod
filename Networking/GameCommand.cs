namespace SpectrumStardewMod.Networking;

/// <summary>A validated, typed command received from Spectrum Game Mods.</summary>
internal sealed record GameCommand(string CommandId, string Game, string Name);
