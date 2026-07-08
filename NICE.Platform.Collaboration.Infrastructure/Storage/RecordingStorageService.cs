namespace NICE.Platform.Collaboration.Infrastructure.Storage;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// File-system implementation of <see cref="IBlobStorageService"/>. Recording and
/// attachment files are written to a configurable path on the host — a local folder
/// or a UNC network share (e.g. <c>\\fileserver\share\recordings</c>). Standard .NET
/// file APIs handle UNC paths transparently; the process's account just needs
/// read/write access to the share.
///
/// Config keys (all in <c>appsettings.json</c>):
///   <c>RecordingStorage:RecordingsPath</c>  — root folder for recording files
///   <c>RecordingStorage:AttachmentsPath</c> — root folder for chat attachments
///
/// Both folders are created automatically on first use if they do not exist.
/// The returned "URI" is a relative path stored in <c>CollaborationRecording.BlobUri</c>.
/// The API exposes  GET /api/v1/recordings/{id}/download  which streams the file.
/// </summary>
public class RecordingStorageService(IConfiguration config, ILogger<RecordingStorageService> logger)
    : IBlobStorageService
{
    private string RecordingsPath  => config["RecordingStorage:RecordingsPath"]  ?? Path.Combine(AppContext.BaseDirectory, "RecordingStorage", "Recordings");
    private string AttachmentsPath => config["RecordingStorage:AttachmentsPath"] ?? Path.Combine(AppContext.BaseDirectory, "RecordingStorage", "Attachments");

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task<string> UploadAsync(
        string blobPath, Stream content, string contentType, CancellationToken ct)
    {
        var fullPath = ResolveFullPath(blobPath);
        EnsureDirectory(fullPath);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);

        logger.LogInformation("RecordingStorage: saved {BlobPath} ({Bytes} bytes)", blobPath, file.Length);

        // Return the relative path as the "URI" so it is consistent
        // regardless of where the service runs.
        return blobPath;
    }

    // ── SAS-equivalent: timed download token (file path, no expiry needed) ──

    /// <summary>
    /// There is no SAS concept for a file system — returns a signed API URL that the
    /// download endpoint honours for the given expiry window. The token is a base-64
    /// encoded path + expiry timestamp (not cryptographically signed — add HMAC if you
    /// need tamper-proofing before production).
    /// </summary>
    public Task<string> GenerateSasUrlAsync(string blobPath, TimeSpan expiry, CancellationToken ct)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry).ToUnixTimeSeconds();
        var token     = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{blobPath}|{expiresAt}"));

        // The caller stores this and the client uses it to download via the REST API.
        var url = $"/api/v1/recordings/stream?token={Uri.EscapeDataString(token)}";
        return Task.FromResult(url);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public Task DeleteAsync(string blobPath, CancellationToken ct)
    {
        var fullPath = ResolveFullPath(blobPath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("RecordingStorage: deleted {BlobPath}", blobPath);
        }
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a blob path like "recordings/2024/abc.mp4" to a full file-system path.
    /// Recording blobs start with "recordings/" and live under RecordingsPath; the
    /// "recordings/" prefix is stripped so the path matches where the streaming store
    /// writes files (RecordingsPath/&lt;date&gt;/&lt;id&gt;.mp4) — NOT RecordingsPath/recordings/…
    /// Everything else goes to AttachmentsPath.
    /// </summary>
    private string ResolveFullPath(string blobPath)
    {
        string rootPath;
        string relative;
        if (blobPath.StartsWith("recordings/", StringComparison.OrdinalIgnoreCase))
        {
            rootPath = RecordingsPath;
            relative = blobPath["recordings/".Length..];
        }
        else
        {
            rootPath = AttachmentsPath;
            relative = blobPath;
        }

        // Normalise separators and prevent path-traversal attacks.
        var safeName = Path.GetFullPath(Path.Combine(rootPath, relative));
        if (!safeName.StartsWith(Path.GetFullPath(rootPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path traversal detected in blobPath: {blobPath}");

        return safeName;
    }

    private static void EnsureDirectory(string fullPath)
    {
        var dir = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
