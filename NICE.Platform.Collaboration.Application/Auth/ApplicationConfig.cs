namespace NICE.Platform.Collaboration.Application.Auth;

/// <summary>
/// Full configuration for one registered application.
/// Loaded from the JSON mock (Applications section in appsettings)
/// and later from the SQL ApplicationConfiguration table.
///
/// X-Access-Key header carries the ApplicationName.
/// Auth provider routing is enforced here — the calling client never selects the
/// validator directly.
///
/// Auth provider routing rules:
///   • UserType = External  → Always ANON (hardcoded; StaffAuthProvider is ignored).
///   • All other UserTypes  → StaffAuthProvider value (READI | NICE | ANON).
/// </summary>
public class ApplicationConfig
{
    /// <summary>Unique application name — matches the X-Access-Key header value.</summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Auth provider used for Agent, Supervisor, Internal, and StandAlone users.
    /// Accepted values: READI | NICE | ANON.
    /// Configured per-application via X-Api-Key — the client never chooses this.
    /// External users always use ANON regardless of this setting.
    /// </summary>
    public string StaffAuthProvider { get; set; } = default!;

    /// <summary>Config returned to External users.</summary>
    public ExternalUserConfig? External { get; set; }

    /// <summary>Config returned to Internal users.</summary>
    public InternalUserConfig? Internal { get; set; }

    /// <summary>Config returned to Agents.</summary>
    public AgentConfig? Agent { get; set; }

    /// <summary>Config returned to Supervisors.</summary>
    public SupervisorConfig? Supervisor { get; set; }

    /// <summary>Config returned for StandAlone screen recording connections.</summary>
    public StandaloneConfig? StandAlone { get; set; }
}

// ── Per-UserType config models ────────────────────────────────────────────────

public class ExternalUserConfig
{
    /// <summary>OnlyBot | OnlyHuman | BotThenHuman</summary>
    public string ChatMode            { get; set; } = "BotThenHuman";
    public bool   CanShareScreen      { get; set; }
    public bool   NeedScreenRecording { get; set; }
}

public class InternalUserConfig
{
    /// <summary>OnlyBot | TalkWithAvailableAgent</summary>
    public string ChatMode            { get; set; } = "TalkWithAvailableAgent";
    public bool   CanShareScreen      { get; set; }
    public bool   NeedScreenRecording { get; set; }
}

public class AgentConfig
{
    public bool CanHandOffToOtherAgent { get; set; }
    public int  MaxParallelChats       { get; set; } = 5;
}

public class SupervisorConfig
{
    public int  MaxParallelChats       { get; set; } = 10;
    public bool CanHandOffToOtherAgent { get; set; }
}

public class StandaloneConfig
{
    /// <summary>Recording starts automatically on connect when true.</summary>
    public bool AutoRecordScreen      { get; set; }

    /// <summary>Supervisors can join and watch the live recording stream when true.</summary>
    public bool SupervisorCanWatchLive { get; set; }
}
