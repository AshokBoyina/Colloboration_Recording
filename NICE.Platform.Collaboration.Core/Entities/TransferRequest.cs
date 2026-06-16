namespace NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Enums;
public class TransferRequest
{
    public Guid Id { get; private set; }
    public Guid CollaborationId { get; private set; }
    public Guid FromAgentId { get; private set; }
    public Guid ToAgentId { get; private set; }
    public string? Reason { get; private set; }
    public TransferStatus Status { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? RespondedAt { get; private set; }
}
