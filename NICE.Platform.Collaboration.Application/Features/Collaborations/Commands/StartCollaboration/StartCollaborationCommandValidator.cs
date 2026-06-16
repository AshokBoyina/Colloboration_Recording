namespace NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.StartCollaboration;
using FluentValidation;
public class StartCollaborationCommandValidator : AbstractValidator<StartCollaborationCommand>
{
    public StartCollaborationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        // PreferredAgentId is intentionally optional — null means the collaboration
        // goes to any available agent.
        RuleFor(x => x.ApplicationId).NotEmpty();
    }
}
