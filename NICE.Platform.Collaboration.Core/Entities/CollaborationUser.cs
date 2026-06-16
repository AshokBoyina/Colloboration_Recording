namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>Table: CollaborationUsers</summary>
public class CollaborationUser
{
    public Guid    Id             { get; set; }

    /// <summary>The ID as provided by the external auth provider (READI / NICE / SurveyId).</summary>
    public string  ExternalUserId { get; set; } = default!;

    public string  FirstName      { get; set; } = default!;
    public string  LastName       { get; set; } = default!;
    public string? Email          { get; set; }
    public bool    IsActive       { get; set; } = true;
    public DateTime CreatedAt     { get; set; }

    // Navigation
    public ICollection<CollaborationApplicationUser> ApplicationUsers { get; set; } = [];
}
