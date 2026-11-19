using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SpectrumStardewMod.Networking;

internal sealed record PairingStart(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval
);

internal sealed record PairingResult(string BroadcasterUserId, string BroadcasterLogin, string HostToken);

/// <summary>Performs the short-lived device-code flow used to pair a Spectrum host.</summary>
internal sealed class StardewPairingClient : IDisposable
{
    private readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<PairingStart> StartAsync(string gatewayUrl, CancellationToken cancellationToken)
    {
        using var response = await this.http.PostAsync(
            gatewayUrl.TrimEnd('/') + "/connect/stardew/pairing/start",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            cancellationToken
        );
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ReadError(json, "Could not start Stardew pairing."));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        return new PairingStart(
            ReadString(root, "device_code"),
            ReadString(root, "user_code"),
            ReadString(root, "verification_uri"),
            ReadInt(root, "expires_in", 600),
            ReadInt(root, "interval", 5)
        );
    }

    public async Task<PairingResult> WaitForApprovalAsync(
        string gatewayUrl,
        PairingStart pairing,
        CancellationToken cancellationToken
    )
    {
        DateTime expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(30, pairing.ExpiresIn));
        int intervalSeconds = Math.Clamp(pairing.Interval, 3, 15);
        while (DateTime.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
            using var response = await this.http.PostAsync(
                gatewayUrl.TrimEnd('/') + "/connect/stardew/pairing/poll",
                new StringContent(JsonSerializer.Serialize(new
                {
                    user_code = pairing.UserCode,
                    device_code = pairing.DeviceCode
                }), Encoding.UTF8, "application/json"),
                cancellationToken
            );
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(ReadError(json, "Could not check Stardew pairing status."));

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string status = ReadString(root, "status");
            if (status == "authorized")
            {
                string broadcasterId = ReadString(root, "broadcaster_user_id");
                string broadcasterLogin = ReadString(root, "broadcaster_login");
                string hostToken = ReadString(root, "host_token");
                if (string.IsNullOrWhiteSpace(broadcasterId) || string.IsNullOrWhiteSpace(hostToken))
                    throw new InvalidOperationException("The pairing response did not include valid host credentials.");
                return new PairingResult(broadcasterId, broadcasterLogin, hostToken);
            }
            if (status == "expired" || status == "consumed")
                throw new InvalidOperationException("The pairing code has expired. Start a new connection request.");
        }

        throw new TimeoutException("The pairing request expired before it was approved.");
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
            value.TryGetInt32(out int result)
                ? result
                : fallback;
    }

    private static string ReadError(string json, string fallback)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ReadString(document.RootElement, "error") is { Length: > 0 } error
                ? error
                : fallback;
        }
        catch { return fallback; }
    }

    public void Dispose() => this.http.Dispose();
}
