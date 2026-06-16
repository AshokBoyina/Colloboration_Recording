namespace NICE.Platform.Collaboration.Core.Requests;

/// <summary>
/// All authentication parameters arrive as HTTP request headers — there is no body.
///
/// Required headers:
///
///   X-Api-Key    — the registered application's secret API key.
///                  Identifies the application and, together with X-Access-Key, determines
///                  which StaffAuthProvider (READI | NICE | ANON) is used for non-External users.
///
///   X-Access-Key — the Application Name (e.g. "SurveyPortal", "Readi").
///                  Used to load the application's full configuration from the store.
///                  The calling client never selects the auth provider directly.
///
///   AuthToken    — the raw token to validate.  Format depends on UserType:
///                    External  → Anonymous JWT (surveyId / firstName / lastName claims).
///                    Others    → Token for the application's configured StaffAuthProvider.
///
///   UserType     — role of the connecting user:
///                  External | Internal | Agent | Supervisor | StandAlone
///
/// Auth provider routing (enforced server-side):
///   • External   → Always ANON.  AuthToken is an anonymous JWT decoded locally.
///   • All others → Application's StaffAuthProvider (READI | NICE | ANON), resolved
///                  from the X-Api-Key / X-Access-Key pair.  Different applications
///                  can use different providers — the key determines which one.
/// </summary>
public static class AuthHeaders
{
    /// <summary>
    /// The application's secret API key. Identifies the application and determines
    /// the StaffAuthProvider used for Agent / Supervisor / Internal / StandAlone users.
    /// </summary>
    public const string ApiKey    = "X-Api-Key";

    /// <summary>
    /// The Application Name — used to load the application config and its StaffAuthProvider.
    /// External users always use ANON regardless of the configured provider.
    /// </summary>
    public const string AccessKey = "X-Access-Key";

    /// <summary>
    /// The raw token to validate.
    /// External users supply an anonymous JWT; all other users supply a token
    /// for the application's configured StaffAuthProvider.
    /// </summary>
    public const string AuthToken = "AuthToken";

    /// <summary>The connecting user's role: External | Internal | Agent | Supervisor | StandAlone.</summary>
    public const string UserType  = "UserType";
}
