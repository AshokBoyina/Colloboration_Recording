namespace NICE.Platform.Collaboration.UI.Models;

/// <summary>
/// Maps to the four HTTP headers expected by POST api/v1/collaboration/auth/validate.
/// </summary>
public class LoginRequest
{
    /// <summary>X-Api-Key header — any non-empty string is accepted in Phase 1 (stub).</summary>
    public string ApiKey          { get; set; } = "chat-ui-key";

    /// <summary>
    /// X-Access-Key header — the registered Application Name.
    /// Determines the AuthProvider: SurveyPortal=ANON, Readi=READI, NicePortal=NICE.
    /// </summary>
    public string ApplicationName { get; set; } = "SurveyPortal";

    /// <summary>AuthToken header — the raw external token (any value in mock mode).</summary>
    public string AuthToken       { get; set; } = string.Empty;

    /// <summary>UserType header — External | Internal | Agent | Supervisor | StandAlone.</summary>
    public string UserType        { get; set; } = "External";

    // ── Bot API credentials (populated from the login form, not from appsettings) ──

    /// <summary>
    /// Sent as X-Api-Key on every real bot API call.
    /// Derived from the X-Api-Key field on the login form (same value used for auth).
    /// </summary>
    public string BotApiKey       { get; set; } = string.Empty;

    /// <summary>
    /// Sent as X-API-Access-Key on every real bot API call.
    /// Derived from the X-Access-Key / ApplicationName field on the login form.
    /// </summary>
    public string BotApiAccessKey { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool   Success     { get; set; }
    public string Error       { get; set; } = string.Empty;
    public string Token       { get; set; } = string.Empty;
    public string UserId      { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UserType    { get; set; } = string.Empty;
}

