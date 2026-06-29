namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Commands.PurgeRecordings;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.PurgeRecordings;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Deletes the media (via IBlobStorageService — local disk or Azure Blob depending on
/// FeatureFlags:UseAzureBlob) and soft-deletes the rows for every recording started on or
/// before the cutoff. In-progress recordings are skipped.
/// </summary>
public sealed class PurgeRecordingsCommandHandler(
    CollaborationDbContext db,
    IBlobStorageService    blob,
    IRecordingStreamStore  store,
    ILogger<PurgeRecordingsCommandHandler> logger)
    : IRequestHandler<PurgeRecordingsCommand, PurgeRecordingsResult>
{
    public async Task<PurgeRecordingsResult> Handle(
        PurgeRecordingsCommand request, CancellationToken ct)
    {
        // A bare date (midnight) means "the whole of that day" → exclusive next-day bound.
        // A date+time is an exact cutoff (inclusive).
        var input  = request.OnOrBefore;
        var cutoff = input.TimeOfDay == TimeSpan.Zero
            ? input.Date.AddDays(1)   // exclusive upper bound covering the entire day
            : input;
        var inclusive = input.TimeOfDay != TimeSpan.Zero;

        var query = db.Recordings.Where(r => !r.IsDeleted);
        query = inclusive
            ? query.Where(r => r.StartedAt <= cutoff)
            : query.Where(r => r.StartedAt <  cutoff);

        var recordings = await query.ToListAsync(ct);

        var notes   = new List<string>();
        int deleted = 0, skipped = 0, failed = 0;
        var now     = DateTime.UtcNow;

        foreach (var rec in recordings)
        {
            // Don't purge a recording that is still being written.
            if (store.GetLocalPath(rec.Id) is not null)
            {
                skipped++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rec.BlobUri))
            {
                try
                {
                    await blob.DeleteAsync(rec.BlobUri, ct);
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogError(ex, "Purge: failed to delete media for recording {Id} ({Blob}).",
                        rec.Id, rec.BlobUri);
                    notes.Add($"Recording {rec.Id}: media delete failed ({ex.Message}); row left intact.");
                    continue;   // don't soft-delete if media still present
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
            "Purge: cutoff={Cutoff:o} deleted={Deleted} skipped={Skipped} failed={Failed}.",
            cutoff, deleted, skipped, failed);

        return new PurgeRecordingsResult(cutoff, deleted, skipped, failed, notes);
    }
}
