namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// The core collaboration session between one or more participants.
/// Created on the first message for chat-based flows, or immediately for StandAlone.
/// </summary>
public class Collaboration
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>
    /// The primary external user this collaboration is serving.
    /// Nullable — StandAlone sessions may not have an external user.
    /// </summary>
    public Guid? ExternalUserId { get; set; }

    /// <summary>
    /// Current state: Pending | BotHandling | AgentHandling | Ended | Transferred | Abandoned.
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Chat mode configured for the application at start time (snapshot).</summary>
    public string ChatMode { get; set; } = string.Empty;

    /// <summary>Whether screen sharing is active for this collaboration.</summary>
    public bool IsScreenSharing { get; set; }

    /// <summary>Whether a recording has been or is being made.</summary>
    public bool IsRecorded { get; set; }

    /// <summary>UTC time the collaboration was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC time the collaboration ended. Null while active.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>How the collaboration ended: Completed | Abandoned | Transferred | TimedOut.</summary>
    public string? EndReason { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public CollaborationApplication                 Application    { get; set; } = null!;
    public CollaborationUser?                       ExternalUser   { get; set; }
    public ICollection<CollaborationParticipant>    Participants   { get; set; } = [];
    public ICollection<CollaborationMessage>        Messages       { get; set; } = [];
    public ICollection<CollaborationBotMessage>     BotMessages    { get; set; } = [];
    public ICollection<CollaborationRecording>      Recordings     { get; set; } = [];
    public ICollection<CollaborationTransferRequest> Transfers     { get; set; } = [];
}
