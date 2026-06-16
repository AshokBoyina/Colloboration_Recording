namespace NICE.Platform.Collaboration.Core.Events;
public class CollaborationTransferredEvent : BaseDomainEvent
{
    public Guid CollaborationId { get; init; }
    public Guid ToAgentId { get; init; }
}
