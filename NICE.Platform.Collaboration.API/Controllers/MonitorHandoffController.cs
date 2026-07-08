namespace NICE.Platform.Collaboration.API.Controllers;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NICE.Platform.Collaboration.API.Services;

/// <summary>
/// Secure cross-tab launch handoff for the standalone monitor. Keeps the JWT out
/// of every URL by exchanging it (in an Authorization header) for a short-lived,
/// single-use opaque code that travels in the launch URL instead.
///
/// Flow:
///   1. Host app button click → POST monitor/handoff  (Authorization: Bearer &lt;JWT&gt;)
///      → returns { code, launchUrl }. Host opens launchUrl in a new tab.
///   2. Monitor /launch tab → POST monitor/handoff/redeem  { code }
///      → returns { sessionToken, user }. Monitor holds the JWT in memory only.
///   3. Monitor, just before opening SignalR → POST monitor/hub-ticket
///      (Authorization: Bearer &lt;sessionToken&gt;) → returns { ticket }. The ticket
///      (not the JWT) goes in the WebSocket URL; JwtBearer.OnMessageReceived redeems it.
///
/// ReadiAuthValidator runs only at step 1 (mint), and only when the supplied token
/// is an external READI token — via SignalRAccessTokenBridge.TryExchangeAsync. An
/// already-internal session JWT is used as-is.
/// </summary>
[ApiController]
[Route("api/v1/collaboration/monitor")]
public sealed class MonitorHandoffController(
    SignalRAccessTokenBridge          bridge,
    IOneTimeTicketService             tickets,
    IConfiguration                    configuration,
    ILogger<MonitorHandoffController> logger) : ControllerBase
{
    // 5 min so manual testing (Postman → browser copy/paste) is comfortable; the code is
    // single-use, so a longer window is still safe. The button flow redeems in < 1 s.
    private static readonly TimeSpan HandoffTtl   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HubTicketTtl = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Step 1 — mint a one-time launch code from the supervisor's token (in a header).
    /// AllowAnonymous because an external READI token wouldn't pass JwtBearer; it's
    /// validated here explicitly (internal JWT used as-is, READI exchanged).
    /// </summary>
    [AllowAnonymous]
    [HttpPost("handoff")]
    public async Task<IActionResult> Handoff(CancellationToken ct)
    {
        var token = ReadBearer() ?? Request.Headers["AuthToken"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized(new { error = "Missing supervisor token (Authorization: Bearer …)." });

        var appName = Request.Headers["X-Access-Key"].FirstOrDefault()
                      ?? Request.Query["app"].FirstOrDefault();

        // The monitor console only receives live sessions when its session role is
        // "StandaloneMonitor" (see CollaborationHub.OnConnectedAsync). Callers may override
        // to "Supervisor" via a UserType header/query if they want cross-app watching.
        var userType = Request.Headers["UserType"].FirstOrDefault()
                       ?? Request.Query["userType"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userType)) userType = "StandaloneMonitor";

        // Internal session JWT → use directly; external READI token → validate (runs
        // ReadiAuthValidator) and issue a StandaloneMonitor session. requireValidation:true
        // rejects an invalid token instead of falling back — real auth for a supervisor session.
        string sessionToken;
        if (bridge.IsInternalSessionToken(token))
        {
            sessionToken = token;
        }
        else
        {
            var exchanged = await bridge.TryExchangeAsync(
                token, appName, ct, userTypeOverride: userType, requireValidation: true);
            if (string.IsNullOrWhiteSpace(exchanged))
                return Unauthorized(new { error = "READI token could not be validated." });
            sessionToken = exchanged;
        }

        // Pull a display summary from the internal JWT (never trust the client for this).
        JwtSecurityToken jwt;
        try { jwt = new JwtSecurityTokenHandler().ReadJwtToken(sessionToken); }
        catch { return Unauthorized(new { error = "Resolved session token was not a readable JWT." }); }

        string Claim(params string[] names) =>
            names.Select(n => jwt.Claims.FirstOrDefault(c => c.Type == n)?.Value)
                 .FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;

        var displayName = ($"{Claim(JwtRegisteredClaimNames.GivenName, "given_name")} " +
                           $"{Claim(JwtRegisteredClaimNames.FamilyName, "family_name")}").Trim();

        var payload = new HandoffPayload(
            SessionToken:    sessionToken,
            UserId:          Claim(JwtRegisteredClaimNames.Sub, "sub", ClaimTypes.NameIdentifier),
            DisplayName:     displayName,
            UserType:        Claim(ClaimTypes.Role, "role"),
            ApplicationId:   Claim("app"),
            ApplicationName: appName ?? string.Empty);

        var code = tickets.CreateHandoff(payload, HandoffTtl);

        // Route through the app ROOT (/?code=) rather than a deep path (/launch?code=):
        // "/" always serves index.html as the default document, so the launch works even
        // where SPA deep-link fallback isn't configured. Login.razor redeems the code at root.
        var monitorBase = (configuration["Monitor:BaseUrl"] ?? string.Empty).TrimEnd('/');
        var launchUrl   = string.IsNullOrEmpty(monitorBase)
            ? null
            : $"{monitorBase}/?code={Uri.EscapeDataString(code)}";

        logger.LogInformation(
            "Monitor handoff minted for user {UserId} (app '{App}'), expires in {Ttl}s.",
            payload.UserId, payload.ApplicationName, (int)HandoffTtl.TotalSeconds);

        return Ok(new { code, launchUrl, expiresInSeconds = (int)HandoffTtl.TotalSeconds });
    }

    /// <summary>Step 2 — the monitor tab redeems the code for the session JWT (single use).</summary>
    [AllowAnonymous]
    [HttpPost("handoff/redeem")]
    public IActionResult Redeem([FromBody] RedeemRequest? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Code) ||
            !tickets.TryRedeemHandoff(body.Code, out var p) || p is null)
            return Unauthorized(new { error = "Invalid, expired, or already-used launch code." });

        return Ok(new
        {
            sessionToken = p.SessionToken,
            user = new
            {
                userId          = p.UserId,
                displayName     = p.DisplayName,
                userType        = p.UserType,
                applicationId   = p.ApplicationId,
                applicationName = p.ApplicationName
            }
        });
    }

    /// <summary>
    /// Step 3 — mint a one-time hub ticket for the authenticated caller. Requires the
    /// internal session JWT in the Authorization header ([Authorize]); the returned
    /// ticket is what goes in the SignalR WebSocket URL instead of the JWT.
    /// </summary>
    [Authorize]
    [HttpPost("hub-ticket")]
    public IActionResult HubTicket()
    {
        var ticket = tickets.CreateHubTicket(User.Claims, HubTicketTtl);
        return Ok(new { ticket, expiresInSeconds = (int)HubTicketTtl.TotalSeconds });
    }

    // ── helpers ────────────────────────────────────────────────────────────────
    private string? ReadBearer()
    {
        var h = Request.Headers.Authorization.FirstOrDefault();
        return h is not null && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? h["Bearer ".Length..].Trim()
            : null;
    }

    public sealed record RedeemRequest(string Code);
}
