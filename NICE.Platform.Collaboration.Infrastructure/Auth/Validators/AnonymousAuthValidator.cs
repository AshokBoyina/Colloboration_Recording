namespace NICE.Platform.Collaboration.Infrastructure.Auth.Validators;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Infrastructure.Auth.Settings;

/// <summary>
/// Anonymous validator — no external HTTP call is made.
/// The <c>AuthToken</c> is a JWT whose payload must contain:
///   <list type="bullet">
///     <item><c>surveyId</c>  — required</item>
///     <item><c>firstName</c> — required</item>
///     <item><c>lastName</c>  — required</item>
///   </list>
/// The signature is NOT cryptographically verified here (the token is self-contained
/// and the claims themselves are what the engine trusts).  If your deployment
/// requires signature verification, supply the key in appsettings and enable it.
///
/// When <see cref="AuthValidationSettings.UseMock"/> is <c>true</c> the token is
/// not decoded — the configured mock response is returned immediately.
/// </summary>
public sealed class AnonymousAuthValidator : IAuthValidator
{
    private const string ClaimSurveyId  = "surveyId";
    private const string ClaimFirstName = "firstName";
    private const string ClaimLastName  = "lastName";
    private const string ClaimEmail     = "email";
    private const string ClaimSub       = "sub";

    private readonly AuthValidationSettings _settings;
    private readonly ILogger<AnonymousAuthValidator> _logger;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public AnonymousAuthValidator(
        IOptions<AuthValidationSettings> settings,
        ILogger<AnonymousAuthValidator> logger)
    {
        _settings = settings.Value;
        _logger   = logger;
    }

    public Task<AuthValidatorResult> ValidateAsync(string authToken, CancellationToken ct = default)
    {
        // ── MOCK MODE ─────────────────────────────────────────────────────────
        if (_settings.UseMock)
        {
            _logger.LogWarning("[MOCK] ANON validation bypassed — token-aware mock.");
            var mock = _settings.Mock.Anon;
            if (!mock.IsValid)
                return Task.FromResult(AuthValidatorResult.Fail(mock.Error ?? "Mock ANON validation failed."));

            // In mock mode the signature is NOT verified, but we still decode the JWT payload
            // so that a properly minted anonymous JWT (e.g. one carrying firstName/lastName claims)
            // displays the real person's name rather than the generic mock defaults.
            // Slug tokens like "alice-smith" continue to work as before.
            string? jwtSub = null, jwtFirst = null, jwtLast = null,
                    jwtEmail = null, jwtSurveyId = null;

            if (!string.IsNullOrWhiteSpace(authToken) &&
                authToken.StartsWith("eyJ", StringComparison.Ordinal))
            {
                try
                {
                    var decoded = _handler.ReadJwtToken(authToken);
                    jwtSub      = GetClaim(decoded, ClaimSub);
                    jwtFirst    = GetClaim(decoded, ClaimFirstName);
                    jwtLast     = GetClaim(decoded, ClaimLastName);
                    jwtEmail    = GetClaim(decoded, ClaimEmail);
                    jwtSurveyId = GetClaim(decoded, ClaimSurveyId);
                }
                catch { /* unparseable token — fall through to slug / mock defaults */ }
            }

            // ── Resolve userId + display name from the authToken ─────────────────
            //
            // Priority:
            //   1. Valid JWT  → use sub/given_name/family_name claims (already extracted above)
            //   2. Plain name → "Troy M", "Alice Smith", "alice-smith"
            //                   (letters, digits, spaces, hyphens — up to 80 chars)
            //                   Spaces are normalised to hyphens for the userId slug.
            //   3. Anything else (JSON blob, long string) → fall back to mock defaults
            //
            string mockUserId;
            string mockFirst, mockLast;

            if (!string.IsNullOrWhiteSpace(jwtSub))
            {
                // Case 1: valid JWT — use JWT claims
                mockUserId = jwtSub;
                mockFirst  = jwtFirst  ?? mock.FirstName;
                mockLast   = jwtLast   ?? mock.LastName;
            }
            else if (!string.IsNullOrWhiteSpace(authToken) &&
                     authToken.Length <= 80 &&
                     NameInputRegex.IsMatch(authToken))
            {
                // Case 2: plain name input — "Troy M", "Alice Smith", "alice-smith"
                // Normalise to a slug for userId (spaces → hyphens, lower-case)
                var normalised = authToken.Trim()
                    .ToLowerInvariant()
                    .Replace(' ', '-');
                mockUserId = normalised;
                (mockFirst, mockLast) = ParseNameInput(authToken.Trim(), mock.FirstName, mock.LastName);
            }
            else
            {
                // Case 3: garbage / JSON blob → safe defaults
                mockUserId = mock.UserId;
                mockFirst  = mock.FirstName;
                mockLast   = mock.LastName;
            }

            var mockEmail    = jwtEmail    ?? mock.Email;
            var mockSurveyId = jwtSurveyId ?? mock.SurveyId ?? mockUserId;

            return Task.FromResult(AuthValidatorResult.Ok(mockUserId, mockEmail, mockFirst, mockLast, mockSurveyId));
        }

        // ── REAL ANON VALIDATION — decode JWT claims internally ───────────────
        if (string.IsNullOrWhiteSpace(authToken))
            return Task.FromResult(AuthValidatorResult.Fail("AuthToken is missing or empty."));

        JwtSecurityToken jwt;
        try
        {
            jwt = _handler.ReadJwtToken(authToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ANON validator: failed to parse JWT.");
            return Task.FromResult(AuthValidatorResult.Fail("AuthToken is not a valid JWT."));
        }

        // Extract required claims
        var surveyId  = GetClaim(jwt, ClaimSurveyId);
        var firstName = GetClaim(jwt, ClaimFirstName);
        var lastName  = GetClaim(jwt, ClaimLastName);

        if (string.IsNullOrWhiteSpace(surveyId))
            return Task.FromResult(AuthValidatorResult.Fail("Anonymous JWT is missing the 'surveyId' claim."));

        if (string.IsNullOrWhiteSpace(firstName))
            return Task.FromResult(AuthValidatorResult.Fail("Anonymous JWT is missing the 'firstName' claim."));

        if (string.IsNullOrWhiteSpace(lastName))
            return Task.FromResult(AuthValidatorResult.Fail("Anonymous JWT is missing the 'lastName' claim."));

        // Optional claims
        var email  = GetClaim(jwt, ClaimEmail);
        var userId = GetClaim(jwt, ClaimSub) ?? surveyId;   // fall back to surveyId as userId

        _logger.LogInformation(
            "ANON validation succeeded for SurveyId={SurveyId}, Name={FirstName} {LastName}.",
            surveyId, firstName, lastName);

        return Task.FromResult(AuthValidatorResult.Ok(
            userId:    userId,
            firstName: firstName,
            lastName:  lastName,
            email:     email,
            surveyId:  surveyId));
    }

    private static string? GetClaim(System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwt, string claimType)
        => jwt.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;

    /// <summary>
    /// If <paramref name="token"/> is a JWT, reads the 'sub' claim without signature verification.
    /// Returns null if the token is not a JWT or has no 'sub' claim.
    /// Used in mock mode to avoid storing the full JWT string as ExternalUserId.
    /// </summary>
    private string? TryExtractSubFromJwt(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("eyJ", StringComparison.Ordinal))
            return null;
        try
        {
            var jwt = _handler.ReadJwtToken(token);
            return GetClaim(jwt, ClaimSub);
        }
        catch { return null; }
    }

    // Matches a hyphen-only slug: "alice-smith", "troy-m"
    // Used by ParseDemoName (no spaces allowed — DB slug format).
    private static readonly System.Text.RegularExpressions.Regex SlugRegex =
        new(@"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches any human-readable name input: letters, digits, spaces, hyphens.
    // Accepts "Troy M", "Alice Smith", "alice-smith", "José García" etc.
    // Rejects JWTs (contain '.'), JSON (contain '{'), arbitrary long strings.
    private static readonly System.Text.RegularExpressions.Regex NameInputRegex =
        new(@"^[\p{L}\p{N}][\p{L}\p{N} \-]*[\p{L}\p{N}]$|^[\p{L}\p{N}]$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Converts a demo token slug to a (First, Last) name pair.
    /// "alice-smith" → ("Alice", "Smith").  Falls back to provided defaults
    /// if <paramref name="token"/> is null/empty, too long, or does not look
    /// like a simple slug (i.e. is a JWT, JSON blob, or other arbitrary string).
    /// Called by all validators in mock mode.
    /// </summary>
    public static (string First, string Last) ParseDemoName(
        string? token, string defaultFirst, string defaultLast)
    {
        // Only parse as a name slug when the string is short and slug-shaped.
        // Anything else (JWTs, JSON, full URIs…) uses the configured defaults
        // so that DB name columns are never at risk of truncation.
        if (string.IsNullOrWhiteSpace(token)
            || token.Length > 80
            || !SlugRegex.IsMatch(token))
            return (defaultFirst, defaultLast);

        var parts = token.Trim().Split('-', 2, StringSplitOptions.RemoveEmptyEntries);

        static string Cap(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

        return parts.Length >= 2
            ? (Cap(parts[0]), Cap(parts[1]))
            : (Cap(parts[0]), defaultLast);
    }

    /// <summary>
    /// Parses a free-text name input (which may contain spaces or hyphens) into
    /// a (First, Last) pair.
    ///
    /// Examples:
    ///   "Troy M"       → ("Troy", "M")
    ///   "Alice Smith"  → ("Alice", "Smith")
    ///   "alice-smith"  → ("Alice", "Smith")
    ///   "Troy"         → ("Troy", defaultLast)
    ///   "Alice Marie Smith" → ("Alice", "Marie Smith")  [first word = first, rest = last]
    ///
    /// Falls back to <paramref name="defaultFirst"/>/<paramref name="defaultLast"/>
    /// when <paramref name="input"/> is null/empty.
    /// </summary>
    internal static (string First, string Last) ParseNameInput(
        string? input, string defaultFirst, string defaultLast)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (defaultFirst, defaultLast);

        static string Cap(string s) =>
            s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

        // Normalise: treat hyphens as spaces so "alice-smith" and "alice smith" work the same
        var normalised = input.Replace('-', ' ').Trim();
        var parts      = normalised.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 2
            ? (Cap(parts[0]), Cap(string.Join(" ", parts[1..])))   // "Troy" + "M" or "Marie Smith"
            : (Cap(parts[0]), defaultLast);
    }
}
