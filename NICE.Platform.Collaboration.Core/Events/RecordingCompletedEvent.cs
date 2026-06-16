namespace NICE.Platform.Collaboration.Core.Events;
public class RecordingCompletedEvent : BaseDomainEvent
{
    public Guid CollaborationId { get; init; }
    public string BlobPath { get; init; } = default!;
}
