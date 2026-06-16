namespace NICE.Platform.Collaboration.Core.SignalR;
public class SystemNoticePayload
{
    public Guid CollaborationId { get; set; }
    public string Message { get; set; } = default!;
}
