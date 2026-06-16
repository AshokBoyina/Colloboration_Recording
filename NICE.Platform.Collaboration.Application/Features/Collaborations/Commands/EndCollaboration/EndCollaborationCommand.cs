namespace NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.EndCollaboration;
using MediatR;
public record EndCollaborationCommand(
    Guid   CollaborationId,
    Guid   RequestingUserId,
    string Reason = "Completed")
    : IRequest<NICE.Platform.Collaboration.Core.Responses.CollaborationResponse>;
