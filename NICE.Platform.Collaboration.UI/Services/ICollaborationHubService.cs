using NICE.Platform.Collaboration.UI.Models;

namespace NICE.Platform.Collaboration.UI.Services;

public interface ICollaborationHubService
{
    HubConnectionState ConnectionState { get; }

    // ── Events ─────────────────────────────────────────────────────────────
    event Func<string, string, Task>?                           OnCollaborationCreated;
    event Func<string, string, string, Task>?                   OnNewCollaborationRequest;
    event Func<string, string, string, Task>?                   OnCollaborationAccepted;
    /// <summary>
    /// Fired when an agent accepts a collaboration — session is now ACTIVE (both parties connected).
    /// Args: (collabId, customerName, agentName).
    /// Intended for supervisor pages that only show sessions once both sides are live.
    /// </summary>
    event Func<string, string, string, Task>?                   OnSessionActivated;
    event Func<string, Task>?                                   OnCollaborationRequestTaken;
    event Func<string, Task>?                                   OnCollaborationEnded;
    event Func<ChatMessage, Task>?                              OnMessageReceived;
    event Func<ChatMessage, Task>?                              OnWhisperReceived;
    event Func<string, Task>?                                   OnSupervisorJoined;
    event Func<string, string, string, Task>?                   OnSupervisorInviteReceived;
    event Func<string, string, Task>?                           OnOffer;    // (collabId, sdp)
    event Func<string, string, Task>?                           OnAnswer;   // (collabId, sdp)
    event Func<string, Task>?                                   OnIceCandidate;
    event Func<string, Task>?                                   OnScreenOfferRequested;
    event Func<string, Task>?                                   OnScreenShareStopped;
    event Func<string, string, Task>?                           OnUserJoined;
    event Func<string, string, Task>?                           OnUserLeft;
    event Action?                                               OnConnectionChanged;
    event Func<string, string, string, Task>?                   OnCollaborationTransferred;
    event Func<string, string, string, Task>?                   OnTransferReceived;
    /// <summary>
    /// Fired when an Internal staff member sends a direct chat request to THIS user specifically.
    /// Args: (collabId, senderName, senderUserType). Only the targeted user receives this.
    /// </summary>
    event Func<string, string, string, Task>?                   OnInternalDirectChatReceived;
    event Action<string>?                                       OnForceDisconnected;
    event Func<List<ActiveChannelInfo>, Task>?                  OnInternalChannelsUpdated;
    event Func<List<OnlineUserInfo>, Task>?                     OnOnlineUsersUpdated;
    event Func<ChatMessage, Task>?                              OnBotMessageReceived;
    event Func<string, Task>?                                   OnBotSuggestsEscalation;
    event Func<string, List<CollaborationMessageDto>, Task>?    OnChatHistory;

    // ── Standalone session events ───────────────────────────────────────────
    /// <summary>Fired when a Standalone user starts a session (received by both the standalone user and all StandaloneMonitors).</summary>
    event Func<StandaloneSessionInfo, Task>?  OnStandaloneSessionStarted;
    /// <summary>Fired when the server returns the full list of active standalone sessions.</summary>
    event Func<List<StandaloneSessionInfo>, Task>? OnStandaloneSessionsList;

    // ── Connection ─────────────────────────────────────────────────────────
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    // ── Collaboration ──────────────────────────────────────────────────────
    Task RequestCollaborationAsync(string? preferredAgentId = null);
    Task AcceptCollaborationAsync(string collaborationId);
    Task EndCollaborationAsync(string collaborationId, string? reason = null);
    Task TransferCollaborationAsync(string collaborationId, string toAgentId, string? reason = null);
    Task InviteSupervisorAsync(string collaborationId, string supervisorId);

    // ── Messaging ──────────────────────────────────────────────────────────
    Task SendMessageAsync(string collaborationId, string content);
    Task SendWhisperAsync(string collaborationId, string content);

    /// <summary>
    /// Sends a user message to the server bot pipeline.
    /// The server replies asynchronously via the <c>BotReply</c> SignalR event which
    /// fires <see cref="OnBotMessageReceived"/> on the client.
    /// When <c>UseRealBot=false</c> the server returns an empty string so ExternalChat
    /// falls back to its local keyword-match mock automatically.
    /// </summary>
    Task AskBotAsync(string sessionId, string userMessage, string apiKey, string apiAccessKey);

    // ── Groups ─────────────────────────────────────────────────────────────
    Task JoinCollaborationAsync(string collaborationId);
    Task LeaveCollaborationAsync(string collaborationId);
    Task SupervisorJoinAsync(string collaborationId);
    /// <summary>Join a collab's SilentMonitor group (real-time whispers) without formally participating.</summary>
    Task JoinSilentlyAsync(string collaborationId);

    // ── WebRTC / Screen share ──────────────────────────────────────────────
    Task ShareScreenOfferAsync(string collaborationId, string sdp);
    Task ShareScreenAnswerAsync(string collaborationId, string sdp);
    Task RequestScreenOfferAsync(string collaborationId);
    Task StopScreenShareAsync(string collaborationId);

    // ── Standalone ─────────────────────────────────────────────────────────
    /// <summary>Standalone user creates a collaboration + joins its SignalR group.</summary>
    Task StartStandaloneSessionAsync();
    /// <summary>StandaloneMonitor joins a specific standalone collaboration group to receive the WebRTC stream.</summary>
    Task JoinStandaloneSessionAsync(string collaborationId);
    /// <summary>Ask server to push the current list of active standalone sessions.</summary>
    Task GetStandaloneSessionsAsync();
}
