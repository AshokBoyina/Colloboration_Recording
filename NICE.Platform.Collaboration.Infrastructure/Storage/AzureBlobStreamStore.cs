namespace NICE.Platform.Collaboration.Infrastructure.Storage;

using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Azure Blob implementation of <see cref="IRecordingStreamStore"/>
/// (active when <c>FeatureFlags:UseAzureBlob = true</c>).
///
/// Strategy: record to local disk via a composed <see cref="LocalDiskStreamStore"/>
/// — this reuses the real-time chunk append and, crucially, the pure-C# fMP4
/// de-fragmentation that makes the file play in Windows Media Player. On
/// finalization the de-fragmented, progressive MP4 is uploaded to Azure Blob and
/// the local temp copy is removed, so Azure is the durable store.
///
/// Why not append-blob streaming directly? The de-fragmentation fix-up needs random
/// access to rewrite box offsets, which an append blob does not provide. Recording
/// to a local scratch file and uploading the finished artifact is simpler and keeps
/// the live <c>/stream</c> endpoint working (it reads <see cref="GetLocalPath"/>
/// while recording). Completed recordings are served via a blob SAS URL.
/// </summary>
public sealed class AzureBlobStreamStore : IRecordingStreamStore, IAsyncDisposable
{
    private readonly LocalDiskStreamStore _local;
    private readonly BlobContainerClient  _container;
    private readonly ILogger<AzureBlobStreamStore> _logger;

    // Blob path (e.g. "recordings/2026-06-16/{id}.mp4") captured at InitAsync.
    private readonly ConcurrentDictionary<Guid, string> _blobPaths = new();

    public AzureBlobStreamStore(
        LocalDiskStreamStore local,
        IConfiguration config,
        ILogger<AzureBlobStreamStore> logger)
    {
        _local     = local;
        _logger    = logger;
        _container = AzureBlobSupport.CreateContainerClient(config);
    }

    public async Task<string> InitAsync(Guid recordingId, CancellationToken ct = default)
    {
        // LocalDiskStreamStore creates the scratch file and returns the relative blob path.
        var blobPath = await _local.InitAsync(recordingId, ct);
        _blobPaths[recordingId] = blobPath;
        return blobPath;
    }

    public Task AppendChunkAsync(Guid recordingId, byte[] chunk, CancellationToken ct = default)
        => _local.AppendChunkAsync(recordingId, chunk, ct);

    public async Task<long> FinalizeAsync(Guid recordingId, CancellationToken ct = default)
    {
        // Capture the local path before FinalizeAsync removes it from the path map.
        var localPath = _local.GetLocalPath(recordingId);

        // Finalize locally first — this flushes, closes, and de-fragments the fMP4
        // into a progressive, WMP-compatible MP4.
        var size = await _local.FinalizeAsync(recordingId, ct);

        _blobPaths.TryRemove(recordingId, out var blobPath);

        if (localPath is null || blobPath is null || !File.Exists(localPath))
        {
            _logger.LogWarning(
                "AzureBlobStreamStore: nothing to upload for {Id} (no finalized local file).", recordingId);
            return size;
        }

        try
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blob = _container.GetBlobClient(blobPath);

            await using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read,
                                                 FileShare.Read, 1 << 20, useAsync: true))
            {
                await blob.UploadAsync(fs, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "video/mp4" }
                }, ct);
            }

            _logger.LogInformation(
                "AzureBlobStreamStore: uploaded {BlobPath} ({Bytes} bytes) to Azure Blob.", blobPath, size);

            // Azure is now the durable store — drop the local scratch copy.
            try { File.Delete(localPath); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AzureBlobStreamStore: could not delete temp file {Path}.", localPath);
            }
        }
        catch (Exception ex)
        {
            // Upload failed — KEEP the local file so the recording is not lost.
            _logger.LogError(ex,
                "AzureBlobStreamStore: upload failed for {BlobPath} — keeping local copy at {Path}.",
                blobPath, localPath);
        }

        return size;
    }

    /// <summary>
    /// Returns the local scratch path while recording (used by the live range-stream
    /// endpoint). Null once the recording is finalized and uploaded — completed
    /// recordings are served via a blob SAS URL instead.
    /// </summary>
    public string? GetLocalPath(Guid recordingId) => _local.GetLocalPath(recordingId);

    public async ValueTask DisposeAsync() => await _local.DisposeAsync();
}
