namespace NICE.Platform.Collaboration.Core.SignalR;
public class AgentTransferPayload
{
    public Guid CollaborationId { get; set; }
    public Guid ToAgentId { get; set; }
    public string? Reason { get; set; }
}
