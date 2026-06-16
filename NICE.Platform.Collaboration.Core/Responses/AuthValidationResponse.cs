namespace NICE.Platform.Collaboration.Core.Responses;

/// <summary>
/// Returned by POST api/v1/collaboration/auth/validate.
///
/// On success:
///   - <see cref="SessionToken"/> is the engine-issued JWT for all subsequent calls.
///   - <see cref="User"/> contains the resolved identity.
///   - <see cref="ApplicationConfig"/> is the configuration slice for the connecting UserType.
///     Only the section relevant to the UserType is populated; all others are null.
///
/// On failure:
///   - <see cref="Error"/> describes why validation was rejected.
/// </summary>
public class AuthValidationResponse
{
    public bool    Success { get; set; }
    public string? Error   { get; set; }

    /// <summary>Engine-issued JWT — present only when <see cref="Success"/> is true.</summary>
    public string? SessionToken { get; set; }

    /// <summary>Resolved identity — present only when <see cref="Success"/> is true.</summary>
    public AuthenticatedUserDto? User { get; set; }

    /// <summary>
    /// Application configuration filtered for the connecting UserType.
    /// Present only when <see cref="Success"/> is true.
    /// </summary>
    public ApplicationConfigDto? ApplicationConfig { get; set; }
}

// ── Identity ──────────────────────────────────────────────────────────────────

/// <summary>Identity resolved after successful token validation.</summary>
public class AuthenticatedUserDto
{
    public string? UserId          { get; set; }
    public string? FirstName       { get; set; }
    public string? LastName        { get; set; }
    public string? Email           { get; set; }

    /// <summary>Populated only for ANON provider.</summary>
    public string? SurveyId        { get; set; }

    /// <summary>READI | NICE | ANON</summary>
    public string  AuthProvider    { get; set; } = default!;

    /// <summary>External | Internal | Agent | Supervisor | StandAlone</summary>
    public string  UserType        { get; set; } = default!;

    public string  ApplicationName { get; set; } = default!;
    public Guid    ApplicationId   { get; set; }
    public Guid    SessionId       { get; set; }
}

// ── Application config — one section populated per UserType ───────────────────

/// <summary>
/// Application configuration returned after authentication.
/// Only the section that matches the connecting UserType is populated.
/// </summary>
public class ApplicationConfigDto
{
    public string ApplicationName { get; set; } = default!;

    /// <summary>Populated when UserType = External.</summary>
    public ExternalUserConfigDto?  ExternalConfig  { get; set; }

    /// <summary>Populated when UserType = Internal.</summary>
    public InternalUserConfigDto?  InternalConfig  { get; set; }

    /// <summary>Populated when UserType = Agent.</summary>
    public AgentConfigDto?         AgentConfig     { get; set; }

    /// <summary>Populated when UserType = Supervisor.</summary>
    public SupervisorConfigDto?    SupervisorConfig { get; set; }

    /// <summary>Populated when UserType = StandAlone.</summary>
    public StandaloneConfigDto?    StandaloneConfig { get; set; }
}

// ── Per-UserType config DTOs ──────────────────────────────────────────────────

public class ExternalUserConfigDto
{
    /// <summary>OnlyBot | OnlyHuman | BotThenHuman</summary>
    public string ChatMode            { get; set; } = default!;
    public bool   CanShareScreen      { get; set; }
    public bool   NeedScreenRecording { get; set; }
}

public class InternalUserConfigDto
{
    /// <summary>OnlyBot | TalkWithAvailableAgent</summary>
    public string ChatMode            { get; set; } = default!;
    public bool   CanShareScreen      { get; set; }
    public bool   NeedScreenRecording { get; set; }
}

public class AgentConfigDto
{
    public bool CanHandOffToOtherAgent { get; set; }
    public int  MaxParallelChats       { get; set; }
}

public class SupervisorConfigDto
{
    public int  MaxParallelChats       { get; set; }
    public bool CanHandOffToOtherAgent { get; set; }
}

public class StandaloneConfigDto
{
    /// <summary>Recording starts automatically on connect.</summary>
    public bool AutoRecordScreen       { get; set; }

    /// <summary>Supervisors can join the live recording stream.</summary>
    public bool SupervisorCanWatchLive { get; set; }
}
