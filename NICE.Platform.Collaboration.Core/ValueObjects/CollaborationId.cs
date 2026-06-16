namespace NICE.Platform.Collaboration.Core.ValueObjects;
public record CollaborationId(Guid Value)
{
    public static CollaborationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
