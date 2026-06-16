namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Application.Interfaces.Repositories;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public class RecordingRepository(CollaborationDbContext db) : IRecordingRepository
{
    public Task<CollaborationRecording?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Recordings.FindAsync([id], ct).AsTask();

    public async Task<IEnumerable<CollaborationRecording>> GetByCollaborationAsync(
        Guid collaborationId, CancellationToken ct)
        => await db.Recordings
            .Where(r => r.CollaborationId == collaborationId)
            .OrderBy(r => r.StartedAt)
            .ToListAsync(ct);

    public async Task AddAsync(CollaborationRecording recording, CancellationToken ct)
    {
        await db.Recordings.AddAsync(recording, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CollaborationRecording recording, CancellationToken ct)
    {
        db.Recordings.Update(recording);
        await db.SaveChangesAsync(ct);
    }
}
