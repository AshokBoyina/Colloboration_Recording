namespace NICE.Platform.Collaboration.Infrastructure.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;

/// <summary>
/// Validates external JWT tokens for two provider modes:
///
///   Symmetric (HS256) — Readi:
///     Token is signed with a shared secret configured in appsettings.
///     Validation is local — no HTTP calls needed.
///
///   OIDC / asymmetric — Nice:
///     Signing keys are fetched (and cached) from the provider's
///     .well-known/openid-configuration discovery endpoint.
///
/// The provider is selected by the <c>applicationName</c> parameter supplied
/// by the caller via the request body — the token itself carries no routing claims.
/// </summary>
public sealed class MultiProviderJwtAuthService : IExternalAuthService
{
    private readonly Dictionary<string, AuthProviderConfig> _providers;

    // Only populated for OIDC (asymmetric) providers.
    private readonly Dictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> _oidcManagers;

    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly ILogger<MultiProviderJwtAuthService> _logger;

    public MultiProviderJwtAuthService(
        IOptions<Dictionary<string, AuthProviderConfig>> options,
        ILogger<MultiProviderJwtAuthService> logger)
    {
        _providers = options.Value;
        _logger    = logger;

        _oidcManagers = _providers
            .Where(kv => !kv.Value.IsSymmetric && kv.Value.Authority is not null)
            .ToDictionary(
                kv => kv.Key,
                kv => (IConfigurationManager<OpenIdConnectConfiguration>)
                    new ConfigurationManager<OpenIdConnectConfiguration>(
                        $"{kv.Value.Authority!.TrimEnd('/')}/.well-known/openid-configuration",
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = true }
                    ),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ExternalAuthResult> ValidateAsync(
        string token,
        string applicationName,
        CancellationToken ct = default)
    {
        // ── Step 1: resolve provider ──────────────────────────────────────────
        if (!_providers.TryGetValue(applicationName, out var config))
        {
            _logger.LogWarning(
                "Auth validation requested for unknown application '{ApplicationName}'.",
                applicationName);
            return ExternalAuthResult.Fail($"Unknown or unconfigured application: '{applicationName}'.");
        }

        // ── Step 2: basic parse (catch obviously malformed tokens early) ──────
        try
        {
            _handler.ReadJwtToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT could not be parsed for '{ApplicationName}'.", applicationName);
            return ExternalAuthResult.Fail("Token format is invalid.");
        }

        // ── Step 3: resolve signing keys ──────────────────────────────────────
        IEnumerable<SecurityKey> signingKeys;
        try
        {
            signingKeys = await ResolveSigningKeysAsync(applicationName, config, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve signing keys for provider '{ApplicationName}'.", applicationName);
            return ExternalAuthResult.Fail($"Auth provider configuration unavailable for '{applicationName}'.");
        }

        // ── Step 4: full validation — issuer, audience, lifetime, signature ───
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = config.Issuer,
            ValidateAudience         = config.ValidateAudience,
            ValidAudience            = config.Audience,
            ValidateLifetime         = true,
            IssuerSigningKeys        = signingKeys,
            ValidateIssuerSigningKey = true,
            ClockSkew                = TimeSpan.FromMinutes(2),
        };

        try
        {
            _handler.ValidateToken(token, validationParams, out _);
        }
        catch (SecurityTokenExpiredException)
        {
            return ExternalAuthResult.Fail("Token has expired.");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return ExternalAuthResult.Fail("Token signature is invalid.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Token validation failed for provider '{ApplicationName}'.", applicationName);
            return ExternalAuthResult.Fail("Token validation failed.");
        }

        _logger.LogInformation(
            "Token validated for provider '{ApplicationName}' (app {ApplicationId}).",
            applicationName, config.ApplicationId);

        return new ExternalAuthResult(
            IsValid:         true,
            ApplicationName: applicationName,
            ApplicationId:   config.ApplicationId,
            Error:           null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IEnumerable<SecurityKey>> ResolveSigningKeysAsync(
        string applicationName,
        AuthProviderConfig config,
        CancellationToken ct)
    {
        if (config.IsSymmetric)
        {
            // Symmetric (HS256) — Readi: derive key directly from the configured secret.
            return [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.Secret!))];
        }

        // OIDC / asymmetric — Nice: fetch JWKS from discovery (cached by ConfigurationManager).
        var oidcConfig = await _oidcManagers[applicationName].GetConfigurationAsync(ct);
        return oidcConfig.SigningKeys;
    }
}
