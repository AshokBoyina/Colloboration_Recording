namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Tracks every user that participated in a collaboration, including join/leave timestamps.
/// Multiple rows per collaboration (one per participant per join event).
/// </summary>
public class CollaborationParticipant
{
    public Guid Id { get; set; }

    public Guid CollaborationId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Role when this participant joined: External | Internal | Agent | Supervisor | StandAlone.</summary>
    public string UserType { get; set; } = string.Empty;

    public DateTime JoinedAt { get; set; }

    /// <summary>Null if still in the collaboration.</summary>
    public DateTime? LeftAt { get; set; }

    /// <summary>Whether this participant is currently the active agent handling the collaboration.</summary>
    public bool IsActiveAgent { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Collaboration     CollaborationEntity { get; set; } = null!;
    public CollaborationUser User                { get; set; } = null!;
}
