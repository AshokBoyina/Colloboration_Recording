namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartRecording;
using FluentValidation;
public class StartRecordingCommandValidator : AbstractValidator<StartRecordingCommand>
{
    public StartRecordingCommandValidator()
    {
        RuleFor(x => x.CollaborationId).NotEmpty();
    }
}
