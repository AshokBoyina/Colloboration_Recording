namespace NICE.Platform.StandaloneRecording.Client;

/// <summary>
/// Configuration that a host application (e.g. Readi) supplies when
/// embedding the Standalone Screen Recording UI.
///
/// Populate via appsettings.json → section "StandaloneRecording" and bind
/// using <c>services.Configure&lt;StandaloneRecordingConfig&gt;(config.GetSection("StandaloneRecording"))</c>.
/// </summary>
public sealed class StandaloneRecordingConfig
{
    public const string SectionName = "StandaloneRecording";

    /// <summary>
    /// Base URL of the Standalone Recording API.
    /// recorder.html and monitor.html are served from this same URL (wwwroot static files).
    /// Example: "https://recording-api.readi.example.com" or "http://localhost:65168"
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:65168";

    /// <summary>
    /// Application API key registered in the Standalone Recording API.
    /// Passed as X-Api-Key header on every request.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Application name as registered (e.g. "Readi").
    /// Passed as X-Access-Key header and used to look up per-app configuration.
    /// </summary>
    public string ApplicationName { get; set; } = "Readi";

    /// <summary>
    /// Full name of the logged-in user (e.g. "John Smith").
    /// Used to auto-mint a LOCAL_JWT demo token.
    /// Must contain a space so JWT given_name and family_name claims are both non-empty.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// User role: "StandAlone" for recorders, "StandaloneMonitor" for supervisors watching live.
    /// </summary>
    public string UserRole { get; set; } = "StandAlone";

    /// <summary>
    /// Pre-minted access token.  When non-empty the client uses this token directly
    /// and skips auto-minting.  Set this when the host already holds a valid LOCAL_JWT
    /// from its own sign-in flow.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;
}
