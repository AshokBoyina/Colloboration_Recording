namespace NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.StartCollaboration;

using MediatR;
using NICE.Platform.Collaboration.Core.Responses;

/// <param name="PreferredAgentId">Optional — null means "any available agent can accept".</param>
/// <param name="DesiredId">
/// Optional — caller-supplied GUID to use as the Collaboration ID (e.g. the host app's own session ID).
/// If null the server generates a new GUID.  The handler rejects duplicate IDs.
/// </param>
public record StartCollaborationCommand(
    Guid   UserId,
    Guid?  PreferredAgentId,
    Guid   ApplicationId,
    Guid?  DesiredId = null)
    : IRequest<CollaborationResponse>;
