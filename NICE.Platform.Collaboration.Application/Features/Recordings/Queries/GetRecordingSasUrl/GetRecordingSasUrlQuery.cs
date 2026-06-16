namespace NICE.Platform.Collaboration.Application.Features.Recordings.Queries.GetRecordingSasUrl;
using MediatR;
public record GetRecordingSasUrlQuery(Guid RecordingId) : IRequest<string>;
