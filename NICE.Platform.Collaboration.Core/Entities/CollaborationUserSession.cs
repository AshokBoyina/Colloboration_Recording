namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Permanent record of every session a user has ever opened.
/// Written at connect time; EndedAt/DurationSeconds filled when session closes.
/// </summary>
public class CollaborationUserSession
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Role during this session.</summary>
    public string UserType { get; set; } = string.Empty;

    public string AuthProvider { get; set; } = string.Empty;

    public DateTime ConnectedAt { get; set; }

    /// <summary>Null while the session is still open.</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Computed and stored when session closes (EndedAt - ConnectedAt in seconds).</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Reason the session ended: Disconnected | Timeout | Kicked | ServerRestart.</summary>
    public string? EndReason { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public CollaborationApplication Application { get; set; } = null!;
    public CollaborationUser         User        { get; set; } = null!;
}
