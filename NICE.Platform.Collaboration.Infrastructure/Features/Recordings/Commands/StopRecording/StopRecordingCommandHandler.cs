namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Commands.StopRecording;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StopRecording;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class StopRecordingCommandHandler(
    CollaborationDbContext db,
    ILogger<StopRecordingCommandHandler> logger)
    : IRequestHandler<StopRecordingCommand, RecordingResponse>
{
    public async Task<RecordingResponse> Handle(
        StopRecordingCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "StopRecording: recording={RecordingId} blob={BlobPath} by={UserId}",
            request.RecordingId, request.BlobPath, request.StoppedByUserId);

        var recording = await db.Recordings.FindAsync(
            [request.RecordingId], cancellationToken)
            ?? throw new KeyNotFoundException($"Recording {request.RecordingId} not found.");

        var now               = DateTime.UtcNow;
        recording.StoppedAt   = now;
        recording.DurationSeconds = (int)(now - recording.StartedAt).TotalSeconds;
        recording.BlobUri     = request.BlobPath;
        recording.Status      = "Ready";

        db.Recordings.Update(recording);
        await db.SaveChangesAsync(cancellationToken);

        return new RecordingResponse
        {
            Id              = recording.Id,
            CollaborationId = recording.CollaborationId,
            RecordingType   = recording.RecordingType,
            Status          = recording.Status,
            StartedAt       = recording.StartedAt,
            StoppedAt       = recording.StoppedAt,
            DurationSeconds = recording.DurationSeconds,
            BlobUri         = recording.BlobUri
        };
    }
}
