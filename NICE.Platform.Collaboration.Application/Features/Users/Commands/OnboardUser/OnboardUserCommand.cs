namespace NICE.Platform.Collaboration.Application.Features.Users.Commands.OnboardUser;
using MediatR;
public record OnboardUserCommand(string ExternalId, string Name, string Email, Guid ApplicationId, string Role) : IRequest<NICE.Platform.Collaboration.Core.Responses.SessionResponse>;
