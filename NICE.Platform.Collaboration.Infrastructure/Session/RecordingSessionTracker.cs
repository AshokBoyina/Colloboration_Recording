namespace NICE.Platform.Collaboration.Infrastructure.Session;

using System.Collections.Concurrent;
using NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Singleton in-memory map:  recordingId → agent connection info.
/// Used by RecordingHub to route supervisor whispers to the correct agent.
/// Data is lost on server restart — agents whose hub re-connects must
/// call StartRecording again to re-register.
/// </summary>
public sealed class RecordingSessionTracker : IRecordingSessionTracker
{
    private readonly ConcurrentDictionary<Guid, Entry> _map = new();

    private sealed record Entry(
        string   ConnectionId,
        string   DisplayName,
        Guid     AgentUserId,
        Guid     ApplicationId,
        Guid     CollaborationId,
        DateTime StartedAt)
    {
        public long BytesWritten { get; set; }
    }

    public void Track(
        Guid   recordingId,
        string agentConnectionId,
        string agentDisplayName,
        Guid   agentUserId,
        Guid   applicationId)
    {
        _map[recordingId] = new Entry(
            agentConnectionId, agentDisplayName, agentUserId,
            applicationId, Guid.Empty, DateTime.UtcNow);
    }

    // Overload used by RecordingHub which also knows the CollaborationId
    public void Track(
        Guid     recordingId,
        string   agentConnectionId,
        string   agentDisplayName,
        Guid     agentUserId,
        Guid     applicationId,
        Guid     collaborationId)
    {
        _map[recordingId] = new Entry(
            agentConnectionId, agentDisplayName, agentUserId,
            applicationId, collaborationId, DateTime.UtcNow);
    }

    public bool TryGetAgent(
        Guid       recordingId,
        out string connectionId,
        out string displayName,
        out Guid   agentUserId,
        out Guid   applicationId)
    {
        if (_map.TryGetValue(recordingId, out var e))
        {
            connectionId = e.ConnectionId;
            displayName  = e.DisplayName;
            agentUserId  = e.AgentUserId;
            applicationId = e.ApplicationId;
            return true;
        }
        connectionId = string.Empty;
        displayName  = string.Empty;
        agentUserId  = Guid.Empty;
        applicationId = Guid.Empty;
        return false;
    }

    public void AddBytes(Guid recordingId, long bytes)
    {
        if (_map.TryGetValue(recordingId, out var e))
            e.BytesWritten += bytes;
    }

    public void Untrack(Guid recordingId) => _map.TryRemove(recordingId, out _);

    public IReadOnlyList<ActiveRecordingInfo> GetByApplication(Guid applicationId)
        => _map
            .Where(kv => kv.Value.ApplicationId == applicationId)
            .Select(kv => new ActiveRecordingInfo(
                kv.Key,
                kv.Value.CollaborationId,
                kv.Value.AgentUserId,
                kv.Value.DisplayName,
                kv.Value.ApplicationId,
                kv.Value.StartedAt,
                kv.Value.BytesWritten))
            .ToList();
}
