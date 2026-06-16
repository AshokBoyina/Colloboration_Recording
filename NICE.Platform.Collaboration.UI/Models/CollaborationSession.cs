namespace NICE.Platform.Collaboration.UI.Models;

public class CollaborationSession
{
    public string  CollaborationId { get; set; } = string.Empty;
    public string  Status          { get; set; } = string.Empty;
    public string  ExternalUserId  { get; set; } = string.Empty;
    public string? AgentId         { get; set; }
    public string  Channel         { get; set; } = string.Empty;
    public DateTime CreatedAt      { get; set; }
    public int     MessageCount    { get; set; }
}
