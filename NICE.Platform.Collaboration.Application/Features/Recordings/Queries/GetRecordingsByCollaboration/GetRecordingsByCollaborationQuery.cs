namespace NICE.Platform.Collaboration.Application.Features.Recordings.Queries.GetRecordingsByCollaboration;
using MediatR;
public record GetRecordingsByCollaborationQuery(Guid CollaborationId) : IRequest<IEnumerable<NICE.Platform.Collaboration.Core.Responses.RecordingResponse>>;
