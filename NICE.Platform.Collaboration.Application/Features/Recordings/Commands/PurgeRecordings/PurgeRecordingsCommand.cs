namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.PurgeRecordings;

using MediatR;

/// <summary>
/// Purges (deletes) all recordings started on or before a cutoff date: removes the
/// media from the configured store (local disk OR Azure Blob) and soft-deletes the rows.
/// In-progress recordings are skipped.
/// </summary>
/// <param name="OnOrBefore">
/// Cutoff. A date with no time (midnight) means "the whole of that day" (inclusive);
/// a date+time is treated as an exact UTC cutoff. Recordings whose StartedAt is on/before
/// this are purged.
/// </param>
/// <param name="DeletedByUserId">Optional — the user performing the purge (audit).</param>
public record PurgeRecordingsCommand(
    DateTime OnOrBefore,
    Guid?    DeletedByUserId = null)
    : IRequest<PurgeRecordingsResult>;

/// <summary>Outcome of a purge request.</summary>
public sealed record PurgeRecordingsResult(
    DateTime CutoffUtc,
    int      Deleted,
    int      SkippedInProgress,
    int      Failed,
    IReadOnlyList<string> Notes);
