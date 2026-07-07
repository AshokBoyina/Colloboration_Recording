using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using NICE.Platform.Collaboration.UI.Models;

// Alias to disambiguate: Microsoft's enum vs our wrapper enum (same name, different namespace).
using SignalRState = Microsoft.AspNetCore.SignalR.Client.HubConnectionState;
using ChatState    = NICE.Platform.Collaboration.UI.Models.HubConnectionState;

namespace NICE.Platform.Collaboration.UI.Services;

public class CollaborationHubService(HttpClient http, IAuthService auth) : ICollaborationHubService
{
    private HubConnection? _hub;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    // ── State ──────────────────────────────────────────────────────────────────
    // Returns our wrapper enum (ChatState) — ICollaborationHubService declares ChatState.
    public ChatState ConnectionState =>
        _hub?.State switch
        {
            SignalRState.Connected    => ChatState.Connected,
            SignalRState.Connecting   => ChatState.Connecting,
            SignalRState.Reconnecting => ChatState.Reconnecting,
            _                        => ChatState.Disconnected
        };

    // ── Events ─────────────────────────────────────────────────────────────────
    public event Func<string, string, Task>?         OnCollaborationCreated;
    public event Func<string, string, string, Task>? OnNewCollaborationRequest;
    public event Func<string, string, string, Task>? OnCollaborationAccepted;
    public event Func<string, string, string, Task>? OnSessionActivated;
    public event Func<string, Task>?                 OnCollaborationRequestTaken;
    public event Func<string, Task>?                 OnCollaborationEnded;
    public event Func<ChatMessage, Task>?            OnMessageReceived;
    public event Func<ChatMessage, Task>?            OnWhisperReceived;
    public event Func<string, Task>?                 OnSupervisorJoined;
    public event Func<string, string, string, Task>? OnSupervisorInviteReceived;
    public event Func<string, string, Task>?         OnOffer;    // (collabId, sdp)
    public event Func<string, string, Task>?         OnAnswer;   // (collabId, sdp)
    public event Func<string, Task>?                 OnIceCandidate;
    public event Func<string, Task>?                 OnScreenOfferRequested;
    public event Func<string, Task>?                 OnScreenShareStopped;
#pragma warning disable CS0067  // events wired for future use
    public event Func<string, string, Task>?            OnUserJoined;
    public event Func<string, string, Task>?            OnUserLeft;
#pragma warning restore CS0067
    public event Action?                                OnConnectionChanged;
    public event Func<string, string, string, Task>?    OnCollaborationTransferred;
    public event Func<string, string, string, Task>?    OnTransferReceived;
    public event Func<string, string, string, Task>?    OnInternalDirectChatReceived;
    public event Action<string>?                        OnForceDisconnected;
    public event Func<List<ActiveChannelInfo>, Task>?   OnInternalChannelsUpdated;
    public event Func<List<OnlineUserInfo>, Task>?       OnOnlineUsersUpdated;
    public event Func<ChatMessage, Task>?               OnBotMessageReceived;
    public event Func<string, Task>?                    OnBotSuggestsEscalation;
    public event Func<string, List<CollaborationMessageDto>, Task>? OnChatHistory;

    // ── Standalone events ──────────────────────────────────────────────────────
    public event Func<StandaloneSessionInfo, Task>?        OnStandaloneSessionStarted;
    public event Func<List<StandaloneSessionInfo>, Task>?  OnStandaloneSessionsList;

    // ── Connection ─────────────────────────────────────────────────────────────
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // Auth guard — the hub is [Authorize] on the server, so connecting without a
        // session token will always be rejected.  Fail fast on the client side so we
        // never open a TCP connection or log a 401 noise.
        if (string.IsNullOrEmpty(auth.Current.Token))
            throw new InvalidOperationException(
                "Cannot connect to the collaboration hub: no session token. " +
                "Call POST /api/v1/auth/validate first.");

        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }

        var baseUrl = http.BaseAddress!.ToString().TrimEnd('/');
        var hubUrl  = $"{baseUrl}/hubs/v1/collaboration";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                // Never put the JWT in the hub URL. Mint a fresh, single-use, short-lived
                // opaque ticket per request (SignalR calls this for negotiate AND the WS
                // upgrade). OnMessageReceived on the server redeems it. Falls back to the
                // JWT only if ticket minting fails (e.g. offline) so dev still works.
                opts.AccessTokenProvider = async () =>
                    await auth.GetHubTicketAsync() ?? auth.Current.Token;
            })
            .WithAutomaticReconnect()
            .Build();

        RegisterHandlers();

        _hub.Closed      += _ => { OnConnectionChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnecting += _ => { OnConnectionChanged?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnected += _ => { OnConnectionChanged?.Invoke(); return Task.CompletedTask; };

        await _hub.StartAsync(ct);
        OnConnectionChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        if (_hub is not null)
        {
            await _hub.StopAsync();
            await _hub.DisposeAsync();
            _hub = null;
            OnConnectionChanged?.Invoke();
        }
    }


    // ── Outbound hub method calls ──────────────────────────────────────────────
    // Every method maps 1-to-1 to a public Task method on CollaborationHub.cs.
    // Method names must match exactly (SignalR is case-sensitive on the server).

    public Task RequestCollaborationAsync(string? preferredAgentId = null)
        => Invoke("RequestCollaboration", preferredAgentId);

    public Task AcceptCollaborationAsync(string collaborationId)
        => Invoke("AcceptCollaboration", collaborationId);

    public Task EndCollaborationAsync(string collaborationId, string? reason = null)
        => Invoke("EndCollaboration", collaborationId, reason ?? "Completed");

    public Task TransferCollaborationAsync(string collaborationId, string toAgentId, string? reason = null)
        => Invoke("TransferCollaboration", collaborationId, toAgentId, reason ?? "");

    public Task InviteSupervisorAsync(string collaborationId, string supervisorId)
        => Invoke("InviteSupervisor", collaborationId, supervisorId);

    public Task SendMessageAsync(string collaborationId, string content)
        => Invoke("SendMessage", collaborationId, content);

    public Task SendWhisperAsync(string collaborationId, string content)
        => Invoke("SendWhisper", collaborationId, content);

    public Task AskBotAsync(string sessionId, string userMessage, string apiKey, string apiAccessKey)
        => Invoke("AskBot", sessionId, userMessage, apiKey, apiAccessKey);

    public Task JoinCollaborationAsync(string collaborationId)
        => Invoke("JoinCollaborationGroup", collaborationId);

    public Task LeaveCollaborationAsync(string collaborationId)
        => Invoke("LeaveCollaborationGroup", collaborationId);

    public Task JoinSilentlyAsync(string collaborationId)
        => Invoke("JoinSilently", collaborationId);

    public Task SupervisorJoinAsync(string collaborationId)
        => Invoke("SupervisorJoin", collaborationId);

    public Task ShareScreenOfferAsync(string collaborationId, string sdp)
        => Invoke("ShareScreenOffer", collaborationId, sdp);

    public Task ShareScreenAnswerAsync(string collaborationId, string sdp)
        => Invoke("ShareScreenAnswer", collaborationId, sdp);

    public Task RequestScreenOfferAsync(string collaborationId)
        => Invoke("RequestScreenOffer", collaborationId);

    public Task StopScreenShareAsync(string collaborationId)
        => Invoke("StopScreenShare", collaborationId);

    public Task StartStandaloneSessionAsync()
        => Invoke("StartStandaloneSession");

    public Task JoinStandaloneSessionAsync(string collaborationId)
        => Invoke("JoinStandaloneSession", collaborationId);

    public Task GetStandaloneSessionsAsync()
        => Invoke("GetStandaloneSessions");

    // Safe invoke — swallows "hub not connected" race conditions.
    private Task Invoke(string method, params object?[] args)
    {
        if (_hub is null) return Task.CompletedTask;
        try   { return _hub.InvokeCoreAsync(method, args); }
        catch { return Task.CompletedTask; }
    }

    // ── Hub event registration ─────────────────────────────────────────────────
    private void RegisterHandlers()
    {
        if (_hub is null) return;

        _hub.On<object>("CollaborationCreated", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var status   = j.TryGetProperty("status",         out var p2) ? p2.GetString() ?? "" : "";
            return OnCollaborationCreated?.Invoke(collabId, status) ?? Task.CompletedTask;
        });

        _hub.On<object>("NewCollaborationRequest", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId     = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var customerName = j.TryGetProperty("customerName",    out var p2) ? p2.GetString() ?? "" : "";
            var status       = j.TryGetProperty("status",          out var p3) ? p3.GetString() ?? "" : "";
            // Args order: (collabId, customerName, status) — handlers read [1] as the display name
            return OnNewCollaborationRequest?.Invoke(collabId, customerName, status) ?? Task.CompletedTask;
        });

        _hub.On<object>("CollaborationAccepted", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId  = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var status    = j.TryGetProperty("status",          out var p2) ? p2.GetString() ?? "" : "";
            var agentName = j.TryGetProperty("agentName",       out var p3) ? p3.GetString() ?? "" : "";
            return OnCollaborationAccepted?.Invoke(collabId, status, agentName) ?? Task.CompletedTask;
        });

        _hub.On<object>("SessionActivated", raw =>
        {
            var j            = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId     = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var customerName = j.TryGetProperty("customerName",    out var p2) ? p2.GetString() ?? "" : "";
            var agentName    = j.TryGetProperty("agentName",       out var p3) ? p3.GetString() ?? "" : "";
            return OnSessionActivated?.Invoke(collabId, customerName, agentName) ?? Task.CompletedTask;
        });

        _hub.On<string>("CollaborationRequestTaken", collabId =>
            OnCollaborationRequestTaken?.Invoke(collabId) ?? Task.CompletedTask);

        _hub.On<object>("CollaborationEnded", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() :
                           j.TryGetProperty("id",               out var p2) ? p2.GetString() : "";
            return OnCollaborationEnded?.Invoke(collabId ?? "") ?? Task.CompletedTask;
        });

        _hub.On<object>("MessageReceived", raw =>
        {
            try
            {
                var msg = System.Text.Json.JsonSerializer.Deserialize<ChatMessage>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
                if (msg is not null)
                    return OnMessageReceived?.Invoke(msg) ?? Task.CompletedTask;
            }
            catch { }
            return Task.CompletedTask;
        });

        _hub.On<object>("WhisperMessage", raw =>
        {
            try
            {
                var msg = System.Text.Json.JsonSerializer.Deserialize<ChatMessage>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
                if (msg is not null)
                    return OnWhisperReceived?.Invoke(msg) ?? Task.CompletedTask;
            }
            catch { }
            return Task.CompletedTask;
        });

        _hub.On<object>("SupervisorJoined", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId = j.TryGetProperty("collaborationId", out var p) ? p.GetString() ?? "" : "";
            return OnSupervisorJoined?.Invoke(collabId) ?? Task.CompletedTask;
        });

        _hub.On<object>("SupervisorInviteReceived", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId     = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var customerName = j.TryGetProperty("customerName",    out var p2) ? p2.GetString() ?? "" : "";
            var agentName    = j.TryGetProperty("fromAgentName",   out var p3) ? p3.GetString() ?? "" : "";
            // Args order: (collabId, customerName, agentName) — matches OnInviteReceived handler
            return OnSupervisorInviteReceived?.Invoke(collabId, customerName, agentName) ?? Task.CompletedTask;
        });

        // Server sends { collaborationId, sdp } as a single JSON object argument.
        // Using On<JsonElement> avoids the double-serialisation ambiguity of On<object>:
        // in some Blazor WASM builds On<object> can receive a plain System.Object with
        // no properties rather than a JsonElement, causing TryGetProperty to always miss.
        _hub.On<System.Text.Json.JsonElement>("Offer", j =>
        {
            var collabId = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var sdp      = j.TryGetProperty("sdp",             out var p2) ? p2.GetString() ?? "" : "";
            Console.WriteLine($"[HubService] Offer received collabId={collabId[..Math.Min(8, collabId.Length)]} sdpLen={sdp.Length}");
            return OnOffer?.Invoke(collabId, sdp) ?? Task.CompletedTask;
        });

        _hub.On<System.Text.Json.JsonElement>("Answer", j =>
        {
            var collabId = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var sdp      = j.TryGetProperty("sdp",             out var p2) ? p2.GetString() ?? "" : "";
            Console.WriteLine($"[HubService] Answer received collabId={collabId[..Math.Min(8, collabId.Length)]} sdpLen={sdp.Length}");
            return OnAnswer?.Invoke(collabId, sdp) ?? Task.CompletedTask;
        });

        _hub.On<string>("IceCandidate",  ice => OnIceCandidate?.Invoke(ice) ?? Task.CompletedTask);

        _hub.On<string>("ScreenOfferRequested", collabId =>
            OnScreenOfferRequested?.Invoke(collabId) ?? Task.CompletedTask);

        _hub.On<string>("ScreenShareStopped", collabId =>
            OnScreenShareStopped?.Invoke(collabId) ?? Task.CompletedTask);

        _hub.On<string>("ForceDisconnect", reason =>
        {
            OnForceDisconnected?.Invoke(reason);
            return Task.CompletedTask;
        });

        _hub.On<object>("CollaborationTransferred", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId   = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var fromAgent  = j.TryGetProperty("fromAgentName",   out var p2) ? p2.GetString() ?? "" : "";
            var toAgentId  = j.TryGetProperty("toAgentId",       out var p3) ? p3.GetString() ?? "" : "";
            return OnCollaborationTransferred?.Invoke(collabId, fromAgent, toAgentId) ?? Task.CompletedTask;
        });

        _hub.On<object>("TransferReceived", raw =>
        {
            var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId     = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var fromAgent    = j.TryGetProperty("fromAgentName",   out var p2) ? p2.GetString() ?? "" : "";
            var customerName = j.TryGetProperty("customerName",    out var p3) ? p3.GetString() ?? "" : "";
            // Args order: (collabId, fromAgent, customerName) — matches OnTransferReceived(collabId, fromAgentId, customerName)
            return OnTransferReceived?.Invoke(collabId, fromAgent, customerName) ?? Task.CompletedTask;
        });

        _hub.On<object>("InternalDirectChatRequest", raw =>
        {
            var j            = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
            var collabId     = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
            var senderName   = j.TryGetProperty("senderName",      out var p2) ? p2.GetString() ?? "" : "";
            var senderType   = j.TryGetProperty("senderUserType",  out var p3) ? p3.GetString() ?? "" : "";
            return OnInternalDirectChatReceived?.Invoke(collabId, senderName, senderType) ?? Task.CompletedTask;
        });

        _hub.On<object>("InternalChannelsUpdated", raw =>
        {
            try
            {
                var channels = System.Text.Json.JsonSerializer.Deserialize<List<ActiveChannelInfo>>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts) ?? [];
                return OnInternalChannelsUpdated?.Invoke(channels) ?? Task.CompletedTask;
            }
            catch { return Task.CompletedTask; }
        });

        _hub.On<object>("OnlineUsersUpdated", raw =>
        {
            try
            {
                var users = System.Text.Json.JsonSerializer.Deserialize<List<OnlineUserInfo>>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts) ?? [];
                return OnOnlineUsersUpdated?.Invoke(users) ?? Task.CompletedTask;
            }
            catch { return Task.CompletedTask; }
        });

        _hub.On<object>("BotMessageReceived", raw =>
        {
            try
            {
                var msg = System.Text.Json.JsonSerializer.Deserialize<ChatMessage>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
                if (msg is not null)
                    return OnBotMessageReceived?.Invoke(msg) ?? Task.CompletedTask;
            }
            catch { }
            return Task.CompletedTask;
        });

        _hub.On<string>("BotSuggestsEscalation", collabId =>
            OnBotSuggestsEscalation?.Invoke(collabId) ?? Task.CompletedTask);

        // ── Bot reply (direct, caller-only response to AskBot hub method) ──────
        // sessionId = the ID passed to AskBotAsync; reply = bot text (empty when UseRealBot=false).
        // ExternalChat subscribes OnBotMessageReceived and falls back to local mock when reply=="".
        _hub.On<string, string>("BotReply", (sessionId, reply) =>
        {
            var msg = new ChatMessage
            {
                CollaborationId = sessionId,
                SenderId        = "nass-bot",
                SenderName      = "Nass",
                Content         = reply,
                IsBot           = true,
                SentAt          = DateTime.UtcNow
            };
            return OnBotMessageReceived?.Invoke(msg) ?? Task.CompletedTask;
        });

        // ── Standalone session events ──────────────────────────────────────────
        _hub.On<object>("StandaloneSessionStarted", raw =>
        {
            try
            {
                var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
                var info = new StandaloneSessionInfo
                {
                    CollaborationId = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "",
                    UserName        = j.TryGetProperty("userName",        out var p2) ? p2.GetString() ?? "" : "",
                    UserId          = j.TryGetProperty("userId",          out var p3) ? p3.GetString() ?? "" : "",
                    StartedAt       = j.TryGetProperty("startedAt",       out var p4) && p4.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow
                };
                return OnStandaloneSessionStarted?.Invoke(info) ?? Task.CompletedTask;
            }
            catch { return Task.CompletedTask; }
        });

        _hub.On<object>("StandaloneSessionsList", raw =>
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<StandaloneSessionInfo>>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts) ?? [];
                return OnStandaloneSessionsList?.Invoke(list) ?? Task.CompletedTask;
            }
            catch { return Task.CompletedTask; }
        });

        _hub.On<object>("ChatHistory", raw =>
        {
            try
            {
                var j = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
                    System.Text.Json.JsonSerializer.Serialize(raw), JsonOpts);
                var collabId = j.TryGetProperty("collaborationId", out var p1) ? p1.GetString() ?? "" : "";
                var msgs     = j.TryGetProperty("messages", out var p2)
                    ? System.Text.Json.JsonSerializer.Deserialize<List<CollaborationMessageDto>>(
                          p2.GetRawText(), JsonOpts) ?? []
                    : new List<CollaborationMessageDto>();
                return OnChatHistory?.Invoke(collabId, msgs) ?? Task.CompletedTask;
            }
            catch { return Task.CompletedTask; }
        });
    }

}
