namespace NICE.Platform.Collaboration.UI.Models;

/// <summary>Application as returned by GET /api/v1/demo/apps</summary>
public class DemoApp
{
    public Guid   Id             { get; set; }
    public string Name           { get; set; } = string.Empty;
    public string AuthProvider   { get; set; } = string.Empty;
    public string ApiKey         { get; set; } = string.Empty;
    public bool   IsActive       { get; set; }
    public int    AgentCount     { get; set; }
    public int    SupervisorCount { get; set; }
}

/// <summary>User as returned by GET /api/v1/demo/users/{appId}</summary>
public class DemoUser
{
    public Guid     Id        { get; set; }
    public string   Token     { get; set; } = string.Empty;   // the login token = ExternalUserId
    public string   FirstName { get; set; } = string.Empty;
    public string   LastName  { get; set; } = string.Empty;
    public string?  Email     { get; set; }
    public string   Role      { get; set; } = string.Empty;
    public DateTime AddedAt   { get; set; }

    public string DisplayName => $"{FirstName} {LastName}".Trim();
}

/// <summary>Result of GET /api/v1/demo/status</summary>
public class DemoStatusResult
{
    public bool    Seeded   { get; set; }
    public int     AppCount { get; set; }
    public bool    DbOk     { get; set; } = true;
    public string? DbError  { get; set; }
}

/// <summary>Message returned by GET /api/v1/collaboration/messages/{collabId}</summary>
public class CollaborationMessageDto
{
    public Guid     Id             { get; set; }
    public Guid     SenderId       { get; set; }
    public string   SenderName     { get; set; } = string.Empty;
    /// <summary>"External", "Agent", "Supervisor", or "System"</summary>
    public string   SenderRole     { get; set; } = string.Empty;
    public string   Content        { get; set; } = string.Empty;
    public bool     IsWhisper      { get; set; }
    /// <summary>True for bot/system messages (maps to CollaborationMessage.IsSystemNotice in the API).</summary>
    public bool     IsSystemNotice { get; set; }
    public DateTime SentAt         { get; set; }
}

/// <summary>Active internal channel as returned by GET /api/v1/demo/channels/{appId}</summary>
public class ActiveChannelInfo
{
    public string       ChannelId    { get; set; } = string.Empty;
    public List<string> Participants { get; set; } = [];
}

/// <summary>DTO for creating a demo user via POST /api/v1/demo/users</summary>
public class DemoCreateUserDto
{
    public string  Name          { get; set; } = string.Empty;
    public string  Role          { get; set; } = string.Empty;
    public Guid    ApplicationId { get; set; }
    public string? Token         { get; set; }
    public string? Email         { get; set; }
}

/// <summary>Online user as returned by GET /api/v1/demo/online-users/{appId} or /internal</summary>
public class OnlineUserInfo
{
    public Guid     UserId          { get; set; }
    public string   DisplayName     { get; set; } = string.Empty;
    /// <summary>"Agent", "Supervisor", or "Internal"</summary>
    public string   UserType        { get; set; } = string.Empty;
    public DateTime ConnectedAt     { get; set; }
    /// <summary>Name of the application this user belongs to. Populated by the /internal endpoint.</summary>
    public string   ApplicationName { get; set; } = string.Empty;
}

/// <summary>Active collaboration as returned by GET /api/v1/collaboration/collaborations/active/{appId}</summary>
public class ActiveCollaborationDto
{
    public Guid      Id           { get; set; }
    /// <summary>Status string — e.g. "Pending", "BotHandling", "AgentHandling".</summary>
    public string    Status       { get; set; } = string.Empty;
    public string    Type         { get; set; } = string.Empty;
    public DateTime  StartedAt    { get; set; }
    public DateTime? EndedAt      { get; set; }
    /// <summary>Display name of the customer (External participant). Falls back to "Customer" when not yet set.</summary>
    public string    CustomerName { get; set; } = string.Empty;
    /// <summary>Display name of the agent handling this session. Empty string if not yet assigned.</summary>
    public string    AgentName    { get; set; } = string.Empty;
}
