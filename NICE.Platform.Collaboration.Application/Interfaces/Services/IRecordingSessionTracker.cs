namespace NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// In-memory registry that maps a recordingId → the SignalR connection ID of
/// the agent who started that recording.  Used to route supervisor whispers
/// directly to the correct agent connection in RecordingHub.
/// Registered as Singleton — lives for the lifetime of the server process.
/// </summary>
public interface IRecordingSessionTracker
{
    void Track(Guid recordingId, string agentConnectionId, string agentDisplayName,
               Guid agentUserId, Guid applicationId);

    /// <summary>Track with the known CollaborationId (RecordingHub overload).</summary>
    void Track(Guid recordingId, string agentConnectionId, string agentDisplayName,
               Guid agentUserId, Guid applicationId, Guid collaborationId);

    bool TryGetAgent(Guid recordingId, out string connectionId, out string displayName,
                     out Guid agentUserId, out Guid applicationId);

    /// <summary>Increment the byte counter for an active recording (called per chunk).</summary>
    void AddBytes(Guid recordingId, long bytes);

    void Untrack(Guid recordingId);

    IReadOnlyList<ActiveRecordingInfo> GetByApplication(Guid applicationId);
}

public sealed record ActiveRecordingInfo(
    Guid   RecordingId,
    Guid   CollaborationId,
    Guid   AgentUserId,
    string AgentDisplayName,
    Guid   ApplicationId,
    DateTime StartedAt,
    long   BytesWritten)
{
    // Mutable so the hub can update byte count on each chunk
    public long BytesWritten { get; set; } = BytesWritten;
}
