namespace NICE.Platform.Collaboration.Application.Interfaces.Repositories;

using NICE.Platform.Collaboration.Core.Entities;

public interface IRecordingRepository
{
    Task<CollaborationRecording?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(CollaborationRecording recording, CancellationToken ct);
    Task UpdateAsync(CollaborationRecording recording, CancellationToken ct);
    Task<IEnumerable<CollaborationRecording>> GetByCollaborationAsync(Guid collaborationId, CancellationToken ct);
}
