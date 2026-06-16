namespace NICE.Platform.Collaboration.Core.Events;
public class CollaborationStartedEvent : BaseDomainEvent
{
    public Guid CollaborationId { get; init; }
    public Guid AgentId { get; init; }
}
