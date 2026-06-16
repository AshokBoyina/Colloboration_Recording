namespace NICE.Platform.Collaboration.Core.SignalR;
public class RecordingAvailablePayload
{
    public Guid CollaborationId { get; set; }
    public string SasUrl { get; set; } = default!;
    public string Type { get; set; } = default!;
}
