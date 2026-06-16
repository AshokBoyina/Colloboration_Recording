namespace NICE.Platform.Collaboration.Infrastructure.Features.Collaborations.Commands.StartCollaboration;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.StartCollaboration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class StartCollaborationCommandHandler(
    CollaborationDbContext     db,
    ILogger<StartCollaborationCommandHandler> logger)
    : IRequestHandler<StartCollaborationCommand, CollaborationResponse>
{
    public async Task<CollaborationResponse> Handle(
        StartCollaborationCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "StartCollaboration: user={UserId} preferredAgent={PreferredAgentId} app={AppId}",
            request.UserId, request.PreferredAgentId?.ToString() ?? "any", request.ApplicationId);

        // Ensure the external user exists
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == request.UserId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"User {request.UserId} not found. Call onboard first.");

        var now = DateTime.UtcNow;

        // Resolve collaboration ID: use the caller-supplied one if provided, else generate.
        var collabId = request.DesiredId ?? Guid.NewGuid();

        // Reject duplicate IDs — prevents accidental session collisions when host apps
        // reuse the same ID for two concurrent recordings.
        if (request.DesiredId.HasValue)
        {
            var collision = await db.Collaborations.AnyAsync(
                c => c.Id == collabId, cancellationToken);
            if (collision)
                throw new InvalidOperationException(
                    $"A collaboration with ID {collabId} already exists. " +
                    "Supply a fresh GUID or omit sessionId to let the server generate one.");
        }

        var collab = new Collaboration
        {
            Id             = collabId,
            ApplicationId  = request.ApplicationId,
            ExternalUserId = request.UserId,
            Status         = "Waiting",
            ChatMode       = "Live",
            IsScreenSharing = false,
            IsRecorded     = false,
            CreatedAt      = now
        };
        await db.Collaborations.AddAsync(collab, cancellationToken);

        // Add the external user as first participant
        var participant = new CollaborationParticipant
        {
            Id              = Guid.NewGuid(),
            CollaborationId = collab.Id,
            UserId          = request.UserId,
            UserType        = "External",
            JoinedAt        = now,
            IsActiveAgent   = false
        };
        await db.Participants.AddAsync(participant, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new CollaborationResponse
        {
            Id        = collab.Id,
            Status    = collab.Status,
            Type      = collab.ChatMode,
            StartedAt = collab.CreatedAt,
        };
    }
}
