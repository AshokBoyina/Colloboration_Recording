namespace NICE.Platform.Collaboration.Application.Auth;

/// <summary>
/// Unified result returned by every <see cref="Interfaces.Auth.IAuthValidator"/> implementation.
/// Never throws — validation failures are expressed through <see cref="IsValid"/> and <see cref="Error"/>.
/// </summary>
public sealed record AuthValidatorResult(
    bool    IsValid,
    string? UserId,
    string? Email,
    string? FirstName,
    string? LastName,
    string? SurveyId,   // Populated only for ANON validator
    string? Error
)
{
    /// <summary>Convenience factory for failure results.</summary>
    public static AuthValidatorResult Fail(string error) =>
        new(false, null, null, null, null, null, error);

    /// <summary>Convenience factory for success results.</summary>
    public static AuthValidatorResult Ok(
        string  userId,
        string? email     = null,
        string? firstName = null,
        string? lastName  = null,
        string? surveyId  = null) =>
        new(true, userId, email, firstName, lastName, surveyId, null);
}
