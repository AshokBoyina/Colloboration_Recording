namespace NICE.Platform.Collaboration.Infrastructure.Features.Collaborations.Commands.EndCollaboration;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.EndCollaboration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Core.Exceptions;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class EndCollaborationCommandHandler(
    CollaborationDbContext db,
    ILogger<EndCollaborationCommandHandler> logger)
    : IRequestHandler<EndCollaborationCommand, CollaborationResponse>
{
    public async Task<CollaborationResponse> Handle(
        EndCollaborationCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "EndCollaboration: collab={CollabId} by={UserId} reason={Reason}",
            request.CollaborationId, request.RequestingUserId, request.Reason);

        var collab = await db.Collaborations
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == request.CollaborationId, cancellationToken)
            ?? throw new CollaborationNotFoundException(request.CollaborationId);

        var now       = DateTime.UtcNow;
        collab.Status    = "Ended";
        collab.EndedAt   = now;
        collab.EndReason = request.Reason;
        db.Collaborations.Update(collab);

        // Stamp all open participants
        foreach (var p in collab.Participants.Where(p => p.LeftAt == null))
        {
            p.LeftAt = now;
            db.Participants.Update(p);
        }

        // Clear CurrentCollaborationId for everyone who was in this collab
        var activeSessions = await db.CurrentSessions
            .Where(s => s.CurrentCollaborationId == request.CollaborationId)
            .ToListAsync(cancellationToken);
        foreach (var s in activeSessions)
        {
            s.CurrentCollaborationId = null;
            db.CurrentSessions.Update(s);
        }

        await db.SaveChangesAsync(cancellationToken);

        return new CollaborationResponse
        {
            Id        = collab.Id,
            Status    = collab.Status,
            Type      = collab.ChatMode,
            StartedAt = collab.CreatedAt,
            EndedAt   = collab.EndedAt
        };
    }
}
