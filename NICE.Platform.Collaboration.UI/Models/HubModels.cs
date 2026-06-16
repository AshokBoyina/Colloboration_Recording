namespace NICE.Platform.Collaboration.UI.Models;

/// <summary>
/// Simplified hub connection state for Blazor WASM components.
/// Mirrors <c>Microsoft.AspNetCore.SignalR.Client.HubConnectionState</c> but avoids
/// a transitive package reference in components that only need the enum.
/// <see cref="CollaborationHubService"/> maps from the SignalR enum to this one.
/// </summary>
public enum HubConnectionState
{
    Disconnected,
    Connected,
    Connecting,
    Reconnecting
}

/// <summary>
/// A single agent's active customer session displayed in the AgentDashboard sidebar.
/// One row is created per <c>CollaborationCreated</c> hub event and removed on <c>CollaborationEnded</c>.
/// </summary>
public class ActiveSession
{
    public string            CollabId        { get; }
    public string            CustomerName    { get; }
    public List<ChatMessage> Messages        { get; } = [];
    public bool              IsScreenSharing { get; set; }
    public DateTime          StartedAt       { get; }      = DateTime.UtcNow;
    public string?           SupervisorName  { get; set; }

    public ActiveSession(string collabId, string customerName)
    {
        CollabId     = collabId;
        CustomerName = customerName;
    }
}

/// <summary>
/// A live standalone recording session shown in the StandaloneMonitor sidebar.
/// Created on <c>StandaloneSessionStarted</c> and removed on <c>CollaborationEnded</c>.
/// </summary>
public class StandaloneSessionInfo
{
    public string   CollaborationId { get; set; } = "";
    public string   UserName        { get; set; } = "";
    public string   UserId          { get; set; } = "";
    public DateTime StartedAt       { get; set; } = DateTime.UtcNow;

    /// <summary>True once the monitor has joined and the WebRTC stream is live.</summary>
    public bool IsStreaming { get; set; }
}

/// <summary>
/// A collaboration session being watched by a supervisor in <c>SupervisorView</c>.
/// Populated on <c>NewCollaborationRequest</c> and supervisor-invite hub events.
/// </summary>
public class WatchedCollab
{
    public string            CollabId        { get; }
    public string            CustomerName    { get; }
    public List<ChatMessage> Messages        { get; } = [];
    public bool              IsJoined        { get; set; }
    public bool              IsScreenSharing { get; set; }
    public bool              HistoryLoaded   { get; set; }
    public DateTime          StartedAt       { get; }

    public WatchedCollab(string collabId, string customerName, DateTime? startedAt = null)
    {
        CollabId     = collabId;
        CustomerName = customerName;
        StartedAt    = startedAt ?? DateTime.UtcNow;
    }
}
