namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartRecording;
using MediatR;
public record StartRecordingCommand(
    Guid   CollaborationId,
    Guid   InitiatedByUserId,
    string RecordingType = "Screen")
    : IRequest<NICE.Platform.Collaboration.Core.Responses.RecordingResponse>;
