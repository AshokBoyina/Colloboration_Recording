namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Table: CollaborationApplicationUsers
/// Junction — a person can have different roles in different applications.
/// </summary>
public class CollaborationApplicationUser
{
    public Guid   ApplicationId { get; set; }
    public Guid   UserId        { get; set; }

    /// <summary>User | Agent | Supervisor | Admin | Bot</summary>
    public string Role          { get; set; } = default!;

    public bool   IsActive      { get; set; } = true;
    public DateTime AddedAt     { get; set; }

    // Navigation
    public CollaborationApplication Application { get; set; } = default!;
    public CollaborationUser         User        { get; set; } = default!;
}
