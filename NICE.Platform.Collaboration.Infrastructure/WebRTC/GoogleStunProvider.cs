namespace NICE.Platform.Collaboration.Infrastructure.WebRTC;

using Microsoft.Extensions.Configuration;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Responses;

/// <summary>
/// Returns Google's free public STUN servers for WebRTC ICE negotiation.
/// No account, no credentials, no cost — sufficient for dev, test, and most
/// production topologies that are not behind strict symmetric NAT.
///
/// URLs are read from <c>IceServers:GoogleStun</c> in appsettings so they can
/// be overridden in config without a code change.  Sensible defaults are
/// baked in so the service works even if the config section is absent.
///
/// Flip <c>FeatureFlags:UseCustomTurn = true</c> to switch to
/// <see cref="TurnStunProvider"/> (dedicated TURN relay server) when needed.
/// </summary>
public class GoogleStunProvider(IConfiguration config) : IIceServerProvider
{
    private static readonly List<string> DefaultUrls =
    [
        "stun:stun.l.google.com:19302",
        "stun:stun1.l.google.com:19302",
    ];

    public Task<IceServerConfigResponse> GetConfigAsync(CancellationToken ct = default)
    {
        var configuredUrls = config
            .GetSection("IceServers:GoogleStun")
            .Get<List<string>>();

        var response = new IceServerConfigResponse
        {
            Urls       = configuredUrls is { Count: > 0 } ? configuredUrls : DefaultUrls,
            Username   = null,   // STUN does not require credentials
            Credential = null,
        };

        return Task.FromResult(response);
    }
}
