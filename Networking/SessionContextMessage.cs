namespace SpectrumStardewMod.Networking;

public sealed class SessionContextRequest
{
}

public sealed class SessionContextMessage
{
    public string HostSessionId { get; set; } = "";
    public string HostBroadcasterUserId { get; set; } = "";
}
