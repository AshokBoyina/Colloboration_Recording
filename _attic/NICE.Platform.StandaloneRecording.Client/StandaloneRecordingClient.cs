namespace NICE.Platform.StandaloneRecording.Client;

using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

/// <summary>
/// Lightweight HTTP client for host applications to interact with the
/// Standalone Recording API — minting tokens, checking health, and
/// building the deep-link URL that opens the recording UI.
///
/// Inject via DI after calling <c>services.AddStandaloneRecordingClient()</c>.
/// </summary>
public sealed class StandaloneRecordingClient
{
    internal const string HttpClientName = "StandaloneRecording";

    private readonly HttpClient _http;
    private readonly StandaloneRecordingConfig _config;

    public StandaloneRecordingClient(
        IHttpClientFactory factory,
        IOptions<StandaloneRecordingConfig> options)
    {
        _http   = factory.CreateClient(HttpClientName);
        _config = options.Value;
    }

    // ── Token minting ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mints a LOCAL_JWT demo token for the given user name + role by calling
    /// POST /api/v1/demo/mint-token on the Standalone Recording API.
    ///
    /// If <see cref="StandaloneRecordingConfig.AccessToken"/> is already set,
    /// returns it directly without making an HTTP call.
    /// </summary>
    public async Task<string> GetOrMintTokenAsync(
        string? name = null, string? role = null, CancellationToken ct = default)
    {
        // Pre-supplied token takes priority
        if (!string.IsNullOrWhiteSpace(_config.AccessToken))
            return _config.AccessToken;

        var userName = (name?.Trim() ?? _config.UserName?.Trim() ?? "Recording User");
        var userRole = role?.Trim() ?? _config.UserRole ?? "StandAlone";

        // LocalJwtAuthValidator requires two-word name (given + family claims)
        if (!userName.Contains(' ')) userName += " User";

        var url = $"api/v1/demo/mint-token?name={Uri.EscapeDataString(userName)}&role={Uri.EscapeDataString(userRole)}";
        var response = await _http.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();

        var body    = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("token").GetString()
               ?? throw new InvalidOperationException("mint-token response missing 'token' field.");
    }

    // ── Validate-first token exchange ─────────────────────────────────────────

    /// <summary>
    /// Exchanges an external token (e.g. a READI token) for an internal session token
    /// via POST /api/v1/collaboration/auth/validate. Use this when the host is handed an
    /// external token: the returned session token authenticates the recording hubs and
    /// chunk uploads directly (validate-first — no server-side token bridge needed).
    /// Demo/LOCAL_JWT tokens are already internal and don't need this.
    /// </summary>
    public async Task<string> ExchangeForSessionTokenAsync(
        string externalToken, string userType = "StandAlone", CancellationToken ct = default)
    {
        // The named HttpClient already sends X-Api-Key and X-Access-Key; add the
        // per-request auth headers /auth/validate expects.
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/collaboration/auth/validate");
        req.Headers.TryAddWithoutValidation("AuthToken", externalToken);
        req.Headers.TryAddWithoutValidation("UserType", userType);

        using var response = await _http.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        foreach (var name in new[] { "sessionToken", "SessionToken" })
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString()!;

        throw new InvalidOperationException("auth/validate response did not contain a session token.");
    }

    // ── Delete recordings ─────────────────────────────────────────────────────

    /// <summary>
    /// Deletes all saved recordings for a collaboration via
    /// DELETE /api/v1/collaboration/recordings/by-collaboration/{collaborationId}.
    /// Requires an internal session token (Authorization: Bearer). Returns the raw
    /// JSON result body ({ deleted, skippedInProgress, alreadyDeleted, notes }).
    /// </summary>
    public async Task<string> DeleteRecordingsAsync(
        Guid collaborationId, string sessionToken, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Delete,
            $"api/v1/collaboration/recordings/by-collaboration/{collaborationId}");
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionToken);

        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Delete failed (HTTP {(int)response.StatusCode}): {body}");

        return body;
    }

    // ── Secure monitor launch (one-time code, JWT never in a URL) ──────────────

    /// <summary>
    /// Securely launches the standalone Monitor. Mints a StandaloneMonitor token, then
    /// exchanges it — in an <c>Authorization: Bearer</c> header, never a URL — for a
    /// short-lived, single-use launch code via
    /// <c>POST /api/v1/collaboration/monitor/handoff</c>, and returns the resulting
    /// launch URL (<c>…/launch?code=&lt;opaque&gt;</c>).
    ///
    /// Open the returned URL in a new browser tab (e.g. <c>window.open</c>). Only the
    /// opaque, single-use code travels in the URL; the JWT is redeemed by the monitor
    /// via a POST body and held in memory. This replaces the insecure
    /// <see cref="BuildMonitorUrlAsync"/> (token-in-URL) path.
    /// </summary>
    /// <param name="name">Display name used when minting the monitor token.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> CreateMonitorLaunchUrlAsync(
        string? name = null, CancellationToken ct = default)
    {
        var token = await GetOrMintTokenAsync(name, "StandaloneMonitor", ct);

        // The named HttpClient already sends X-Api-Key / X-Access-Key (used as the app name).
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/collaboration/monitor/handoff");
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(req, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Monitor handoff failed (HTTP {(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("launchUrl", out var lu) && lu.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(lu.GetString()))
            return lu.GetString()!;

        // Server didn't have Monitor:BaseUrl configured — surface the code so the caller can build it.
        var code = root.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
        throw new InvalidOperationException(
            code is null
                ? "Monitor handoff response contained neither launchUrl nor code."
                : $"Handoff returned a code but no launchUrl — set 'Monitor:BaseUrl' in the API " +
                  $"appsettings, or open {{monitorBase}}/launch?code={code} yourself.");
    }

    // ── Deep-link URL builders ────────────────────────────────────────────────

    /// <summary>
    /// Returns the URL to open recorder.html, served directly from the API's wwwroot.
    /// Host app opens this URL in a WebView2 control or browser tab.
    /// The JWT token is passed as a query parameter — the page reads it on load.
    /// </summary>
    /// <param name="name">Display name used when minting a token (ignored if a token is pre-configured).</param>
    /// <param name="sessionId">
    /// Optional — the host application's own session / ticket / interaction GUID.
    /// When provided the server uses this as the Collaboration ID for the recording session,
    /// so recordings are immediately correlated to your existing records without a separate lookup.
    /// Pass null to let the server generate the ID automatically.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> BuildRecorderUrlAsync(
        string?           name      = null,
        Guid?             sessionId = null,
        CancellationToken ct        = default)
    {
        var token = await GetOrMintTokenAsync(name, "StandAlone", ct);
        return BuildUrl("/recorder.html", token, sessionId);
    }

    /// <summary>
    /// Returns the URL to open monitor.html, served directly from the API's wwwroot.
    /// Supervisor opens this to watch live screen sessions in real time.
    /// </summary>
    public async Task<string> BuildMonitorUrlAsync(
        string?           name      = null,
        CancellationToken ct        = default)
    {
        var token = await GetOrMintTokenAsync(name, "StandaloneMonitor", ct);
        return BuildUrl("/monitor.html", token);
    }

    // ── Health check ──────────────────────────────────────────────────────────

    /// <summary>Pings GET /health on the Standalone Recording API.</summary>
    public async Task<bool> IsApiHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var r = await _http.GetAsync("health", ct);
            return r.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a URL pointing at a static page served by the API itself.
    /// Since recorder.html / monitor.html are in the API's wwwroot, the base
    /// is just ApiBaseUrl — no separate ChatUI URL needed.
    /// </summary>
    private string BuildUrl(string page, string token, Guid? sessionId = null)
    {
        var apiBase = _config.ApiBaseUrl.TrimEnd('/');
        var tok     = Uri.EscapeDataString(token);
        var url     = $"{apiBase}{page}?token={tok}";
        if (sessionId.HasValue)
            url += $"&sessionId={sessionId.Value}";
        return url;
    }
}
