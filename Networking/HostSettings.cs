namespace SpectrumStardewMod.Networking;

/// <summary>The streamer settings synchronised with the Spectrum host session.</summary>
internal sealed record HostSettings(
    bool SpawnEnabled = true,
    string Difficulty = "normal"
);
