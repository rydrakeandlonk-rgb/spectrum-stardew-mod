namespace SpectrumStardewMod.Gameplay;

internal sealed record PoolDiagnostic(
    string Pool,
    bool ExpectedEnabled,
    bool EffectiveEnabled,
    string Override,
    string Reason,
    int DefinitionCount
);
 
internal sealed record PoolSpawnDecision(
    string Monster,
    string Pool,
    string Outcome,
    string Reason,
    string RecordedAt
);
