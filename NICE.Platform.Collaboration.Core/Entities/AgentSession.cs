namespace NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Enums;
public class AgentSession
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string ConnectionId { get; private set; } = default!;
    public AgentStatus Status { get; private set; }
    public int ActiveCollaborations { get; private set; }
    public DateTime ConnectedAt { get; private set; }
    public DateTime LastHeartbeat { get; private set; }
}
