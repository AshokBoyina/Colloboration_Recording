namespace NICE.Platform.Collaboration.Core.Events;
public class AgentAssignedEvent : BaseDomainEvent
{
    public Guid CollaborationId { get; init; }
    public Guid AgentId { get; init; }
}
