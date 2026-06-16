namespace NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Responses;
public interface IIceServerProvider
{
    Task<IceServerConfigResponse> GetConfigAsync(CancellationToken ct = default);
}
