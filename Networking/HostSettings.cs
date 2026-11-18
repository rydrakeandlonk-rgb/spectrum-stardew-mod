namespace SpectrumStardewMod.Networking;

/// <summary>The streamer-controlled settings synchronised with the Spectrum host session.</summary>
internal sealed record HostSettings(
    bool SpawnEnabled = true,
    string Difficulty = "normal",
    bool ChatResponses = true,
    bool DebugMessages = false
);
