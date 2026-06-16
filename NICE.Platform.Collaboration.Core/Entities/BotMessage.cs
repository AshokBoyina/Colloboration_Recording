namespace NICE.Platform.Collaboration.Core.Entities;
public class BotMessage
{
    public Guid Id { get; private set; }
    public Guid? CollaborationId { get; private set; }
    public string SessionId { get; private set; } = default!;
    public string Role { get; private set; } = default!;   // "user" | "bot"
    public string Content { get; private set; } = default!;
    public bool UsedForTraining { get; private set; }
    public DateTime SentAt { get; private set; }
}
