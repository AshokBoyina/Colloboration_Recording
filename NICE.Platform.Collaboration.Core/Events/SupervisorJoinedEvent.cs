namespace NICE.Platform.Collaboration.Core.Events;
public class SupervisorJoinedEvent : BaseDomainEvent
{
    public Guid CollaborationId { get; init; }
    public Guid SupervisorId { get; init; }
    public bool IsSilent { get; init; }
}
