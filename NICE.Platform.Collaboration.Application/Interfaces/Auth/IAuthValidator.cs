namespace NICE.Platform.Collaboration.Application.Interfaces.Auth;

using NICE.Platform.Collaboration.Application.Auth;

/// <summary>
/// Contract for a single auth provider validator (READI, NICE, or ANON).
/// Each implementation validates the raw <c>AuthToken</c> according to its own rules
/// and returns a unified <see cref="AuthValidatorResult"/>.
/// </summary>
public interface IAuthValidator
{
    /// <summary>
    /// Validates the supplied <paramref name="authToken"/>.
    /// Never throws — validation failures are returned via <see cref="AuthValidatorResult.IsValid"/>.
    /// </summary>
    Task<AuthValidatorResult> ValidateAsync(string authToken, CancellationToken ct = default);
}
