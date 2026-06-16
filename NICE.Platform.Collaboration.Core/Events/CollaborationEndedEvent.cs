namespace NICE.Platform.Collaboration.Core.Events;
public class CollaborationEndedEvent : BaseDomainEvent
{
    public Guid CollaborationId { get; init; }
}
