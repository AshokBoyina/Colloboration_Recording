namespace NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Streaming append store for live recordings.
/// Chunks arrive in real-time from the agent's MediaRecorder and must be
/// appended incrementally — unlike IBlobStorageService which writes a
/// completed file in one shot.
/// </summary>
public interface IRecordingStreamStore
{
    /// <summary>
    /// Called once when recording starts. Creates the local file (and Azure
    /// append blob) so that subsequent AppendChunkAsync calls have a target.
    /// Returns the storage path / blob URI that will be written to
    /// CollaborationRecording.BlobUri when the recording is finalised.
    /// </summary>
    Task<string> InitAsync(Guid recordingId, CancellationToken ct = default);

    /// <summary>
    /// Appends a raw media chunk. Called for every chunk POSTed by the agent.
    /// </summary>
    Task AppendChunkAsync(Guid recordingId, byte[] chunk, CancellationToken ct = default);

    /// <summary>
    /// Called when the agent stops recording. Flushes and closes the stream.
    /// Returns the final file-size in bytes.
    /// </summary>
    Task<long> FinalizeAsync(Guid recordingId, CancellationToken ct = default);

    /// <summary>
    /// Returns the full local filesystem path of the recording so it can
    /// be range-streamed by the HTTP endpoint (supervisor live view).
    /// Returns null when the store is Azure-only (no local file).
    /// </summary>
    string? GetLocalPath(Guid recordingId);
}
