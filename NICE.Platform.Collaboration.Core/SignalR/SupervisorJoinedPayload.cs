namespace NICE.Platform.Collaboration.Core.SignalR;
public class SupervisorJoinedPayload
{
    public Guid CollaborationId { get; set; }
    public Guid SupervisorId { get; set; }
    public string SupervisorName { get; set; } = default!;
    public bool IsSilent { get; set; }
}
