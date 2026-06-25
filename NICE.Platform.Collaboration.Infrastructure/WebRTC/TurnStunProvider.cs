namespace NICE.Platform.Collaboration.Infrastructure.WebRTC;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Responses;
using Microsoft.Extensions.Configuration;
public class TurnStunProvider(IConfiguration config) : IIceServerProvider
{
    public Task<IceServerConfigResponse> GetConfigAsync(CancellationToken ct = default)
    {
        // Config lives under IceServers:TurnServer (matches appsettings Section 7 and
        // GoogleStunProvider's IceServers:GoogleStun). A common production setup also
        // includes a STUN url alongside the TURN urls so srflx candidates still work.
        var response = new IceServerConfigResponse
        {
            Urls       = config.GetSection("IceServers:TurnServer:Urls").Get<List<string>>() ?? [],
            Username   = config["IceServers:TurnServer:Username"],
            Credential = config["IceServers:TurnServer:Credential"],
        };
        return Task.FromResult(response);
    }
}
