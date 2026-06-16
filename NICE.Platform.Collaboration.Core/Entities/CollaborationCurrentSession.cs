namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Represents an active engine session for a connected user.
/// Rows are deleted when the session ends; historical data lives in CollaborationUserSession.
/// </summary>
public class CollaborationCurrentSession
{
    /// <summary>Internal session PK — same GUID issued in the JWT (sid claim).</summary>
    public Guid Id { get; set; }

    /// <summary>The application this session belongs to.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>The user this session belongs to.</summary>
    public Guid UserId { get; set; }

    /// <summary>Role at the time of connection (External | Internal | Agent | Supervisor | StandAlone).</summary>
    public string UserType { get; set; } = string.Empty;

    /// <summary>Auth provider used for the initial token validation (READI | NICE | ANON).</summary>
    public string AuthProvider { get; set; } = string.Empty;

    /// <summary>The SignalR connection ID(s) — may be updated if the client reconnects.</summary>
    public string? SignalRConnectionId { get; set; }

    /// <summary>UTC timestamp when the session was established.</summary>
    public DateTime ConnectedAt { get; set; }

    /// <summary>UTC timestamp of the last heartbeat / activity ping from this session.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Active collaboration the user is currently in, if any.</summary>
    public Guid? CurrentCollaborationId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public CollaborationApplication Application { get; set; } = null!;
    public CollaborationUser         User        { get; set; } = null!;
}
