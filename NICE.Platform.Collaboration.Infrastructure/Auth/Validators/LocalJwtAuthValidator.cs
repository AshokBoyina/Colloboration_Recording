namespace NICE.Platform.Collaboration.Infrastructure.Auth.Validators;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Infrastructure.Auth.Settings;

/// <summary>
/// Local Signed JWT validator — the production-testing auth provider.
///
/// Verifies an HMAC-SHA256 signed JWT without calling any external identity server.
/// The signing secret lives in <c>appsettings.json → AuthValidation:LocalJwt:Secret</c>
/// and must be ≥ 32 characters (256 bits).
///
/// Required token claims:
///   sub          — user identifier (any stable string; becomes ExternalUserId in DB)
///   given_name   — first name
///   family_name  — last name
///
/// Optional token claims:
///   email        — user email
///
/// Minting tokens:
///   • Easiest:  POST /api/v1/demo/mint-token?name=Alice+Smith&amp;role=Agent  (returns a ready token)
///   • Manual:   jwt.io → paste the secret → set iss/aud/sub/given_name/family_name claims
///   • CLI:      any JWT tool using HS256 + the configured secret
///
/// When <see cref="AuthValidationSettings.UseMock"/> is <c>true</c> the signature check
/// is bypassed and the configured mock response is returned — same behaviour as other validators.
/// </summary>
public sealed class LocalJwtAuthValidator : IAuthValidator
{
    private readonly AuthValidationSettings          _settings;
    private readonly ILogger<LocalJwtAuthValidator>  _logger;
    private readonly JwtSecurityTokenHandler         _handler = new() { MapInboundClaims = false };

    public LocalJwtAuthValidator(
        IOptions<AuthValidationSettings>       settings,
        ILogger<LocalJwtAuthValidator>         logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public Task<AuthValidatorResult> ValidateAsync(
        string authToken, CancellationToken ct = default)
    {
        // ── MOCK MODE ─────────────────────────────────────────────────────────
        if (_settings.UseMock)
        {
            _logger.LogWarning("[MOCK] LOCAL_JWT validation bypassed — using mock response.");
            var mock = _settings.Mock.Anon;   // reuse ANON mock block — same shape
            if (!mock.IsValid)
                return Task.FromResult(AuthValidatorResult.Fail(mock.Error ?? "Mock LOCAL_JWT validation failed."));

            // In mock mode the signature is NOT verified, but we still decode the JWT payload
            // so that a properly minted token (e.g. from /api/v1/demo/mint-token?name=Rajiv+Jain)
            // displays the real person's name / sub rather than the generic mock defaults.
            // This lets devs test with realistic names without disabling mock mode entirely.
            string? jwtSub = null, jwtFirst = null, jwtLast = null, jwtEmail = null;
            if (!string.IsNullOrWhiteSpace(authToken) &&
                authToken.StartsWith("eyJ", StringComparison.Ordinal))
            {
                try
                {
                    var decoded = _handler.ReadJwtToken(authToken);
                    jwtSub   = decoded.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
                    jwtFirst = decoded.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.GivenName
                                                               || c.Type == "given_name")?.Value;
                    jwtLast  = decoded.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.FamilyName
                                                               || c.Type == "family_name")?.Value;
                    jwtEmail = decoded.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email
                                                               || c.Type == "email")?.Value;
                }
                catch { /* unparseable — fall through to mock defaults */ }
            }

            // UserId: prefer JWT sub → slug authToken → configured mock id
            string mockUserId;
            if (!string.IsNullOrWhiteSpace(jwtSub))
                mockUserId = jwtSub;
            else if (!string.IsNullOrWhiteSpace(authToken) &&
                     authToken.Length <= 80 &&
                     System.Text.RegularExpressions.Regex.IsMatch(
                         authToken, @"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?$"))
                mockUserId = authToken;   // safe slug e.g. "rajiv-jain"
            else
                mockUserId = mock.UserId; // garbage / JSON blob → use configured default

            // Name: prefer real JWT claims → ParseDemoName(sub slug) → mock defaults
            var firstName = !string.IsNullOrWhiteSpace(jwtFirst)
                ? jwtFirst
                : AnonymousAuthValidator.ParseDemoName(mockUserId, mock.FirstName, mock.LastName).First;
            var lastName  = !string.IsNullOrWhiteSpace(jwtLast)
                ? jwtLast
                : AnonymousAuthValidator.ParseDemoName(mockUserId, mock.FirstName, mock.LastName).Last;
            var email     = jwtEmail ?? mock.Email;

            return Task.FromResult(AuthValidatorResult.Ok(mockUserId, email, firstName, lastName));
        }

        // ── REAL LOCAL_JWT VALIDATION ─────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(authToken))
            return Task.FromResult(AuthValidatorResult.Fail("AuthToken is missing or empty."));

        var cfg = _settings.LocalJwt;

        if (string.IsNullOrWhiteSpace(cfg.Secret))
        {
            _logger.LogError(
                "LOCAL_JWT secret is not configured. " +
                "Set AuthValidation:LocalJwt:Secret in appsettings.json (min 32 chars).");
            return Task.FromResult(AuthValidatorResult.Fail(
                "LOCAL_JWT provider is not properly configured on the server."));
        }

        if (cfg.Secret.Length < 32)
        {
            _logger.LogError(
                "LOCAL_JWT secret is too short ({Length} chars). Minimum is 32 chars (256 bits) for HS256.",
                cfg.Secret.Length);
            return Task.FromResult(AuthValidatorResult.Fail(
                "LOCAL_JWT secret is too short — must be at least 32 characters."));
        }

        try
        {
            var key              = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg.Secret));
            var validationParams = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidateIssuer           = !string.IsNullOrWhiteSpace(cfg.Issuer),
                ValidIssuer              = cfg.Issuer,
                ValidateAudience         = !string.IsNullOrWhiteSpace(cfg.Audience),
                ValidAudience            = cfg.Audience,
                ValidateLifetime         = cfg.ValidateExpiry,
                ClockSkew                = TimeSpan.FromMinutes(2)
            };

            var principal = _handler.ValidateToken(authToken, validationParams, out _);

            // Extract required claims
            var sub        = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                          ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var firstName  = principal.FindFirstValue(JwtRegisteredClaimNames.GivenName)
                          ?? principal.FindFirstValue("given_name");
            var lastName   = principal.FindFirstValue(JwtRegisteredClaimNames.FamilyName)
                          ?? principal.FindFirstValue("family_name");
            var email      = principal.FindFirstValue(JwtRegisteredClaimNames.Email)
                          ?? principal.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(sub))
                return Task.FromResult(AuthValidatorResult.Fail(
                    "LOCAL_JWT is missing the 'sub' claim (user identifier)."));

            if (string.IsNullOrWhiteSpace(firstName))
                return Task.FromResult(AuthValidatorResult.Fail(
                    "LOCAL_JWT is missing the 'given_name' claim."));

            if (string.IsNullOrWhiteSpace(lastName))
                return Task.FromResult(AuthValidatorResult.Fail(
                    "LOCAL_JWT is missing the 'family_name' claim."));

            _logger.LogInformation(
                "LOCAL_JWT validation succeeded for sub={Sub}, name={First} {Last}.",
                sub, firstName, lastName);

            return Task.FromResult(AuthValidatorResult.Ok(
                userId:    sub,
                email:     email,
                firstName: firstName,
                lastName:  lastName));
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("LOCAL_JWT token has expired.");
            return Task.FromResult(AuthValidatorResult.Fail("Token has expired. Please mint a new token."));
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("LOCAL_JWT signature verification failed — wrong secret?");
            return Task.FromResult(AuthValidatorResult.Fail(
                "Token signature is invalid. Ensure the token was signed with the configured LocalJwt:Secret."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LOCAL_JWT validation failed.");
            return Task.FromResult(AuthValidatorResult.Fail($"Token validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Reads the 'sub' claim from a JWT without signature verification.
    /// Used in mock mode to avoid storing the full JWT string as ExternalUserId.
    /// Returns null if <paramref name="token"/> is not a JWT or has no 'sub' claim.
    /// </summary>
    private string? TryExtractSub(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("eyJ", StringComparison.Ordinal))
            return null;
        try
        {
            var jwt = _handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
        }
        catch { return null; }
    }
}
