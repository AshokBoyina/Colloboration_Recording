namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Commands.DeleteRecordingsByCollaboration;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.DeleteRecordingsByCollaboration;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Deletes recording media via <see cref="IBlobStorageService"/> — which is wired to
/// LocalDiskStorageService or AzureBlobStorageService by FeatureFlags:UseAzureBlob — so
/// the same call works for both stores. Rows are soft-deleted (IsDeleted/DeletedAt) for audit.
/// </summary>
public sealed class DeleteRecordingsByCollaborationCommandHandler(
    CollaborationDbContext db,
    IBlobStorageService    blob,
    IRecordingStreamStore  store,
    ILogger<DeleteRecordingsByCollaborationCommandHandler> logger)
    : IRequestHandler<DeleteRecordingsByCollaborationCommand, DeleteRecordingsResult>
{
    public async Task<DeleteRecordingsResult> Handle(
        DeleteRecordingsByCollaborationCommand request, CancellationToken ct)
    {
        var notes   = new List<string>();
        int deleted = 0, skipped = 0, already = 0;

        var recordings = await db.Recordings
            .Where(r => r.CollaborationId == request.CollaborationId)
            .ToListAsync(ct);

        if (recordings.Count == 0)
            notes.Add($"No recordings found for collaboration {request.CollaborationId}.");

        var now = DateTime.UtcNow;

        foreach (var rec in recordings)
        {
            if (rec.IsDeleted) { already++; continue; }

            // Refuse to delete a recording that is still being written.
            if (store.GetLocalPath(rec.Id) is not null)
            {
                skipped++;
                notes.Add($"Recording {rec.Id} is still in progress — stop it before deleting.");
                continue;
            }

            // Delete the media from whichever store is configured (local disk or Azure blob).
            if (!string.IsNullOrWhiteSpace(rec.BlobUri))
            {
                try
                {
                    await blob.DeleteAsync(rec.BlobUri, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete media for recording {Id} ({Blob}).", rec.Id, rec.BlobUri);
                    notes.Add($"Recording {rec.Id}: media delete failed ({ex.Message}); row left intact.");
                    continue;   // don't mark deleted if the media is still there
                }
            }

            rec.IsDeleted       = true;
            rec.DeletedAt       = now;
            rec.DeletedByUserId = request.DeletedByUserId;
            rec.Status          = "Deleted";
            deleted++;
        }

        if (deleted > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "DeleteRecordings: collab={Collab} deleted={Deleted} skipped={Skipped} already={Already}.",
            request.CollaborationId, deleted, skipped, already);

        return new DeleteRecordingsResult(request.CollaborationId, deleted, skipped, already, notes);
    }
}
