namespace NICE.Platform.Collaboration.Application.Interfaces.Repositories;

using NICE.Platform.Collaboration.Core.Entities;

public interface IUserRepository
{
    Task<CollaborationUser?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<CollaborationUser?> GetByExternalIdAsync(string externalId, CancellationToken ct);
    Task AddAsync(CollaborationUser user, CancellationToken ct);
    Task UpdateAsync(CollaborationUser user, CancellationToken ct);
    Task<IEnumerable<CollaborationUser>> GetActiveByApplicationAsync(Guid applicationId, CancellationToken ct);
}
