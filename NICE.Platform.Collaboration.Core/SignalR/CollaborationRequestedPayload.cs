namespace NICE.Platform.Collaboration.Core.SignalR;
public class CollaborationRequestedPayload
{
    public Guid CollaborationId { get; set; }
    public string UserName { get; set; } = default!;
    public Guid ApplicationId { get; set; }
}
