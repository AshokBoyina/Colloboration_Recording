namespace NICE.Platform.Collaboration.Core.Requests;
public class RegisterApplicationRequest
{
    public string Name { get; set; } = default!;
    public int MaxAgentsOnline { get; set; }
    public int MaxUsersOnline { get; set; }
    public int MaxCollaborationsPerAgent { get; set; }
    public string? WebhookUrl { get; set; }
}
