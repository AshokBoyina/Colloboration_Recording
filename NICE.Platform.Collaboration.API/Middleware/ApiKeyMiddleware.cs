namespace NICE.Platform.Collaboration.API.Middleware;

using System.Security.Cryptography;
using System.Text;
using NICE.Platform.Collaboration.Application.Interfaces.Repositories;

/// <summary>
/// Validates the <c>X-Api-Key</c> header against the registered application store
/// for every protected route.
///
/// Skipped for:
///   /swagger/**          — API documentation
///   /api/v1/collaboration/auth/** — auth pre-flight (no key required yet)
///   /hubs/**             — SignalR (key validated at hub level)
///   /health              — health checks
///
/// The raw key is SHA-256 hashed before comparison — keys are never stored
/// or compared in plain text.
///
/// The resolved <see cref="IApplicationRepository"/> lookup is stubbed (Phase 1).
/// Full DB resolution is wired in Phase 2.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
{
    private const string ApiKeyHeader = "X-Api-Key";

    // Context item key — downstream code reads the resolved app from HttpContext.Items.
    public const string ResolvedAppKey = "ResolvedApplication";

    public async Task InvokeAsync(HttpContext context, IApplicationRepository appRepo)
    {
        // ── Skip list ─────────────────────────────────────────────────────────
        var path = context.Request.Path;
        if (path.StartsWithSegments("/swagger")                           ||
            path.StartsWithSegments("/api/v1/collaboration/auth")         ||
            path.StartsWithSegments("/api/v1/collaboration/recordings")   ||  // bearer-protected; browser can't easily add X-Api-Key
            path.StartsWithSegments("/api/v1/demo")                       ||  // demo/setup — no key needed
            path.StartsWithSegments("/hubs")                              ||
            path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        // ── Read header ───────────────────────────────────────────────────────
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var rawKey) ||
            string.IsNullOrWhiteSpace(rawKey))
        {
            logger.LogWarning("Request to {Path} rejected — missing {Header}.", path, ApiKeyHeader);
            context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"error\":\"Missing or empty X-Api-Key header.\"}");
            return;
        }

        // ── Hash and look up ──────────────────────────────────────────────────
        var hashedKey = ComputeSha256(rawKey!);

        // TODO (Phase 2): replace stub with real DB lookup:
        //   var app = await appRepo.GetByHashedKeyAsync(hashedKey, context.RequestAborted);
        //   if (app is null || !app.IsActive) { ... reject ... }
        //   context.Items[ResolvedAppKey] = app;

        // Phase 1 stub — accept any non-empty key, log for tracing.
        logger.LogDebug("X-Api-Key hash={Hash} accepted (stub — DB lookup pending).", hashedKey[..8]);

        await next(context);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeSha256(string input)
    {
        var bytes  = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
