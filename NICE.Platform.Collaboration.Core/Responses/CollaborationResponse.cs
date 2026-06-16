namespace NICE.Platform.Collaboration.Core.Responses;
public class CollaborationResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = default!;
    public string Type { get; set; } = default!;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>Display name of the External (customer) participant. Empty string if no customer participant recorded yet.</summary>
    public string CustomerName { get; set; } = "";
    /// <summary>Display name of the Agent (non-External) participant handling this session. Empty string if not yet assigned.</summary>
    public string AgentName    { get; set; } = "";
}
