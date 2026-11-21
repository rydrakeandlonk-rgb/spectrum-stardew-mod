namespace SpectrumStardewMod.Networking;

internal sealed record PoolOverrideUpdate(
    string SessionId,
    int Revision,
    IReadOnlyDictionary<string, string> Overrides
);
