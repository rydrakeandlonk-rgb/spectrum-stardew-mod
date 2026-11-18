using StardewModdingAPI;

namespace SpectrumStardewMod;

/// <summary>Local configuration for the host's Spectrum Game Mods connection.</summary>
internal sealed class ModConfig
{
    public string GatewayUrl { get; set; } = "https://spectrum-game-mods.rydrakeandlonk.workers.dev";
    public string BroadcasterUserId { get; set; } = "";
    public string HostToken { get; set; } = "";
}
