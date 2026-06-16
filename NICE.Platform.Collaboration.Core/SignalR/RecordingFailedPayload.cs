namespace NICE.Platform.Collaboration.Core.SignalR;
public class RecordingFailedPayload
{
    public Guid CollaborationId { get; set; }
    public string Reason { get; set; } = default!;
}
