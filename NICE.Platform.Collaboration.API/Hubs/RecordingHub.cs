namespace NICE.Platform.Collaboration.API.Hubs;

using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartStandAloneRecording;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StopRecording;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Constants;
using NICE.Platform.Collaboration.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

/// <summary>
/// Hub for agent screen recording (StandAlone mode) and supervisor live monitoring.
///
/// Agent flow:
///   1. Agent connects → StartRecording(appId) → gets back recordingId
///   2. JS captures screen, POSTs chunks to POST /api/v1/recordings/{id}/chunks
///   3. Agent calls StopRecording(recordingId) when done
///
/// Supervisor flow (UserType = StandAlone):
///   1. JoinStandAlone(appId) → receives RecordingStarted for all live sessions
///   2. WatchRecording(recordingId) → receives RecordingChunk events in real-time
///   3. WhisperToAgent(recordingId, message) → agent receives RecordingWhisper
///   4. StopWatching(recordingId) when done
///
/// Events pushed to clients:
///   RecordingStarted   { RecordingId, CollaborationId, AgentName, StartedAt }
///   RecordingStopped   { RecordingId, DurationSeconds, FileSizeBytes }
///   RecordingChunk     { RecordingId, Sequence, SizeBytes }   (metadata only; bytes via HTTP range)
///   RecordingWhisper   { RecordingId, Message, FromName, SentAt }
///   ForceDisconnect    { Reason }
/// </summary>
[Authorize]
public sealed class RecordingHub(
    ISender                    sender,
    CollaborationDbContext      db,
    IRecordingStreamStore       store,
    IRecordingSessionTracker    tracker,
    ILogger<RecordingHub>       logger) : Hub
{
    // ── Claim helpers ────────────────────────────────────────────────────────
    private Guid   CurrentUserId =>
        Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User?.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    private Guid   CurrentAppId =>
        Guid.TryParse(User?.FindFirstValue("app"), out var id) ? id : Guid.Empty;
    private string CurrentRole        => User?.FindFirstValue(ClaimTypes.Role) ?? User?.FindFirstValue("role") ?? "Agent";
    private string CurrentFirstName   => User?.FindFirstValue(ClaimTypes.GivenName)  ?? User?.FindFirstValue("given_name") ?? "";
    private string CurrentLastName    => User?.FindFirstValue(ClaimTypes.Surname)    ?? User?.FindFirstValue("family_name") ?? "";
    private string CurrentDisplayName => $"{CurrentFirstName} {CurrentLastName}".Trim();

    private ClaimsPrincipal? User => Context.User;

    // ── Connection lifecycle ─────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;
        var role   = CurrentRole;
        logger.LogInformation("RecordingHub: {User} ({Role}) connected [{Conn}]",
            CurrentDisplayName, role, Context.ConnectionId);

        // StandAlone supervisors auto-join their application group
        if (role.Equals("StandAlone", StringComparison.OrdinalIgnoreCase))
        {
            var appId = CurrentAppId;
            if (appId != Guid.Empty)
                await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.StandAlone(appId));
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // If an agent disconnects, untrack any recording they had in progress
        // (chunk upload endpoint will fail anyway once hub is gone)
        logger.LogInformation("RecordingHub: [{Conn}] disconnected", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    // ── Supervisor: join/leave StandAlone app group ─────────────────────────

    /// <summary>
    /// Supervisor calls this on page load to receive RecordingStarted broadcasts.
    /// Returns info about all recordings currently live in the application.
    /// </summary>
    public async Task JoinStandAlone(string applicationId)
    {
        if (!Guid.TryParse(applicationId, out var appId))
        {
            logger.LogWarning("JoinStandAlone: invalid applicationId '{Id}' from {Conn}", applicationId, Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", "Invalid applicationId — cannot join standalone group.");
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.StandAlone(appId));

        // Return currently live recordings for this app
        var live = tracker.GetByApplication(appId);
        await Clients.Caller.SendAsync("LiveRecordingSnapshot", live.Select(r => new
        {
            r.RecordingId,
            r.CollaborationId,
            r.AgentUserId,
            r.AgentDisplayName,
            r.StartedAt,
            r.BytesWritten
        }));
    }

    // ── Supervisor: watch / stop watching a specific recording ───────────────

    /// <summary>
    /// Supervisor joins the recording group to receive RecordingChunk notifications.
    /// </summary>
    public Task WatchRecording(string recordingId)
        => Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.Recording(Guid.Parse(recordingId)));

    public Task StopWatching(string recordingId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, SignalRGroups.Recording(Guid.Parse(recordingId)));

    // ── Supervisor: whisper to agent ─────────────────────────────────────────

    /// <summary>
    /// Routes a private whisper message to the agent handling this recording.
    /// The agent receives a RecordingWhisper event; supervisors do NOT see it.
    /// </summary>
    public async Task WhisperToAgent(string recordingId, string message)
    {
        var recId = Guid.Parse(recordingId);
        if (!tracker.TryGetAgent(recId, out var agentConnId, out _, out _, out _))
        {
            await Clients.Caller.SendAsync("Error", "Recording not found or agent disconnected.");
            return;
        }

        await Clients.Client(agentConnId).SendAsync("RecordingWhisper", new
        {
            RecordingId   = recordingId,
            Message       = message,
            FromName      = CurrentDisplayName,
            SentAt        = DateTime.UtcNow
        });
    }

    // ── Agent: start recording ────────────────────────────────────────────────

    /// <summary>
    /// Agent calls this to create a Collaboration + Recording row and start
    /// the recording session.  Returns the recordingId the JS must use when
    /// POSTing chunks.
    /// </summary>
    public async Task StartRecording(string applicationId, string? collaborationId = null)
    {
        if (!Guid.TryParse(applicationId, out var appId))
        {
            await Clients.Caller.SendAsync("Error", "Invalid applicationId.");
            return;
        }

        // Optional caller-supplied collaboration/session id. If absent or unparseable,
        // the server generates one (handled in the command handler).
        Guid? collabId = Guid.TryParse(collaborationId, out var parsedCollab) ? parsedCollab : null;

        var userId  = CurrentUserId;
        var connId  = Context.ConnectionId;

        var result = await sender.Send(
            new StartStandAloneRecordingCommand(userId, appId, collabId));

        // Register in tracker so whispers can reach this connection
        tracker.Track(result.RecordingId, connId, CurrentDisplayName, userId, appId, result.CollaborationId);

        // Join the recording group so chunk broadcast reaches the agent too (optional)
        await Groups.AddToGroupAsync(connId, SignalRGroups.Recording(result.RecordingId));

        // Tell the caller its recording ID
        await Clients.Caller.SendAsync("RecordingReady", new
        {
            RecordingId     = result.RecordingId.ToString(),
            CollaborationId = result.CollaborationId.ToString()
        });

        // Notify all StandAlone supervisors in the application
        await Clients
            .Group(SignalRGroups.StandAlone(appId))
            .SendAsync("RecordingStarted", new
            {
                RecordingId     = result.RecordingId.ToString(),
                CollaborationId = result.CollaborationId.ToString(),
                AgentUserId     = userId.ToString(),
                AgentName       = CurrentDisplayName,
                StartedAt       = DateTime.UtcNow
            });

        logger.LogInformation("Agent {User} started recording {RecId}", CurrentDisplayName, result.RecordingId);
    }

    // ── Agent: stop recording ─────────────────────────────────────────────────

    /// <summary>
    /// Agent calls this after the last chunk has been uploaded.
    /// Finalizes the stream store and updates the DB row.
    /// </summary>
    public async Task StopRecording(string recordingId)
    {
        var recId = Guid.Parse(recordingId);

        // Capture the blob path BEFORE FinalizeAsync removes it from the store's path map.
        // GetLocalPath returns the full OS path (e.g. C:\recordings\2026-06-15\<id>.mp4).
        // We convert it to a relative "recordings/<date>/<id>.mp4" for storage in BlobUri.
        var localPath = store.GetLocalPath(recId);

        // Finalize the stream store (flush + close file)
        var fileSize = await store.FinalizeAsync(recId);

        // Derive the portable blob-path from the local path so BlobUri is always populated.
        string? blobUri = null;
        if (localPath is not null)
        {
            // Blob path: recordings/<date>/<id>.<ext> — keeps RecordingsPath out of the DB.
            var dateFolder  = Path.GetFileName(Path.GetDirectoryName(localPath)) ?? DateTime.Now.ToString("yyyy-MM-dd");
            var ext         = Path.GetExtension(localPath);   // .mp4 or .webm
            blobUri = $"recordings/{dateFolder}/{recId}{ext}";
        }

        // Update DB
        var recording = await db.Recordings.FindAsync(recId);
        if (recording is not null)
        {
            var now                   = DateTime.UtcNow;
            recording.StoppedAt       = now;
            recording.DurationSeconds = (int)(now - recording.StartedAt).TotalSeconds;
            recording.FileSizeBytes   = fileSize;
            recording.Status          = "Ready";
            recording.BlobUri         = blobUri ?? recording.BlobUri;
            db.Recordings.Update(recording);
            await db.SaveChangesAsync();
        }

        // Get appId for broadcast BEFORE untracking
        Guid appId = Guid.Empty;
        tracker.TryGetAgent(recId, out _, out _, out _, out appId);

        // Unregister from tracker
        tracker.Untrack(recId);

        // Notify supervisors
        if (appId != Guid.Empty)
        {
            await Clients
                .Group(SignalRGroups.StandAlone(appId))
                .SendAsync("RecordingStopped", new
                {
                    RecordingId     = recordingId,
                    DurationSeconds = recording?.DurationSeconds ?? 0,
                    FileSizeBytes   = fileSize
                });
        }

        logger.LogInformation("Recording {RecId} stopped ({Bytes} bytes)", recId, fileSize);
    }

    // ── Called by RecordingsController after appending a chunk ───────────────

    /// <summary>
    /// Broadcasts chunk-available metadata to supervisors watching this recording.
    /// Called internally by RecordingsController — not directly from clients.
    /// </summary>
    public Task BroadcastChunkAvailable(Guid recordingId, int sequence, int sizeBytes)
        => Clients
            .Group(SignalRGroups.Recording(recordingId))
            .SendAsync("RecordingChunk", new
            {
                RecordingId = recordingId.ToString(),
                Sequence    = sequence,
                SizeBytes   = sizeBytes
            });
}
