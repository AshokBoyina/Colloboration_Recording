namespace NICE.Platform.Collaboration.Application.Interfaces.Auth;

/// <summary>
/// Validates an external JWT token issued by Readi or Nice.
/// User identity (name, email) and the target application are supplied by the caller
/// via the request body — the JWT itself is used only for cryptographic validation
/// (signature, issuer, audience, expiry).
/// </summary>
public interface IExternalAuthService
{
    /// <summary>
    /// Validates the raw JWT string against the OIDC configuration for
    /// <paramref name="applicationName"/>.
    /// Never throws — validation failures are expressed via
    /// <see cref="ExternalAuthResult.IsValid"/> and <see cref="ExternalAuthResult.Error"/>.
    /// </summary>
    /// <param name="token">Raw JWT from the X-Api-Key header.</param>
    /// <param name="applicationName">
    /// Provider key that must match an entry in the "AuthProviders" appsettings section
    /// (e.g. "Readi" or "Nice").
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExternalAuthResult> ValidateAsync(
        string token,
        string applicationName,
        CancellationToken ct = default);
}

/// <summary>Result returned by <see cref="IExternalAuthService.ValidateAsync"/>.</summary>
public sealed record ExternalAuthResult(
    bool    IsValid,
    string? ApplicationName,
    Guid    ApplicationId,
    string? Error
)
{
    /// <summary>Convenience factory for failure cases.</summary>
    public static ExternalAuthResult Fail(string error) =>
        new(false, null, Guid.Empty, error);
}
