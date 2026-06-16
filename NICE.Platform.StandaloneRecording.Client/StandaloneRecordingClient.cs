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
