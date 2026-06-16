namespace NICE.Platform.Collaboration.Application.Auth;

/// <summary>
/// Configuration for a single external JWT auth provider (e.g. Readi, Nice).
/// Bound from appsettings "AuthProviders:{ProviderName}".
///
/// Two provider modes are supported — set exactly one of <see cref="Secret"/> or <see cref="Authority"/>:
///
///   Symmetric (HS256) — e.g. Readi:
///     Set <see cref="Secret"/> with the shared signing key.
///     <see cref="Authority"/> is not required.
///
///   OIDC / asymmetric — e.g. Nice:
///     Set <see cref="Authority"/> so signing keys are fetched from the discovery endpoint.
///     <see cref="Secret"/> is not required.
/// </summary>
public class AuthProviderConfig
{
    /// <summary>
    /// Shared symmetric signing secret (HS256).
    /// Set this for providers that sign tokens with a shared secret key (e.g. Readi).
    /// Mutually exclusive with <see cref="Authority"/>.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// OIDC authority base URL used for .well-known/openid-configuration discovery (e.g. Nice).
    /// Mutually exclusive with <see cref="Secret"/>.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>Expected token issuer (iss claim). Required for both modes.</summary>
    public string Issuer { get; set; } = default!;

    /// <summary>Expected token audience (aud claim). Required for both modes.</summary>
    public string Audience { get; set; } = default!;

    /// <summary>Internal ApplicationRegistration ID that maps to this provider.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>Set false only if the provider does not embed a specific audience (not recommended).</summary>
    public bool ValidateAudience { get; set; } = true;

    /// <summary>True when using symmetric key validation.</summary>
    public bool IsSymmetric => Secret is not null;
}
