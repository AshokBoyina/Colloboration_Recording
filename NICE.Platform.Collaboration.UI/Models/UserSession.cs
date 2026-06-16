namespace NICE.Platform.Collaboration.UI.Models;

public class UserSession
{
    public string Token           { get; set; } = string.Empty;
    public string UserId          { get; set; } = string.Empty;
    public string DisplayName     { get; set; } = string.Empty;
    public string ApplicationId   { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    /// <summary>External | Internal | Agent | Supervisor | Standalone</summary>
    public string UserType          { get; set; } = string.Empty;

    // ── Bot API credentials (captured at login, not from appsettings) ──────────

    /// <summary>X-Api-Key forwarded to the real Bot API on every call (empty = no bot auth).</summary>
    public string BotApiKey         { get; set; } = string.Empty;

    /// <summary>X-API-Access-Key forwarded to the real Bot API on every call.</summary>
    public string BotApiAccessKey   { get; set; } = string.Empty;

    // Parsed helpers
    public Guid   UserId_Guid       => Guid.TryParse(UserId, out var g) ? g : Guid.Empty;
    public Guid   ApplicationId_Guid => Guid.TryParse(ApplicationId, out var g) ? g : Guid.Empty;

    public bool IsAuthenticated     => !string.IsNullOrEmpty(Token);

    // ── Convenience properties for hub service / pages ──────────────────────
    // Expose UserId as Guid directly to avoid repeated parsing in every page.
    public Guid UserId_Parsed
    {
        get => Guid.TryParse(UserId, out var g) ? g : Guid.Empty;
        set => UserId = value.ToString();
    }
}

// Extension so code can use Auth.Current.UserId as Guid
public static class UserSessionExtensions
{
    public static Guid GetUserId(this UserSession s) =>
        Guid.TryParse(s.UserId, out var g) ? g : Guid.Empty;
    public static Guid GetApplicationId(this UserSession s) =>
        Guid.TryParse(s.ApplicationId, out var g) ? g : Guid.Empty;
}
