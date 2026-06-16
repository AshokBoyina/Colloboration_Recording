namespace NICE.Platform.Collaboration.Core.Constants;
public static class SignalRGroups
{
    public static string Collaboration(Guid id) => $"collab-{id}";
    public static string Application(Guid id)   => $"app-{id}";
    public static string SilentMonitor(Guid id) => $"silent-{id}";
    public static string Recording(Guid id)     => $"recording-{id}";
    public static string Agent(Guid id)         => $"agent-{id}";
    public static string Supervisor(Guid id)    => $"supervisor-{id}";
    /// <summary>Personal group for each Internal staff member — used for direct internal chat routing.</summary>
    public static string Internal(Guid id)      => $"internal-{id}";

    /// <summary>
    /// Group joined by all StandAlone supervisors in an application.
    /// They receive RecordingStarted / RecordingStopped broadcasts.
    /// </summary>
    public static string StandAlone(Guid appId) => $"standalone-{appId}";

    /// <summary>
    /// Group joined by all StandaloneMonitor users in an application.
    /// They receive StandaloneSessionStarted / CollaborationEnded broadcasts
    /// so their session list stays live without polling.
    /// </summary>
    public static string StandaloneMonitor(Guid appId) => $"standalone-monitor-{appId}";
}
