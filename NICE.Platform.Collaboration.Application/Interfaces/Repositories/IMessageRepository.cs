namespace NICE.Platform.Collaboration.Application.Interfaces.Repositories;

using NICE.Platform.Collaboration.Core.Entities;

public interface IMessageRepository
{
    Task AddAsync(CollaborationMessage message, CancellationToken ct);
    Task<IEnumerable<CollaborationMessage>> GetByCollaborationAsync(Guid collaborationId, CancellationToken ct);
    Task<CollaborationMessage?> GetByIdAsync(Guid id, CancellationToken ct);
    Task UpdateAsync(CollaborationMessage message, CancellationToken ct);
}
