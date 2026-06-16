namespace NICE.Platform.Collaboration.Core.Events;
public abstract class BaseDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
