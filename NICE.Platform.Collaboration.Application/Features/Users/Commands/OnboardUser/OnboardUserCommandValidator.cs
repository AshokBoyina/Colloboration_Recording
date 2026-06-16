namespace NICE.Platform.Collaboration.Application.Features.Users.Commands.OnboardUser;
using FluentValidation;
public class OnboardUserCommandValidator : AbstractValidator<OnboardUserCommand>
{
    public OnboardUserCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.ExternalId).NotEmpty();
    }
}
