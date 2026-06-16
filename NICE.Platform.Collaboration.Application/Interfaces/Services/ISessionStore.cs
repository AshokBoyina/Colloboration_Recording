namespace NICE.Platform.Collaboration.Application.Interfaces.Services;
public interface ISessionStore
{
    Task SetAsync(string key, string value, TimeSpan? expiry, CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
}
