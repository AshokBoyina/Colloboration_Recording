namespace NICE.Platform.Collaboration.Infrastructure.Auth.Validators;

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Infrastructure.Auth.Settings;

/// <summary>
/// READI auth provider — the production (non-demo) staff authentication for the
/// Standalone Recording package. Calls the external READI validate endpoint with
/// the caller's <c>AuthToken</c> and maps the response to an
/// <see cref="AuthValidatorResult"/>.
///
/// Wiring:
///   • Endpoint:  <c>AuthValidation:Endpoints:ReadiValidateUrl</c>
///   • Mock mode: <c>AuthValidation:UseMock = true</c> returns <c>Mock:Readi</c>
///                without any HTTP call (used by the demo flows).
///   • Selected:  per-application <c>StaffAuthProvider = "READI"</c>, or globally
///                via the <c>FeatureFlags:UseReadiAuth</c> default (see AuthController).
///
/// Identity resolution order from the READI response:
///   1. JWT in the <c>X-Readi-Token</c> response header (sub / given_name /
///      family_name / email claims, sub may be a JSON array).
///   2. JSON body fields: sub | userId | id, given_name|firstName,
///      family_name|lastName, email.
///
/// Never throws — all failures are returned via <see cref="AuthValidatorResult.IsValid"/>.
/// The validator is resolved as a typed <see cref="HttpClient"/> (see
/// DependencyInjection.AddHttpClient&lt;ReadiAuthValidator&gt;), so connection
/// pooling and handler lifetime are managed by IHttpClientFactory.
/// </summary>
public sealed class ReadiAuthValidator : IAuthValidator
{
    private readonly HttpClient _http;
    private readonly AuthValidationSettings _settings;
    private readonly ILogger<ReadiAuthValidator> _logger;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public ReadiAuthValidator(
        HttpClient http,
        IOptions<AuthValidationSettings> settings,
        ILogger<ReadiAuthValidator> logger)
    {
        _http     = http;
        _settings = settings.Value;
        _logger   = logger;
    }

    public async Task<AuthValidatorResult> ValidateAsync(
        string authToken, CancellationToken ct = default)
    {
        if (_settings.UseMock)
        {
            _logger.LogWarning("[MOCK] READI validation bypassed — token-aware mock.");
            var mock = _settings.Mock.Readi;
            if (!mock.IsValid)
                return AuthValidatorResult.Fail(mock.Error ?? "Mock READI validation failed.");

            // Resolve a safe userId — never store a raw JWT blob or JSON string in ExternalUserId.
            // Priority: slug token (e.g. "alice-smith") → configured mock.UserId.
            string userId;
            if (string.IsNullOrWhiteSpace(authToken))
                userId = mock.UserId;
            else if (authToken.Length <= 80 &&
                     System.Text.RegularExpressions.Regex.IsMatch(
                         authToken, @"^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?$"))
                userId = authToken;          // safe slug — "alice-smith" etc.
            else
                userId = mock.UserId;        // JWT / JSON blob / garbage → ignore

            var (first, last)  = AnonymousAuthValidator.ParseDemoName(userId, mock.FirstName, mock.LastName);
            return AuthValidatorResult.Ok(userId, mock.Email, first, last);
        }

        var url = _settings.Endpoints.ReadiValidateUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogError("READI validate URL not configured.");
            return AuthValidatorResult.Fail("READI provider is not configured.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", authToken);
                request.Headers.TryAddWithoutValidation("Cookie", $"AuthToken={authToken}");
            }

            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("READI returned HTTP {Status}.", (int)response.StatusCode);
                return AuthValidatorResult.Fail(
                    $"READI validation rejected (HTTP {(int)response.StatusCode}).");
            }

            // 1) Prefer JWT in response header X-Readi-Token
            if (response.Headers.TryGetValues("X-Readi-Token", out var headerValues))
            {
                var raw = headerValues.FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        raw = raw["Bearer ".Length..].Trim();

                    var userId    = GetClaimFromJwtPayload(raw, "sub")
                                    ?? TryReadJwtClaim(raw, j => j.Subject);
                    var givenName = GetClaimFromJwtPayload(raw, "given_name")
                                    ?? TryReadJwtClaim(raw, j =>
                                        j.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value);
                    var familyName = GetClaimFromJwtPayload(raw, "family_name")
                                     ?? TryReadJwtClaim(raw, j =>
                                         j.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value);
                    var email     = GetClaimFromJwtPayload(raw, "email")
                                    ?? TryReadJwtClaim(raw, j =>
                                        j.Claims.FirstOrDefault(c => c.Type == "email")?.Value);

                    if (!string.IsNullOrEmpty(userId))
                        return AuthValidatorResult.Ok(userId, email, givenName, familyName);
                }
            }

            // 2) Fall back to JSON body
            var jsonString = await response.Content.ReadAsStringAsync(ct);
            var body = JsonSerializer.Deserialize<JsonElement>(jsonString, JsonOpts);

            var subFromBody = TryGetJsonString(body, "sub")
                              ?? TryGetJsonString(body, "userId")
                              ?? TryGetJsonString(body, "id");

            if (string.IsNullOrEmpty(subFromBody))
            {
                _logger.LogWarning("READI response contained no usable sub/userId.");
                return AuthValidatorResult.Fail("READI response missing user identifier.");
            }

            return AuthValidatorResult.Ok(
                subFromBody,
                TryGetJsonString(body, "email"),
                TryGetJsonString(body, "given_name") ?? TryGetJsonString(body, "firstName"),
                TryGetJsonString(body, "family_name") ?? TryGetJsonString(body, "lastName"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "READI validation threw an exception.");
            return AuthValidatorResult.Fail("READI validation failed due to an internal error.");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? TryGetJsonString(JsonElement el, string propertyName)
    {
        if (el.TryGetProperty(propertyName, out var prop))
            return prop.GetString();
        return null;
    }

    /// <summary>
    /// Decodes the JWT payload without signature verification and returns the
    /// named claim. Handles sub encoded as a JSON array (takes first element).
    /// </summary>
    private static string? GetClaimFromJwtPayload(string jwt, string claimName)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            var paddedPayload = parts[1].PadRight(
                parts[1].Length + (4 - parts[1].Length % 4) % 4, '=');
            var payloadBytes  = Convert.FromBase64String(
                paddedPayload.Replace('-', '+').Replace('_', '/'));
            using var doc = JsonDocument.Parse(payloadBytes);

            if (!doc.RootElement.TryGetProperty(claimName, out var el)) return null;

            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Array  => el.EnumerateArray().FirstOrDefault().GetString(),
                _                    => el.ToString()
            };
        }
        catch { return null; }
    }

    private static string? TryReadJwtClaim(
        string jwt, Func<JwtSecurityToken, string?> selector)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var token   = handler.ReadJwtToken(jwt);
            return selector(token);
        }
        catch { return null; }
    }
}
