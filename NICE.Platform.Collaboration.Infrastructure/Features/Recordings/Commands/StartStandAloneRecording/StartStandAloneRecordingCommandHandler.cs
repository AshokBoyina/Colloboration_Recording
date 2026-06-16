namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Commands.StartStandAloneRecording;

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartStandAloneRecording;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class StartStandAloneRecordingCommandHandler(
    CollaborationDbContext        db,
    IRecordingStreamStore         store,
    ILogger<StartStandAloneRecordingCommandHandler> logger)
    : IRequestHandler<StartStandAloneRecordingCommand, StartStandAloneRecordingResponse>
{
    public async Task<StartStandAloneRecordingResponse> Handle(
        StartStandAloneRecordingCommand request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // 1. Resolve the StandAlone Collaboration for this recording session.
        //    If the caller supplied a CollaborationId, reuse the existing collaboration
        //    (so multiple recordings correlate to the same host session) or create it
        //    with that id. Otherwise generate a fresh collaboration id server-side.
        var collabId = request.CollaborationId ?? Guid.NewGuid();

        var collab = request.CollaborationId is null
            ? null
            : await db.Collaborations
                      .FirstOrDefaultAsync(c => c.Id == collabId, cancellationToken);

        if (collab is null)
        {
            collab = new Collaboration
            {
                Id             = collabId,
                ApplicationId  = request.ApplicationId,
                ExternalUserId = null,              // no customer for StandAlone
                Status         = "AgentHandling",
                ChatMode       = "StandAlone",
                IsRecorded     = true,
                CreatedAt      = now
            };
            await db.Collaborations.AddAsync(collab, cancellationToken);

            // 2. Add agent as participant (only when creating the collaboration)
            var participant = new CollaborationParticipant
            {
                Id              = Guid.NewGuid(),
                CollaborationId = collab.Id,
                UserId          = request.AgentUserId,
                UserType        = "Agent",
                JoinedAt        = now,
                IsActiveAgent   = true
            };
            await db.Participants.AddAsync(participant, cancellationToken);
        }

        // 3. Create the Recording row (Status = Recording while active)
        var recording = new CollaborationRecording
        {
            Id              = Guid.NewGuid(),
            CollaborationId = collab.Id,
            RecordingType   = "Screen",
            StartedAt       = now,
            Status          = "Recording"
        };
        await db.Recordings.AddAsync(recording, cancellationToken);

        // 4. Prime the stream store (creates local file / Azure append blob)
        var blobPath = await store.InitAsync(recording.Id, cancellationToken);
        recording.BlobUri = blobPath;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "StandAlone recording started: RecordingId={RId} CollabId={CId} Agent={UserId}",
            recording.Id, collab.Id, request.AgentUserId);

        return new StartStandAloneRecordingResponse
        {
            CollaborationId = collab.Id,
            RecordingId     = recording.Id,
            BlobPath        = blobPath
        };
    }
}
