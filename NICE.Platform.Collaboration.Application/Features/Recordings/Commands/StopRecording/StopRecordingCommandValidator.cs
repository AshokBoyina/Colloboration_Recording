namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StopRecording;
using FluentValidation;
public class StopRecordingCommandValidator : AbstractValidator<StopRecordingCommand>
{
    public StopRecordingCommandValidator()
    {
        RuleFor(x => x.RecordingId).NotEmpty();
    }
}
