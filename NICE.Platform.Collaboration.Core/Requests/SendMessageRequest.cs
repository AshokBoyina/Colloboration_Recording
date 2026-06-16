namespace NICE.Platform.Collaboration.Core.Requests;
public class SendMessageRequest
{
    public Guid CollaborationId { get; set; }
    public Guid SenderId { get; set; }
    public string Content { get; set; } = default!;
    public Guid? ReplyToId { get; set; }
}
