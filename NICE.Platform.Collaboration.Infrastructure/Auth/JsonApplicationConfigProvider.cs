namespace NICE.Platform.Collaboration.Infrastructure.Auth;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;

/// <summary>
/// JSON-backed implementation of <see cref="IApplicationConfigProvider"/>.
///
/// Reads all registered applications from the <c>Applications</c> section
/// in appsettings.json (or appsettings.Development.json).
/// Lookup is case-insensitive on the application name.
///
/// Replace this with a SQL EF Core implementation when the
/// ApplicationConfiguration table is ready (Phase 2).
///
/// Expected appsettings shape:
/// <code>
/// "Applications": {
///   "SurveyPortal": {
///     // StaffAuthProvider applies to Agent, Supervisor, Internal, StandAlone.
///     // External users always use ANON — this field is ignored for them.
///     "StaffAuthProvider": "READI",
///     "External":    { "ChatMode": "BotThenHuman", "CanShareScreen": true, "NeedScreenRecording": false },
///     "Internal":    { "ChatMode": "TalkWithAvailableAgent", "CanShareScreen": true, "NeedScreenRecording": false },
///     "Agent":       { "CanHandOffToOtherAgent": true, "MaxParallelChats": 5 },
///     "Supervisor":  { "MaxParallelChats": 10, "CanHandOffToOtherAgent": false },
///     "StandAlone":  { "AutoRecordScreen": true, "SupervisorCanWatchLive": false }
///   }
/// }
/// </code>
/// </summary>
public sealed class JsonApplicationConfigProvider(
    IConfiguration configuration,
    ILogger<JsonApplicationConfigProvider> logger) : IApplicationConfigProvider
{
    private const string SectionName = "Applications";

    public Task<ApplicationConfig?> GetByNameAsync(string applicationName, CancellationToken ct = default)
    {
        var appsSection = configuration.GetSection(SectionName);

        if (!appsSection.Exists())
        {
            logger.LogWarning(
                "No '{Section}' section found in configuration. " +
                "Add application definitions to appsettings.json.", SectionName);
            return Task.FromResult<ApplicationConfig?>(null);
        }

        // Case-insensitive match — application names in headers may vary in casing.
        ApplicationConfig? match = null;

        foreach (var appSection in appsSection.GetChildren())
        {
            if (!string.Equals(appSection.Key, applicationName, StringComparison.OrdinalIgnoreCase))
                continue;

            match = new ApplicationConfig
            {
                Name              = appSection.Key,
                StaffAuthProvider = appSection["StaffAuthProvider"] ?? string.Empty,
                External          = BindIfPresent<ExternalUserConfig>(appSection, "External"),
                Internal     = BindIfPresent<InternalUserConfig>(appSection, "Internal"),
                Agent        = BindIfPresent<AgentConfig>(appSection, "Agent"),
                Supervisor   = BindIfPresent<SupervisorConfig>(appSection, "Supervisor"),
                StandAlone   = BindIfPresent<StandaloneConfig>(appSection, "StandAlone")
            };
            break;
        }

        if (match is null)
        {
            logger.LogWarning(
                "Application '{ApplicationName}' not found in '{Section}' configuration.",
                applicationName, SectionName);
        }
        else
        {
            logger.LogDebug(
                "Loaded config for application '{ApplicationName}' (StaffAuthProvider={Provider}).",
                match.Name, match.StaffAuthProvider);
        }

        return Task.FromResult(match);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static T? BindIfPresent<T>(IConfigurationSection parent, string key)
        where T : class, new()
    {
        var section = parent.GetSection(key);
        if (!section.Exists()) return null;
        var result = new T();
        section.Bind(result);
        return result;
    }
}
