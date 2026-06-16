namespace NICE.Platform.Collaboration.Core.Requests;
public class TransferCollaborationRequest
{
    public Guid CollaborationId { get; set; }
    public Guid ToAgentId { get; set; }
    public string? Reason { get; set; }
}
