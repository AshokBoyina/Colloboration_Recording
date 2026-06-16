namespace NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.EndCollaboration;
using FluentValidation;
public class EndCollaborationCommandValidator : AbstractValidator<EndCollaborationCommand>
{
    public EndCollaborationCommandValidator()
    {
        RuleFor(x => x.CollaborationId).NotEmpty();
    }
}
