namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Table: CollaborationApplicationUserTypeConfigs
/// One row per UserType per Application.
/// This is the SQL source of truth that replaces the JSON Applications mock.
/// </summary>
public class CollaborationApplicationUserTypeConfig
{
    public Guid   Id            { get; set; }
    public Guid   ApplicationId { get; set; }

    /// <summary>External | Internal | Agent | Supervisor | StandAlone</summary>
    public string UserType      { get; set; } = default!;

    // ── External / Internal ───────────────────────────────────────────────────
    /// <summary>OnlyBot | OnlyHuman | BotThenHuman | TalkWithAvailableAgent</summary>
    public string? ChatMode            { get; set; }
    public bool    CanShareScreen      { get; set; }
    public bool    NeedScreenRecording { get; set; }

    // ── Agent / Supervisor ────────────────────────────────────────────────────
    public bool CanHandOffToOtherAgent { get; set; }
    public int  MaxParallelChats       { get; set; }

    // ── StandAlone ────────────────────────────────────────────────────────────
    public bool AutoRecordScreen       { get; set; }
    public bool SupervisorCanWatchLive { get; set; }

    // Navigation
    public CollaborationApplication Application { get; set; } = default!;
}
