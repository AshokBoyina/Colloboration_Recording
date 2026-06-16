namespace NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StopRecording;
using MediatR;
public record StopRecordingCommand(
    Guid   RecordingId,
    Guid   StoppedByUserId,
    string BlobPath)
    : IRequest<NICE.Platform.Collaboration.Core.Responses.RecordingResponse>;
