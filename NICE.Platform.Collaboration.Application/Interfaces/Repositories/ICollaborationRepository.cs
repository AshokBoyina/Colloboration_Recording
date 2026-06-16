namespace NICE.Platform.Collaboration.Application.Interfaces.Repositories;

using NICE.Platform.Collaboration.Core.Entities;

public interface ICollaborationRepository
{
    Task<Collaboration?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Collaboration?> GetByIdWithParticipantsAsync(Guid id, CancellationToken ct);
    Task AddAsync(Collaboration collab, CancellationToken ct);
    Task UpdateAsync(Collaboration collab, CancellationToken ct);
    Task<IEnumerable<Collaboration>> GetActiveByApplicationAsync(Guid applicationId, CancellationToken ct);
    Task<IEnumerable<Collaboration>> GetActiveByAgentAsync(Guid agentUserId, CancellationToken ct);
}
