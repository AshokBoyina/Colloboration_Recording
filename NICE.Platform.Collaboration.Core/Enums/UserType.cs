namespace NICE.Platform.Collaboration.Core.Enums;

public enum UserType
{
    External,
    Internal,
    Agent,
    Supervisor,
    StandAlone,
    /// <summary>Screen recorder — captures their own screen, shares live + saves to disk/blob.</summary>
    Standalone,
    /// <summary>Watches all active Standalone sessions live via WebRTC.</summary>
    StandaloneMonitor
}
