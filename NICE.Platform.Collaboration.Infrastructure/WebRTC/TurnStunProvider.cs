namespace NICE.Platform.Collaboration.Infrastructure.WebRTC;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Responses;
using Microsoft.Extensions.Configuration;
public class TurnStunProvider(IConfiguration config) : IIceServerProvider
{
    public Task<IceServerConfigResponse> GetConfigAsync(CancellationToken ct = default)
    {
        var response = new IceServerConfigResponse
        {
            Urls       = config.GetSection("TurnServer:Urls").Get<List<string>>() ?? [],
            Username   = config["TurnServer:Username"],
            Credential = config["TurnServer:Credential"],
        };
        return Task.FromResult(response);
    }
}
