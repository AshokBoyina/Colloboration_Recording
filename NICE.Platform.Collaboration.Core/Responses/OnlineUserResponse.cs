namespace NICE.Platform.Collaboration.Core.Responses;

/// <summary>
/// A non-External user currently connected to the SignalR hub for an application.
/// Backed by the CurrentSessions table — a row exists only while the hub connection is live.
/// </summary>
public class OnlineUserResponse
{
    public Guid     UserId          { get; set; }
    public string   DisplayName     { get; set; } = string.Empty;
    /// <summary>"Agent", "Supervisor", or "Internal"</summary>
    public string   UserType        { get; set; } = string.Empty;
    public DateTime ConnectedAt     { get; set; }
    /// <summary>Name of the application the user is connected under. Populated by the global /internal/online endpoint; empty for per-app queries.</summary>
    public string   ApplicationName { get; set; } = string.Empty;
}
