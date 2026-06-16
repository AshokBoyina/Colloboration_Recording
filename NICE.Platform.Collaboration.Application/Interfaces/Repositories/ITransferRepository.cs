namespace NICE.Platform.Collaboration.Application.Interfaces.Repositories;

using NICE.Platform.Collaboration.Core.Entities;

public interface ITransferRepository
{
    Task AddAsync(CollaborationTransferRequest transfer, CancellationToken ct);
    Task UpdateAsync(CollaborationTransferRequest transfer, CancellationToken ct);
    Task<CollaborationTransferRequest?> GetPendingAsync(Guid collaborationId, CancellationToken ct);
    Task<IEnumerable<CollaborationTransferRequest>> GetByCollaborationAsync(Guid collaborationId, CancellationToken ct);
}
