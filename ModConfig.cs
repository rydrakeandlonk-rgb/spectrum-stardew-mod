using System.Text.RegularExpressions;
using StardewModdingAPI;

namespace SpectrumStardewMod;

/// <summary>Local configuration for the host's Spectrum Game Mods connection.</summary>
internal sealed class ModConfig
{
    public const int CurrentConfigVersion = 2;
    private const string DefaultGatewayUrl = "https://spectrum-game-mods.rydrakeandlonk.workers.dev";

    public int ConfigVersion { get; set; }
    public string GatewayUrl { get; set; } = DefaultGatewayUrl;
    public string BroadcasterUserId { get; set; } = "";
    public string BroadcasterLogin { get; set; } = "";
    public string HostToken { get; set; } = "";
    public string ChallengeName { get; set; } = "Spectrum Challenge";
    public int ChallengeDeathCount { get; set; }

    /// <summary>Upgrade older configs and discard malformed or incomplete credentials safely.</summary>
    public bool Normalize(IMonitor monitor)
    {
        bool changed = false;

        if (this.ConfigVersion < CurrentConfigVersion)
        {
            this.ConfigVersion = CurrentConfigVersion;
            changed = true;
            monitor.Log($"Migrated the Spectrum mod configuration to version {CurrentConfigVersion}. Existing valid connection details were preserved.", LogLevel.Info);
        }
        else if (this.ConfigVersion > CurrentConfigVersion)
        {
            monitor.Log($"The local Spectrum config was written by a newer version ({this.ConfigVersion}). Known fields will be normalised for compatibility.", LogLevel.Warn);
            this.ConfigVersion = CurrentConfigVersion;
            changed = true;
        }

        string gateway = (this.GatewayUrl ?? "").Trim();
        bool validGateway = Uri.TryCreate(gateway, UriKind.Absolute, out Uri? gatewayUri) &&
            gatewayUri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(gatewayUri.UserInfo) &&
            string.IsNullOrEmpty(gatewayUri.Query) &&
            string.IsNullOrEmpty(gatewayUri.Fragment);

        if (!validGateway)
        {
            this.GatewayUrl = DefaultGatewayUrl;
            changed = true;
            monitor.Log("GatewayUrl was invalid. Restored the official Spectrum Game Mods HTTPS endpoint.", LogLevel.Warn);
        }
        else
        {
            string normalisedGateway = gatewayUri!.GetLeftPart(UriPartial.Path).TrimEnd('/');
            if (this.GatewayUrl != normalisedGateway)
            {
                this.GatewayUrl = normalisedGateway;
                changed = true;
            }
        }

        string broadcasterLogin = Regex.Replace((this.BroadcasterLogin ?? "").Trim(), @"[^a-zA-Z0-9_]", "");
        if (broadcasterLogin.Length > 25) broadcasterLogin = broadcasterLogin[..25];
        if (this.BroadcasterLogin != broadcasterLogin)
        {
            this.BroadcasterLogin = broadcasterLogin;
            changed = true;
        }

        string broadcasterId = (this.BroadcasterUserId ?? "").Trim();
        string hostToken = (this.HostToken ?? "").Trim();
        bool hasAnyCredential = broadcasterId.Length > 0 || hostToken.Length > 0;
        bool validCredentials = Regex.IsMatch(broadcasterId, @"^\d{1,30}$") &&
            Regex.IsMatch(hostToken, @"^[a-fA-F0-9]{64}$");

        if (hasAnyCredential && !validCredentials)
        {
            this.BroadcasterUserId = "";
            this.HostToken = "";
            changed = true;
            monitor.Log("The saved Spectrum connection details were incomplete or invalid. Cleared them safely; connect Twitch again from the in-game menu.", LogLevel.Warn);
        }
        else
        {
            if (this.BroadcasterUserId != broadcasterId)
            {
                this.BroadcasterUserId = broadcasterId;
                changed = true;
            }
            if (this.HostToken != hostToken)
            {
                this.HostToken = hostToken;
                changed = true;
            }
        }

        string challengeName = Regex.Replace((this.ChallengeName ?? "").Trim(), @"[\r\n\t]+", " ");
        if (challengeName.Length == 0)
            challengeName = "Spectrum Challenge";
        if (challengeName.Length > 80)
            challengeName = challengeName[..80];
        if (this.ChallengeName != challengeName)
        {
            this.ChallengeName = challengeName;
            changed = true;
        }

        int deathCount = Math.Clamp(this.ChallengeDeathCount, 0, 1000000);
        if (this.ChallengeDeathCount != deathCount)
        {
            this.ChallengeDeathCount = deathCount;
            changed = true;
        }

        return changed;
    }
}
