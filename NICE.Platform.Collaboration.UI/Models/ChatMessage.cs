namespace NICE.Platform.Collaboration.UI.Models;

public class ChatMessage
{
    public string  MessageId       { get; set; } = Guid.NewGuid().ToString();
    public string  CollaborationId { get; set; } = string.Empty;
    public string  SenderId        { get; set; } = string.Empty;
    public string  SenderName      { get; set; } = string.Empty;
    /// <summary>"External", "Agent", "Supervisor", or "System"</summary>
    public string  SenderRole      { get; set; } = string.Empty;
    public string  Content         { get; set; } = string.Empty;
    public bool    IsWhisper       { get; set; }
    public bool    IsBot           { get; set; }
    public bool    IsMine          { get; set; }
    public DateTime SentAt         { get; set; } = DateTime.UtcNow;

    // ── Convenience aliases used by InternalChat ───────────────────────────
    /// <summary>Collaboration ID as Guid (parsed from CollaborationId string).</summary>
    public string CollabId
    {
        get => CollaborationId;
        set => CollaborationId = value;
    }

    /// <summary>Sender ID as Guid-string. Set directly as string in ExternalChat (legacy).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
}
