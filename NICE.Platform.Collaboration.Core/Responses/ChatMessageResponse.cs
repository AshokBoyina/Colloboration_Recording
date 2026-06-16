namespace NICE.Platform.Collaboration.Core.Responses;
public class ChatMessageResponse
{
    public Guid Id { get; set; }
    /// <summary>The collaboration this message belongs to.</summary>
    public Guid CollaborationId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    /// <summary>Role of the sender: "External", "Agent", "Supervisor", "System"</summary>
    public string SenderRole { get; set; } = string.Empty;
    public string Content { get; set; } = default!;
    public int SequenceNumber { get; set; }
    public Guid? ReplyToId { get; set; }
    public bool IsSystemNotice { get; set; }
    public bool IsWhisper { get; set; }
    public DateTime SentAt { get; set; }
}
