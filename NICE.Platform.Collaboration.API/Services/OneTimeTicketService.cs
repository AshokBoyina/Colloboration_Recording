namespace NICE.Platform.Collaboration.API.Services;

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;

/// <summary>
/// Data carried by a launch <b>handoff</b> code: the internal session JWT plus a
/// display summary the monitor shows after redeeming. Never exposed in a URL —
/// only the opaque code is, and only for a few seconds, single-use.
/// </summary>
public sealed record HandoffPayload(
    string SessionToken,
    string UserId,
    string DisplayName,
    string UserType,
    string ApplicationId,
    string ApplicationName);

/// <summary>
/// Issues and redeems short-lived, single-use, opaque tickets used to keep JWTs
/// out of URLs during two hops:
///
///   • <b>Handoff code</b> — minted when a host app clicks "open monitor"; carries the
///     supervisor's internal session JWT. The new monitor tab redeems it (once) to
///     obtain the JWT in a response body instead of the launch URL.
///
///   • <b>Hub ticket</b> — minted by the monitor just before it opens the SignalR
///     connection; carries the caller's claims. Redeemed inside the JwtBearer
///     OnMessageReceived hook so the WebSocket URL never contains the JWT.
///
/// Tickets are opaque (256-bit random, url-safe, no dots so they can never be
/// mistaken for a JWT), single-use (removed on redeem), and expire after their TTL.
/// </summary>
public interface IOneTimeTicketService
{
    /// <summary>Stores a handoff payload and returns its opaque code.</summary>
    string CreateHandoff(HandoffPayload payload, TimeSpan ttl);

    /// <summary>Redeems (and consumes) a handoff code. False if unknown/expired/used.</summary>
    bool TryRedeemHandoff(string code, out HandoffPayload? payload);

    /// <summary>Snapshots the given claims behind an opaque hub ticket.</summary>
    string CreateHubTicket(IEnumerable<Claim> claims, TimeSpan ttl);

    /// <summary>Redeems (and consumes) a hub ticket into an authenticated principal.</summary>
    bool TryRedeemHubTicket(string ticket, out ClaimsPrincipal? principal);
}

/// <inheritdoc />
public sealed class OneTimeTicketService : IOneTimeTicketService
{
    private sealed record Entry(object Payload, DateTimeOffset ExpiresAt);
    private sealed record ClaimLite(string Type, string Value);

    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private long _lastSweepTicks = DateTimeOffset.UtcNow.UtcTicks;

    public string CreateHandoff(HandoffPayload payload, TimeSpan ttl) => Add(payload, ttl);

    public bool TryRedeemHandoff(string code, out HandoffPayload? payload)
    {
        payload = null;
        if (TryTake(code, out var obj) && obj is HandoffPayload p) { payload = p; return true; }
        return false;
    }

    public string CreateHubTicket(IEnumerable<Claim> claims, TimeSpan ttl)
    {
        var snapshot = claims.Select(c => new ClaimLite(c.Type, c.Value)).ToArray();
        return Add(snapshot, ttl);
    }

    public bool TryRedeemHubTicket(string ticket, out ClaimsPrincipal? principal)
    {
        principal = null;
        if (TryTake(ticket, out var obj) && obj is ClaimLite[] lite)
        {
            var claims = lite.Select(c => new Claim(c.Type, c.Value));
            // A non-null authenticationType makes IsAuthenticated true so [Authorize] passes.
            var identity = new ClaimsIdentity(claims, "HubTicket", ClaimTypes.NameIdentifier, ClaimTypes.Role);
            principal = new ClaimsPrincipal(identity);
            return true;
        }
        return false;
    }

    // ── internals ────────────────────────────────────────────────────────────
    private string Add(object payload, TimeSpan ttl)
    {
        Sweep();
        // 256-bit url-safe token, no '.' so it can never be read as a JWT.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _store[token] = new Entry(payload, DateTimeOffset.UtcNow.Add(ttl));
        return token;
    }

    private bool TryTake(string token, out object? payload)
    {
        payload = null;
        if (string.IsNullOrEmpty(token)) return false;
        if (_store.TryRemove(token, out var entry))   // single-use: gone on first read
        {
            if (entry.ExpiresAt >= DateTimeOffset.UtcNow) { payload = entry.Payload; return true; }
        }
        return false;
    }

    // Opportunistic cleanup of expired entries — at most once every 30s.
    private void Sweep()
    {
        var now      = DateTimeOffset.UtcNow;
        var lastTicks = Interlocked.Read(ref _lastSweepTicks);
        if ((now.UtcTicks - lastTicks) < TimeSpan.FromSeconds(30).Ticks) return;
        if (Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, lastTicks) != lastTicks) return;

        foreach (var kvp in _store)
            if (kvp.Value.ExpiresAt < now)
                _store.TryRemove(kvp.Key, out _);
    }
}
