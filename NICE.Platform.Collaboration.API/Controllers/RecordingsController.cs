namespace NICE.Platform.Collaboration.API.Controllers;

using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StartRecording;
using NICE.Platform.Collaboration.Application.Features.Recordings.Commands.StopRecording;
using NICE.Platform.Collaboration.Application.Features.Recordings.Queries.GetRecordingsByCollaboration;
using NICE.Platform.Collaboration.Application.Features.Recordings.Queries.GetRecordingSasUrl;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Requests;
using NICE.Platform.Collaboration.Infrastructure.Persistence;
using NICE.Platform.Collaboration.API.Hubs;

[Authorize]
[ApiController]
[Route("api/v1/collaboration/recordings")]
public class RecordingsController(
    ISender                   sender,
    IRecordingStreamStore      store,
    IRecordingSessionTracker   tracker,
    IHubContext<RecordingHub>  hubContext,
    CollaborationDbContext      db) : ControllerBase
{
    private Guid CallerId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub"), out var id)
            ? id : Guid.Empty;

    // ── Standard recording lifecycle ─────────────────────────────────────────

    /// <summary>Start a new recording for a collaboration.</summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start(
        [FromBody] StartRecordingRequest request, CancellationToken ct)
    {
        var result = await sender.Send(
            new StartRecordingCommand(request.CollaborationId, CallerId, request.Type), ct);
        return Ok(result);
    }

    /// <summary>Stop an active recording and store blob path.</summary>
    [HttpPost("{id:guid}/stop")]
    public async Task<IActionResult> Stop(
        Guid id, [FromQuery] string blobPath, CancellationToken ct)
    {
        var result = await sender.Send(
            new StopRecordingCommand(id, CallerId, blobPath), ct);
        return Ok(result);
    }

    /// <summary>Get all recordings for a collaboration.</summary>
    [HttpGet("{collaborationId:guid}")]
    public async Task<IActionResult> GetByCollaboration(Guid collaborationId, CancellationToken ct)
    {
        var result = await sender.Send(
            new GetRecordingsByCollaborationQuery(collaborationId), ct);
        return Ok(result);
    }

    /// <summary>Get a time-limited SAS URL (or local proxy URL) for streaming a recording.</summary>
    [HttpGet("{id:guid}/sas-url")]
    public async Task<IActionResult> GetSasUrl(Guid id, CancellationToken ct)
    {
        var url = await sender.Send(new GetRecordingSasUrlQuery(id), ct);
        return Ok(new { Url = url });
    }

    // ── StandAlone live recording chunk upload ──────────────────────────────

    /// <summary>
    /// Agent JS posts binary recording chunks here (raw body, not multipart).
    /// The server appends each chunk to local disk + Azure Blob in real-time
    /// and broadcasts a RecordingChunk notification to supervisors watching.
    ///
    /// The body is read straight off <c>Request.Body</c>, so the request
    /// Content-Type is irrelevant to parsing. We deliberately do NOT restrict it
    /// with <c>[Consumes]</c>: the MediaRecorder sends the real media MIME type
    /// (e.g. <c>video/mp4;codecs=avc1,mp4a.40.2</c> or <c>video/webm</c>), and a
    /// <c>[Consumes("application/octet-stream")]</c> filter would reject those
    /// with HTTP 415 before the action runs — silently dropping every chunk and
    /// leaving a 0-byte recording file. The format is conveyed separately via the
    /// optional <c>X-Recording-MimeType</c> header.
    ///
    /// Body: raw binary media chunk (fragmented MP4 or WebM)
    /// Header: X-Chunk-Sequence: {int}        (0-based chunk number from JS)
    /// Header: X-Recording-MimeType: {string} (optional, informational)
    /// </summary>
    [HttpPost("{id:guid}/chunks")]
    [AllowAnonymous]
    [RequestSizeLimit(2 * 1024 * 1024)]   // 2 MB max per chunk
    public async Task<IActionResult> UploadChunk(Guid id, CancellationToken ct)
    {
        var active = await db.Recordings.AsNoTracking()
            .AnyAsync(r => r.Id == id && r.Status == "Recording", ct);
        if (!active)
            return Unauthorized(new { error = "Recording is not active or does not exist." });

        // Read raw binary body
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var chunk = ms.ToArray();

        if (chunk.Length == 0)
            return BadRequest(new { error = "Empty chunk." });

        // Append to storage (local disk + optional Azure blob)
        await store.AppendChunkAsync(id, chunk, ct);

        // Update in-memory byte counter
        tracker.AddBytes(id, chunk.Length);

        // Parse optional sequence number from header
        _ = int.TryParse(Request.Headers["X-Chunk-Sequence"].ToString(), out var seq);

        // Broadcast chunk-available event to supervisors watching this recording
        await hubContext.Clients
            .Group($"recording-{id}")
            .SendAsync("RecordingChunk", new
            {
                RecordingId = id.ToString(),
                Sequence    = seq,
                SizeBytes   = chunk.Length
            });

        return Ok(new
        {
            RecordingId = id,
            Sequence    = seq,
            SizeBytes   = chunk.Length,
            LocalPath   = store.GetLocalPath(id)
        });
    }

    // ── HTTP range-streaming for supervisor live / completed playback ─────────

    /// <summary>
    /// Streams the recording file with HTTP range-request support.
    /// The supervisor's video element sources this URL.
    /// Works for both live recordings (file grows) and completed ones.
    /// </summary>
    [HttpGet("{id:guid}/stream")]
    public async Task<IActionResult> Stream(Guid id, CancellationToken ct)
    {
        var localPath = store.GetLocalPath(id);

        // If not in live store, look up BlobUri in the DB and resolve to a local path.
        if (localPath == null || !System.IO.File.Exists(localPath))
        {
            var rec = await db.Recordings.AsNoTracking()
                               .FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rec?.BlobUri == null) return NotFound();

            // BlobUri is "recordings/<date>/<id>.<ext>" — resolve against the configured root.
            var recordingsRoot = HttpContext.RequestServices
                .GetRequiredService<IConfiguration>()["LocalStorage:RecordingsPath"]
                ?? System.IO.Path.Combine(AppContext.BaseDirectory, "LocalStorage", "Recordings");

            // Strip the leading "recordings/" prefix that BlobUri carries by convention.
            var relativePart = rec.BlobUri.StartsWith("recordings/", StringComparison.OrdinalIgnoreCase)
                ? rec.BlobUri["recordings/".Length..]
                : rec.BlobUri;

            var fallbackPath = System.IO.Path.Combine(recordingsRoot, relativePart);
            if (!System.IO.File.Exists(fallbackPath)) return NotFound();
            localPath = fallbackPath;
        }

        // Derive content-type from the actual file extension so both old .webm
        // files and new .mp4 files are served with the correct MIME type.
        var contentType = System.IO.Path.GetExtension(localPath).ToLowerInvariant() switch
        {
            ".mp4"  => "video/mp4",
            ".webm" => "video/webm",
            _       => "application/octet-stream"
        };

        // Serve with range support so the browser can seek / buffer incrementally.
        var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                                        FileShare.ReadWrite, 65536, true);
        Response.Headers.Append("Accept-Ranges", "bytes");
        return File(fileStream, contentType, enableRangeProcessing: true);
    }

    // ── Browser-side WebRTC recording upload ─────────────────────────────────

    /// <summary>
    /// Receives a completed WebM recording blob from the agent/supervisor browser
    /// and saves it to the configured local recordings path.
    ///
    /// This is the production equivalent of POST /api/v1/demo/recordings/upload.
    /// It requires an authenticated Bearer token (unlike the demo endpoint).
    ///
    /// The JS <c>screenShare.configure()</c> in chat.js passes this URL when
    /// <c>EnableRecording</c> is true. AgentDashboard and SupervisorView both use
    /// this endpoint for browser-captured WebRTC recordings.
    ///
    /// Body: multipart/form-data  { file: WebM blob, collaborationId: string }
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public async Task<IActionResult> UploadRecording(
        IFormFile file,
        [FromForm] string? collaborationId,
        [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file received." });

        var recordingsRoot = config["LocalStorage:RecordingsPath"]
                             ?? Path.Combine(AppContext.BaseDirectory, "Recordings");
        var dateFolder = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var saveDir    = Path.Combine(recordingsRoot, dateFolder);
        Directory.CreateDirectory(saveDir);

        var collabPart = !string.IsNullOrEmpty(collaborationId) && collaborationId.Length >= 8
            ? collaborationId[..8]
            : "rec";
        var timestamp = DateTime.UtcNow.ToString("HHmmss");
        var ext       = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";
        var fileName  = $"recording-{collabPart}-{timestamp}{ext}";
        var fullPath  = Path.Combine(saveDir, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, ct);

        return Ok(new
        {
            fileName,
            path      = fullPath,
            sizeBytes = file.Length,
            savedAt   = DateTime.UtcNow
        });
    }

    // ── List live recordings by application (for supervisor initial load) ────

    /// <summary>
    /// Returns all currently live recording sessions for the calling user's application.
    /// Supervisor uses this on page load to populate the session list without waiting
    /// for real-time events.
    /// </summary>
    [HttpGet("live")]
    public IActionResult GetLive()
    {
        var appIdClaim = User.FindFirstValue("app");
        if (!Guid.TryParse(appIdClaim, out var appId))
            return BadRequest(new { error = "app claim missing from token" });

        var live = tracker.GetByApplication(appId);
        return Ok(live.Select(r => new
        {
            r.RecordingId,
            r.CollaborationId,
            r.AgentUserId,
            r.AgentDisplayName,
            r.StartedAt,
            r.BytesWritten
        }));
    }
}

