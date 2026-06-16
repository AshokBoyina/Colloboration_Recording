namespace NICE.Platform.Collaboration.API.Hubs;

using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.EndCollaboration;
using NICE.Platform.Collaboration.Application.Features.Collaborations.Commands.StartCollaboration;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Constants;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Standalone Recording edition of CollaborationHub.
///
/// Retained methods: session connect/disconnect, heartbeat, group join/leave,
/// WebRTC screen-share offer/answer/stop, and all Standalone-specific methods
/// (StartStandaloneSession, JoinStandaloneSession, GetStandaloneSessions, EndCollaboration).
///
/// Excluded (full Collaboration Engine only): RequestCollaboration, AcceptCollaboration,
/// TransferCollaboration, InviteSupervisor, SupervisorJoin, SendMessage, SendWhisper,
/// AskBot, BroadcastInternalChannels.
/// </summary>
[Authorize]
public sealed class CollaborationHub(
    ISender                   sender,
    CollaborationDbContext     db,
    IIceServerProvider        iceProvider,
    ILogger<CollaborationHub> logger) : Hub
{
    // ── Claim helpers ───────────────────────────────────────────────────────
    private Guid   CurrentUserId        => ParseGuid(Claim(ClaimTypes.NameIdentifier) ?? Claim("sub"));
    private Guid   CurrentApplicationId => ParseGuid(Claim("app"));
    private Guid   CurrentSessionId     => ParseGuid(Claim("sid"));
    private string CurrentUserType      => Claim(ClaimTypes.Role) ?? Claim("role") ?? "External";
    private string CurrentAuthProvider  => Claim("provider") ?? "UNKNOWN";
    private string CurrentFirstName     => Claim(ClaimTypes.GivenName)  ?? Claim("given_name")   ?? "";
    private string CurrentLastName      => Claim(ClaimTypes.Surname)    ?? Claim("family_name")   ?? "";
    private string CurrentDisplayName   => $"{CurrentFirstName} {CurrentLastName}".Trim();

    private string? Claim(string type) => Context.User?.FindFirstValue(type);

    private static Guid ParseGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : Guid.Empty;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var connId = Context.ConnectionId;
        var userId = CurrentUserId;
        var appId  = CurrentApplicationId;
        var sessId = CurrentSessionId;
        var now    = DateTime.UtcNow;

        logger.LogInformation(
            "Hub connected: user={UserId} app={AppId} type={UserType} conn={ConnId}",
            userId, appId, CurrentUserType, connId);

        if (userId == Guid.Empty || appId == Guid.Empty)
        {
            logger.LogWarning(
                "Rejecting hub connection — missing JWT claims (userId={UserId} appId={AppId}). " +
                "Call POST /api/v1/demo/mint-token?name=...&role=StandAlone to get a valid token. conn={ConnId}",
                userId, appId, connId);
            await Clients.Caller.SendAsync("ForceDisconnect",
                "Invalid token: missing 'sub' (user GUID) or 'app' (application GUID) claim. " +
                "Use POST /api/v1/demo/mint-token to mint a valid token.");
            Context.Abort();
            return;
        }

        // ── All DB work below is wrapped in a single try/catch ───────────────────
        // FindAsync at line 79 (and the Application lookup below) were previously
        // OUTSIDE any try/catch. A SQL exception (table missing, DB unreachable)
        // would propagate uncaught to the SignalR dispatcher and kill the connection
        // without sending a ForceDisconnect. Now every DB access in OnConnectedAsync
        // is guarded and delivers a human-readable error to the client.
        try
        {
        // ── GetOrCreate user ────────────────────────────────────────────────────
        // mint-token auto-provisions the user in DB, but we guard here in case
        // migrations haven't run yet or a manually crafted token is used.
        var dbUser = await db.Users.FindAsync(new object[] { userId });
        if (dbUser is null)
        {
            dbUser = new CollaborationUser
            {
                Id             = userId,
                ExternalUserId = userId.ToString(),
                FirstName      = string.IsNullOrWhiteSpace(CurrentFirstName) ? "Unknown" : CurrentFirstName,
                LastName       = string.IsNullOrWhiteSpace(CurrentLastName)  ? "User"    : CurrentLastName,
                IsActive       = true,
                CreatedAt      = now
            };
            await db.Users.AddAsync(dbUser);
            await db.SaveChangesAsync();
            logger.LogInformation("Hub: auto-created user {UserId} ({Name})", userId, CurrentDisplayName);
        }

        // ── Verify application exists ─────────────────────────────────────────
        var dbApp = await db.Applications.FindAsync(new object[] { appId });
        if (dbApp is null)
        {
            logger.LogWarning(
                "Hub: applicationId {AppId} not found in DB. Call POST /api/v1/demo/seed or mint-token first. conn={ConnId}",
                appId, connId);
            await Clients.Caller.SendAsync("ForceDisconnect",
                $"Application {appId} not configured. Call POST /api/v1/demo/seed to set up demo apps.");
            Context.Abort();
            return;
        }

        var sessionId = sessId == Guid.Empty ? Guid.NewGuid() : sessId;
        var stale = await db.CurrentSessions.FindAsync(new object[] { sessionId });
        if (stale is not null) db.CurrentSessions.Remove(stale);

        // Single-session enforcement — evict duplicate connections
        var duplicateSessions = await db.CurrentSessions
            .Where(s => s.UserId == userId && s.SignalRConnectionId != connId)
            .ToListAsync();

        foreach (var dup in duplicateSessions)
        {
            try
            {
                await Clients.Client(dup.SignalRConnectionId!)
                    .SendAsync("ForceDisconnect", "You have been signed in from another device.");
            }
            catch { /* connection may already be gone */ }
            db.CurrentSessions.Remove(dup);
        }
        if (duplicateSessions.Count > 0) await db.SaveChangesAsync();

        var current = new CollaborationCurrentSession
        {
            Id                  = sessionId,
            ApplicationId       = appId,
            UserId              = userId,
            UserType            = CurrentUserType,
            AuthProvider        = CurrentAuthProvider,
            SignalRConnectionId = connId,
            ConnectedAt         = now,
            LastSeenAt          = now
        };
        await db.CurrentSessions.AddAsync(current);

        var history = new CollaborationUserSession
        {
            Id            = Guid.NewGuid(),
            ApplicationId = appId,
            UserId        = userId,
            UserType      = CurrentUserType,
            AuthProvider  = CurrentAuthProvider,
            ConnectedAt   = now
        };
        await db.UserSessions.AddAsync(history);
        await db.SaveChangesAsync();

        } // end try
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Hub OnConnectedAsync DB error: user={UserId} app={AppId} conn={ConnId}. " +
                "Ensure EF Core migrations have been applied (dotnet ef database update).",
                userId, appId, connId);
            await Clients.Caller.SendAsync("ForceDisconnect",
                $"Database error during connect ({ex.GetType().Name}): {ex.Message} " +
                "-- Run 'dotnet ef database update' in the API project and restart.");
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(connId, SignalRGroups.Application(appId));
        if (CurrentUserType is "StandaloneMonitor")
            await Groups.AddToGroupAsync(connId, SignalRGroups.StandaloneMonitor(appId));

        // Push ICE config so client can initialise WebRTC immediately
        var iceConfig = await iceProvider.GetConfigAsync();
        await Clients.Caller.SendAsync("IceServersReady", iceConfig);

        await BroadcastOnlineUsersAsync(appId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var connId = Context.ConnectionId;
        var now    = DateTime.UtcNow;

        logger.LogInformation("Hub disconnected: conn={ConnId} reason={Reason}",
            connId, exception?.Message ?? "clean");

        var current = await db.CurrentSessions
            .FirstOrDefaultAsync(s => s.SignalRConnectionId == connId);

        if (current is not null)
        {
            // Stamp participant if mid-standalone-session
            if (current.CurrentCollaborationId.HasValue)
            {
                var participant = await db.Participants.FirstOrDefaultAsync(
                    p => p.CollaborationId == current.CurrentCollaborationId.Value
                      && p.UserId          == current.UserId
                      && p.LeftAt          == null);
                if (participant is not null)
                {
                    participant.LeftAt = now;
                    db.Participants.Update(participant);
                }

                // Standalone user disconnected — end the session and notify monitors
                if (current.UserType is "Standalone" or "StandAlone")
                {
                    var collab = await db.Collaborations
                        .FindAsync(current.CurrentCollaborationId.Value);
                    if (collab is not null && collab.Status != "Ended")
                    {
                        collab.Status    = "Ended";
                        collab.EndedAt   = now;
                        collab.EndReason = "Standalone user disconnected";
                        db.Collaborations.Update(collab);

                        var endPayload = new
                        {
                            id        = collab.Id.ToString(),
                            status    = "Ended",
                            endReason = collab.EndReason
                        };
                        await Clients
                            .Group(SignalRGroups.StandaloneMonitor(current.ApplicationId))
                            .SendAsync("CollaborationEnded", endPayload);
                        await Clients
                            .Group(SignalRGroups.Collaboration(collab.Id))
                            .SendAsync("CollaborationEnded", endPayload);
                    }
                }
            }

            db.CurrentSessions.Remove(current);

            var history = await db.UserSessions
                .Where(s => s.UserId == current.UserId
                         && s.EndedAt == null
                         && s.ConnectedAt >= current.ConnectedAt.AddSeconds(-5))
                .OrderByDescending(s => s.ConnectedAt)
                .FirstOrDefaultAsync();
            if (history is not null)
            {
                history.EndedAt         = now;
                history.DurationSeconds = (int)(now - history.ConnectedAt).TotalSeconds;
                history.EndReason       = exception is null ? "Disconnected" : "Error";
                db.UserSessions.Update(history);
            }

            await db.SaveChangesAsync();
            await BroadcastOnlineUsersAsync(current.ApplicationId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Heartbeat ───────────────────────────────────────────────────────────

    /// <summary>Clients call every 30 s to refresh LastSeenAt (presence detection).</summary>
    public async Task Heartbeat()
    {
        var current = await db.CurrentSessions
            .FirstOrDefaultAsync(s => s.SignalRConnectionId == Context.ConnectionId);
        if (current is null) return;
        current.LastSeenAt = DateTime.UtcNow;
        db.CurrentSessions.Update(current);
        await db.SaveChangesAsync();
    }

    // ── Group management ───────────────────────────────────────────────────

    public async Task JoinCollaborationGroup(string collaborationId)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.Collaboration(collabGuid));
    }

    public async Task LeaveCollaborationGroup(string collaborationId)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, SignalRGroups.Collaboration(collabGuid));
    }

    /// <summary>StandaloneMonitor joins silently to observe without presence noise.</summary>
    public async Task JoinSilently(string collaborationId)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Groups.AddToGroupAsync(
            Context.ConnectionId, SignalRGroups.SilentMonitor(collabGuid));
    }

    // ── WebRTC screen-share signaling ─────────────────────────────────────

    /// <summary>Standalone user broadcasts SDP offer to their collab group.</summary>
    public async Task ShareScreenOffer(string collaborationId, string sdp)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Clients
            .GroupExcept(SignalRGroups.Collaboration(collabGuid), Context.ConnectionId)
            .SendAsync("Offer", new { collaborationId = collabGuid.ToString(), sdp });
        logger.LogDebug("ShareScreenOffer collab={CollabId}", collabGuid);
    }

    /// <summary>StandaloneMonitor returns SDP answer to the standalone user.</summary>
    public async Task ShareScreenAnswer(string collaborationId, string sdp)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Clients
            .GroupExcept(SignalRGroups.Collaboration(collabGuid), Context.ConnectionId)
            .SendAsync("Answer", new { collaborationId = collabGuid.ToString(), sdp });
    }

    /// <summary>Monitor requests a fresh offer from the current standalone user.</summary>
    public async Task RequestScreenOffer(string collaborationId)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Clients
            .GroupExcept(SignalRGroups.Collaboration(collabGuid), Context.ConnectionId)
            .SendAsync("ScreenOfferRequested", collabGuid.ToString());
    }

    /// <summary>Standalone user signals that screen-share has stopped.</summary>
    public async Task StopScreenShare(string collaborationId)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Clients
            .Group(SignalRGroups.Collaboration(collabGuid))
            .SendAsync("ScreenShareStopped", new { collaborationId = collabGuid.ToString() });
    }

    // ── Standalone session management ─────────────────────────────────────

    /// <summary>
    /// Called by a Standalone user on login. Creates a Collaboration record,
    /// joins the collab group, then broadcasts the new session to all monitors.
    /// </summary>
    /// <param name="collaborationId">
    /// Optional GUID string supplied by the host application (e.g. Readi's internal session/ticket ID).
    /// When provided, the server uses this as the Collaboration ID instead of generating a new one.
    /// The server rejects duplicate IDs with an "Error" event.
    /// Omit (or pass null / empty) to let the server generate the ID automatically.
    /// </param>
    public async Task StartStandaloneSession(string? collaborationId = null)
    {
        var userId = CurrentUserId;
        var appId  = CurrentApplicationId;

        // Parse the optional host-supplied ID; silently ignore invalid GUIDs
        Guid? desiredId = null;
        if (!string.IsNullOrWhiteSpace(collaborationId) &&
            Guid.TryParse(collaborationId, out var parsedId))
            desiredId = parsedId;

        Guid collabGuid;
        try
        {
            var result = await sender.Send(new StartCollaborationCommand(userId, null, appId, desiredId));
            collabGuid = result.Id;
        }
        catch (InvalidOperationException ex)
        {
            // Duplicate ID — tell the caller immediately
            logger.LogWarning("StartStandaloneSession: {Error}", ex.Message);
            await Clients.Caller.SendAsync("Error", ex.Message);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.Collaboration(collabGuid));

        var session = await db.CurrentSessions
            .FirstOrDefaultAsync(s => s.SignalRConnectionId == Context.ConnectionId);
        if (session is not null)
        {
            session.CurrentCollaborationId = collabGuid;
            db.CurrentSessions.Update(session);
            await db.SaveChangesAsync();
        }

        var displayName = CurrentDisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (dbUser != null)
                displayName = $"{dbUser.FirstName} {dbUser.LastName}".Trim();
        }
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = $"Standalone {userId.ToString()[..4].ToUpper()}";

        var sessionInfo = new
        {
            collaborationId = collabGuid.ToString(),
            userName        = displayName,
            userId          = userId.ToString(),
            startedAt       = DateTime.UtcNow,
            isStreaming     = false
        };

        await Clients.Caller.SendAsync("StandaloneSessionStarted", sessionInfo);
        await Clients
            .Group(SignalRGroups.StandaloneMonitor(appId))
            .SendAsync("StandaloneSessionStarted", sessionInfo);

        logger.LogInformation(
            "Standalone session started: collab={CollabId} user={UserId} name={Name}",
            collabGuid, userId, displayName);
    }

    /// <summary>
    /// StandaloneMonitor joins a specific session. Adds them to the collab group
    /// then triggers a re-offer from the standalone user.
    /// </summary>
    public async Task JoinStandaloneSession(string collaborationId)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.Collaboration(collabGuid));
        await Clients
            .GroupExcept(SignalRGroups.Collaboration(collabGuid), Context.ConnectionId)
            .SendAsync("ScreenOfferRequested", collabGuid.ToString());
        logger.LogInformation("StandaloneMonitor {UserId} joined collab {CollabId}",
            CurrentUserId, collabGuid);
    }

    /// <summary>
    /// Returns active Standalone sessions in this application to the calling monitor.
    /// </summary>
    public async Task GetStandaloneSessions()
    {
        var appId = CurrentApplicationId;

        // Materialise Guids first, then project with C# ToString() (always lowercase)
        // to avoid EF SQL CAST producing UPPERCASE UUIDs that don't match client-side comparisons.
        var rows = await db.CurrentSessions
            .AsNoTracking()
            .Where(s => s.ApplicationId == appId
                     && (s.UserType == "Standalone" || s.UserType == "StandAlone")
                     && s.CurrentCollaborationId != null)
            .Join(db.Users,
                  s => s.UserId,
                  u => u.Id,
                  (s, u) => new
                  {
                      CollabId    = s.CurrentCollaborationId,
                      UserName    = (u.FirstName + " " + u.LastName).Trim(),
                      UserId      = s.UserId,
                      ConnectedAt = s.ConnectedAt
                  })
            .ToListAsync();

        var sessions = rows.Select(r => new
        {
            collaborationId = r.CollabId!.Value.ToString(),
            userName        = r.UserName,
            userId          = r.UserId.ToString(),
            startedAt       = r.ConnectedAt,
            isStreaming     = true
        });

        await Clients.Caller.SendAsync("StandaloneSessionsList", sessions);
    }

    /// <summary>Ends a standalone collaboration and notifies all monitors.</summary>
    public async Task EndCollaboration(string collaborationId, string? reason)
    {
        if (!Guid.TryParse(collaborationId, out var collabGuid)) return;
        await sender.Send(
            new EndCollaborationCommand(collabGuid, CurrentUserId, reason ?? "Completed"));

        var payload = new { id = collabGuid.ToString(), status = "Ended", endReason = reason };
        await Clients
            .Group(SignalRGroups.Collaboration(collabGuid))
            .SendAsync("CollaborationEnded", payload);
        await Clients
            .Group(SignalRGroups.StandaloneMonitor(CurrentApplicationId))
            .SendAsync("CollaborationEnded", payload);
    }

    // ── Online presence ──────────────────────────────────────────────────────

    private async Task BroadcastOnlineUsersAsync(Guid appId)
    {
        var online = await db.CurrentSessions
            .AsNoTracking()
            .Where(s => s.ApplicationId == appId)
            .Join(db.Users,
                  s => s.UserId,
                  u => u.Id,
                  (s, u) => new
                  {
                      UserId      = s.UserId.ToString(),
                      DisplayName = (u.FirstName + " " + u.LastName).Trim(),
                      UserType    = s.UserType,
                      ConnectedAt = s.ConnectedAt
                  })
            .ToListAsync();

        await Clients
            .Group(SignalRGroups.Application(appId))
            .SendAsync("OnlineUsersUpdated", online);
    }
}
