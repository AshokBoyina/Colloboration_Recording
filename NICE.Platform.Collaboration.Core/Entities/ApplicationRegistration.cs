namespace NICE.Platform.Collaboration.Core.Entities;
public class ApplicationRegistration
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string HashedApiKey { get; private set; } = default!;
    public int MaxAgentsOnline { get; private set; }
    public int MaxUsersOnline { get; private set; }
    public int MaxCollaborationsPerAgent { get; private set; }
    public string? WebhookUrl { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
