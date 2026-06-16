namespace NICE.Platform.Collaboration.Infrastructure.Features.Recordings.Queries.GetRecordingSasUrl;

using MediatR;
using NICE.Platform.Collaboration.Application.Features.Recordings.Queries.GetRecordingSasUrl;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public sealed class GetRecordingSasUrlQueryHandler(
    CollaborationDbContext db,
    IBlobStorageService   blobService)
    : IRequestHandler<GetRecordingSasUrlQuery, string>
{
    public async Task<string> Handle(
        GetRecordingSasUrlQuery request, CancellationToken cancellationToken)
    {
        var recording = await db.Recordings.FindAsync(
            [request.RecordingId], cancellationToken)
            ?? throw new KeyNotFoundException($"Recording {request.RecordingId} not found.");

        if (string.IsNullOrWhiteSpace(recording.BlobUri))
            throw new InvalidOperationException(
                $"Recording {request.RecordingId} has no blob URI yet (status: {recording.Status}).");

        return await blobService.GenerateSasUrlAsync(
            recording.BlobUri,
            TimeSpan.FromMinutes(60),
            cancellationToken);
    }
}
