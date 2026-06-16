namespace NICE.Platform.Collaboration.Core.Entities;
public class ChatMessage
{
    public Guid Id { get; private set; }
    public Guid CollaborationId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = default!;
    public int SequenceNumber { get; private set; }
    public Guid? ReplyToId { get; private set; }
    public bool IsSystemNotice { get; private set; }
    public bool IsHidden { get; private set; }
    public DateTime SentAt { get; private set; }
}
