namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// A chat message sent by a human participant within a collaboration.
/// Bot messages are stored separately in CollaborationBotMessage.
/// </summary>
public class CollaborationMessage
{
    public Guid Id { get; set; }

    public Guid CollaborationId { get; set; }

    /// <summary>The user who sent the message.</summary>
    public Guid SenderId { get; set; }

    /// <summary>Sender role at the time of sending.</summary>
    public string SenderType { get; set; } = string.Empty;

    /// <summary>Message body text. Null for attachment-only messages.</summary>
    public string? Body { get; set; }

    /// <summary>
    /// Message type: Text | Attachment | System | Whisper.
    /// Whisper = internal note visible only to agents/supervisors.
    /// </summary>
    public string MessageType { get; set; } = "Text";

    /// <summary>Whether this message has been soft-deleted (body redacted).</summary>
    public bool IsDeleted { get; set; }

    public DateTime SentAt { get; set; }

    /// <summary>UTC timestamp when the external user read the message. Null until read.</summary>
    public DateTime? ReadAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Collaboration                      CollaborationEntity { get; set; } = null!;
    public CollaborationUser                  Sender              { get; set; } = null!;
    public ICollection<CollaborationAttachment> Attachments       { get; set; } = [];
}
