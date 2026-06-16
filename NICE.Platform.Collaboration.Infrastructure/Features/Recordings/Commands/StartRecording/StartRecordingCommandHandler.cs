namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Commands.StartRecording;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartRecording;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Exceptions;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class StartRecordingCommandHandler(
    CollaborationDbContext    db,
    IRecordingStreamStore     streamStore,
    ILogger<StartRecordingCommandHandler> logger)
    : IRequestHandler<StartRecordingCommand, RecordingResponse>
{
    public async Task<RecordingResponse> Handle(
        StartRecordingCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "StartRecording: collab={CollabId} type={Type} by={UserId}",
            request.CollaborationId, request.RecordingType, request.InitiatedByUserId);

        var collab = await db.Collaborations.FindAsync(
            [request.CollaborationId], cancellationToken)
            ?? throw new CollaborationNotFoundException(request.CollaborationId);

        var now = DateTime.UtcNow;
        var recording = new CollaborationRecording
        {
            Id              = Guid.NewGuid(),
            CollaborationId = request.CollaborationId,
            RecordingType   = request.RecordingType,
            StartedAt       = now,
            Status          = "Recording"
        };

        await db.Recordings.AddAsync(recording, cancellationToken);

        collab.IsRecorded = true;
        db.Collaborations.Update(collab);
        await db.SaveChangesAsync(cancellationToken);

        // ── Open the file stream NOW so chunk uploads don't get dropped ────────
        // LocalDiskStreamStore keeps a FileStream open per recordingId.
        // If InitAsync is not called before the first chunk arrives, AppendChunkAsync
        // finds no entry in _streams and silently discards the data.
        try
        {
            await streamStore.InitAsync(recording.Id, cancellationToken);
            logger.LogInformation("RecordingStream opened for {Id}", recording.Id);
        }
        catch (Exception ex)
        {
            // Non-fatal — log and continue; chunks will be dropped but the DB row is valid.
            logger.LogError(ex, "Failed to open recording stream for {Id}", recording.Id);
        }

        return new RecordingResponse
        {
            Id              = recording.Id,
            CollaborationId = recording.CollaborationId,
            RecordingType   = recording.RecordingType,
            Status          = recording.Status,
            StartedAt       = recording.StartedAt
        };
    }
}
