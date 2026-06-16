namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>Table: CollaborationApplications</summary>
public class CollaborationApplication
{
    public Guid   Id               { get; set; }
    public string Name             { get; set; } = default!;
    public string HashedApiKey     { get; set; } = default!;

    /// <summary>READI | NICE | ANON — the auth provider configured for this application.</summary>
    public string AuthProvider     { get; set; } = default!;

    public int    MaxAgentsOnline  { get; set; }
    public int    MaxUsersOnline   { get; set; }

    /// <summary>Root path inside Azure Blob Storage for this application.</summary>
    public string BlobContainerPath { get; set; } = default!;

    public string? WebhookUrl      { get; set; }
    public bool   IsActive         { get; set; } = true;
    public DateTime CreatedAt      { get; set; }

    // Navigation
    public ICollection<CollaborationApplicationUserTypeConfig> UserTypeConfigs { get; set; } = [];
    public ICollection<CollaborationApplicationUser>           ApplicationUsers { get; set; } = [];
}
