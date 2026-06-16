namespace NICE.Platform.Collaboration.Application.Interfaces.Repositories;

using NICE.Platform.Collaboration.Core.Entities;

public interface IApplicationRepository
{
    Task<CollaborationApplication?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CollaborationApplication?> GetByApiKeyHashAsync(string hash, CancellationToken ct);
    Task<CollaborationApplication?> GetByNameAsync(string name, CancellationToken ct);
    Task AddAsync(CollaborationApplication app, CancellationToken ct);
    Task UpdateAsync(CollaborationApplication app, CancellationToken ct);
}
