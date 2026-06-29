namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Queries.GetRecordingsByCollaboration;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Recordings.Queries.GetRecordingsByCollaboration;
using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class GetRecordingsByCollaborationQueryHandler(CollaborationDbContext db)
    : IRequestHandler<GetRecordingsByCollaborationQuery, IEnumerable<RecordingResponse>>
{
    public async Task<IEnumerable<RecordingResponse>> Handle(
        GetRecordingsByCollaborationQuery request, CancellationToken cancellationToken)
    {
        var recordings = await db.Recordings
            .AsNoTracking()
            .Where(r => r.CollaborationId == request.CollaborationId && !r.IsDeleted)
            .OrderBy(r => r.StartedAt)
            .ToListAsync(cancellationToken);

        return recordings.Select(r => new RecordingResponse
        {
            Id              = r.Id,
            CollaborationId = r.CollaborationId,
            RecordingType   = r.RecordingType,
            Status          = r.Status,
            StartedAt       = r.StartedAt,
            StoppedAt       = r.StoppedAt,
            DurationSeconds = r.DurationSeconds,
            BlobUri         = r.BlobUri
        });
    }
}
