namespace NICE.Platform.Collaboration.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Demo / setup controller — no authentication required.
///
/// Provides endpoints to seed the three reference applications into the real
/// SQL database and to manage demo users (agents, supervisors) per application
/// so that any stakeholder can run a credible end-to-end demo without needing
/// to stand up real READI / NICE identity providers.
///
/// All tokens used here are the <c>ExternalUserId</c> stored in the DB. The
/// mock auth validators parse the token back into a display name at login time.
///
/// Routes:
///   GET  /api/v1/demo/status
///   POST /api/v1/demo/seed
///   GET  /api/v1/demo/apps
///   GET  /api/v1/demo/users/{appId}
///   POST /api/v1/demo/users
///   DELETE /api/v1/demo/users/{userId}/{appId}
///   POST /api/v1/demo/recordings/upload  — save screen recording blob to local disk
/// </summary>
[ApiController]
[Route("api/v1/demo")]
[AllowAnonymous]
public class DemoController(CollaborationDbContext db, IConfiguration config) : ControllerBase
{
    // ── Deterministic seed GUIDs — never change these ─────────────────────────
    private static readonly Dictionary<string, SeedApp> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SurveyPortal"] = new(
            new Guid("00000000-0000-0000-0001-000000000001"),
            "ANON", "survey-portal-key"),

        ["Readi"] = new(
            new Guid("00000000-0000-0000-0001-000000000002"),
            "READI", "readi-key"),

        ["NicePortal"] = new(
            new Guid("00000000-0000-0000-0001-000000000003"),
            "NICE", "nice-portal-key"),
    };

    // ── GET /api/v1/demo/ping ── reachability check (no DB) ──────────────────
    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, utc = DateTime.UtcNow });

    // ── GET /api/v1/demo/status ───────────────────────────────────────────────
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        try
        {
            var count = await db.Applications.CountAsync(ct);
            return Ok(new { seeded = count > 0, appCount = count, dbOk = true });
        }
        catch (Exception ex)
        {
            // DB not ready (tables missing, wrong connection string, etc.)
            return Ok(new { seeded = false, appCount = 0, dbOk = false,
                            dbError = ex.Message });
        }
    }

    // ── GET /api/v1/demo/apps/static ── hardcoded list, no DB needed ─────────
    [HttpGet("apps/static")]
    public IActionResult StaticApps() =>
        Ok(Seeds.Select(kv => new
        {
            Id           = kv.Value.Id,
            Name         = kv.Key,
            AuthProvider = kv.Value.AuthProvider,
            ApiKey       = kv.Value.ApiKey
        }).ToList());

    // ── POST /api/v1/demo/seed ────────────────────────────────────────────────
    [HttpPost("seed")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        try
        {
            var now   = DateTime.UtcNow;
            var added = 0;

            foreach (var (name, seed) in Seeds)
            {
                var existing = await db.Applications
                    .FirstOrDefaultAsync(a => a.Id == seed.Id, ct);

                if (existing is null)
                {
                    await db.Applications.AddAsync(new CollaborationApplication
                    {
                        Id                = seed.Id,
                        Name              = name,
                        HashedApiKey      = seed.ApiKey,
                        AuthProvider      = seed.AuthProvider,
                        MaxAgentsOnline   = 20,
                        MaxUsersOnline    = 100,
                        BlobContainerPath = name.ToLowerInvariant(),
                        IsActive          = true,
                        CreatedAt         = now
                    }, ct);
                    added++;
                }
            }

            await db.SaveChangesAsync(ct);
            return Ok(new { message = $"Seeded {added} application(s). Already existing ones were skipped." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "DB error — make sure the EF Core migration has been applied.",
                detail = ex.Message
            });
        }
    }

    // ── GET /api/v1/demo/apps ─────────────────────────────────────────────────
    [HttpGet("apps")]
    public async Task<IActionResult> GetApps(CancellationToken ct)
    {
        var apps = await db.Applications
            .AsNoTracking()
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.AuthProvider,
                ApiKey         = a.HashedApiKey,
                a.IsActive,
                AgentCount     = db.ApplicationUsers.Count(au =>
                    au.ApplicationId == a.Id && au.Role == "Agent" && au.IsActive),
                SupervisorCount = db.ApplicationUsers.Count(au =>
                    au.ApplicationId == a.Id && au.Role == "Supervisor" && au.IsActive),
            })
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        return Ok(apps);
    }

    // ── GET /api/v1/demo/users/{appId} ────────────────────────────────────────
    [HttpGet("users/{appId:guid}")]
    public async Task<IActionResult> GetUsers(Guid appId, CancellationToken ct)
    {
        var users = await db.ApplicationUsers
            .AsNoTracking()
            .Where(au => au.ApplicationId == appId && au.IsActive)
            .Join(db.Users,
                  au => au.UserId,
                  u  => u.Id,
                  (au, u) => new
                  {
                      u.Id,
                      Token      = u.ExternalUserId,   // the login token
                      u.FirstName,
                      u.LastName,
                      u.Email,
                      au.Role,
                      au.AddedAt
                  })
            .OrderBy(u => u.Role)
            .ThenBy(u => u.FirstName)
            .ToListAsync(ct);

        return Ok(users);
    }

    // ── POST /api/v1/demo/users ───────────────────────────────────────────────
    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(
        [FromBody] DemoCreateUserRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required." });

        if (request.ApplicationId == Guid.Empty)
            return BadRequest(new { error = "ApplicationId is required." });

        if (string.IsNullOrWhiteSpace(request.Role))
            return BadRequest(new { error = "Role is required (Agent | Supervisor | External | Internal)." });

        var now = DateTime.UtcNow;

        // Generate slug token from name: "Alice Smith" → "alice-smith"
        var token = !string.IsNullOrWhiteSpace(request.Token)
            ? request.Token.Trim()
            : request.Name.Trim().ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace(".", "");

        var nameParts = request.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts[0];
        var lastName  = nameParts.Length > 1 ? nameParts[1] : string.Empty;
        var email     = request.Email ?? $"{token}@demo.nice.com";

        // Upsert CollaborationUser by ExternalUserId (= token)
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.ExternalUserId == token, ct);

        if (user is null)
        {
            user = new CollaborationUser
            {
                Id             = Guid.NewGuid(),
                ExternalUserId = token,
                FirstName      = firstName,
                LastName       = lastName,
                Email          = email,
                IsActive       = true,
                CreatedAt      = now
            };
            await db.Users.AddAsync(user, ct);
        }
        else
        {
            user.FirstName = firstName;
            user.LastName  = lastName;
            user.Email     = email;
            user.IsActive  = true;
        }

        // Upsert CollaborationApplicationUser
        var appUser = await db.ApplicationUsers.FirstOrDefaultAsync(
            au => au.UserId == user.Id && au.ApplicationId == request.ApplicationId, ct);

        if (appUser is null)
        {
            await db.ApplicationUsers.AddAsync(new CollaborationApplicationUser
            {
                ApplicationId = request.ApplicationId,
                UserId        = user.Id,
                Role          = request.Role,
                IsActive      = true,
                AddedAt       = now
            }, ct);
        }
        else
        {
            appUser.Role     = request.Role;
            appUser.IsActive = true;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            user.Id,
            Token     = token,
            user.FirstName,
            user.LastName,
            user.Email,
            request.Role
        });
    }


    // ── DELETE /api/v1/demo/users/{userId}/{appId} ─────────────────────────
    [HttpDelete("users/{userId:guid}/{appId:guid}")]
    public async Task<IActionResult> RemoveUser(Guid userId, Guid appId, CancellationToken ct)
    {
        var appUser = await db.ApplicationUsers.FirstOrDefaultAsync(
            au => au.UserId == userId && au.ApplicationId == appId, ct);

        if (appUser is null)
            return NotFound(new { error = "User not found in this application." });

        appUser.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "User deactivated." });
    }

    // ── GET /api/v1/demo/channels/{appId} ─────────────────────────────────
    [HttpGet("channels/{appId:guid}")]
    public async Task<IActionResult> GetActiveChannels(Guid appId, CancellationToken ct)
    {
        var sessions = await db.CurrentSessions
            .AsNoTracking()
            .Where(s => s.ApplicationId          == appId
                     && s.UserType               == "Internal"
                     && s.CurrentCollaborationId.HasValue)
            .Join(db.Users,
                  s => s.UserId,
                  u => u.Id,
                  (s, u) => new
                  {
                      ChannelId   = s.CurrentCollaborationId!.Value,
                      DisplayName = (u.FirstName + " " + u.LastName).Trim()
                  })
            .ToListAsync(ct);

        var channels = sessions
            .GroupBy(x => x.ChannelId)
            .Select(g => new
            {
                ChannelId    = g.Key.ToString(),
                Participants = g.Select(x => x.DisplayName).Distinct().ToList()
            })
            .OrderBy(c => c.ChannelId)
            .ToList();

        return Ok(channels);
    }

    // ── GET /api/v1/demo/online-users/{appId} ─────────────────────────────
    /// <summary>
    /// Returns all non-External users currently connected to the hub for the given application.
    /// Backed by CurrentSessions — a row exists only while the SignalR connection is active.
    /// </summary>
    [HttpGet("online-users/{appId:guid}")]
    public async Task<IActionResult> GetOnlineUsers(Guid appId, CancellationToken ct)
    {
        var online = await db.CurrentSessions
            .AsNoTracking()
            .Where(s => s.ApplicationId == appId
                     && s.UserType      != "External")
            .Join(db.Users,
                  s => s.UserId,
                  u => u.Id,
                  (s, u) => new
                  {
                      UserId          = u.Id,
                      DisplayName     = (u.FirstName + " " + u.LastName).Trim(),
                      UserType        = s.UserType,
                      ConnectedAt     = s.ConnectedAt,
                      ApplicationName = string.Empty
                  })
            .OrderBy(x => x.UserType)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);

        return Ok(online);
    }

    // ── GET /api/v1/demo/online-users/internal ────────────────────────────
    /// <summary>
    /// Returns ALL non-External users connected across every application — used by the
    /// Internal Chat to show a global staff directory regardless of which app each
    /// person logged into.
    /// </summary>
    [HttpGet("online-users/internal")]
    public async Task<IActionResult> GetAllInternalOnlineUsers(CancellationToken ct)
    {
        var online = await db.CurrentSessions
            .AsNoTracking()
            .Include(s => s.Application)
            .Include(s => s.User)
            .Where(s => s.UserType != "External")
            .Select(s => new
            {
                UserId          = s.User.Id,
                DisplayName     = (s.User.FirstName + " " + s.User.LastName).Trim(),
                UserType        = s.UserType,
                ConnectedAt     = s.ConnectedAt,
                ApplicationName = s.Application.Name
            })
            .OrderBy(x => x.ApplicationName)
            .ThenBy(x => x.UserType)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(ct);

        return Ok(online);
    }

    // ── POST /api/v1/demo/mint-token ─────────────────────────────────────────
    /// <summary>
    /// Mints a LOCAL_JWT token signed with the configured secret.
    /// The token includes all claims the SignalR hubs require (sub/GUID, app, role, sid).
    /// For StandAlone / StandaloneMonitor roles the Readi application is auto-seeded and
    /// the user is auto-upserted in the DB, so no separate /demo/seed call is needed.
    ///
    /// Query params:
    ///   name      — full name, e.g. "Alice Smith"  (required)
    ///   role      — Agent | Supervisor | Internal | StandAlone | StandaloneMonitor | External  (required)
    ///   appId     — applicationId GUID (optional; defaults to Readi for StandAlone/StandaloneMonitor roles)
    ///   sub       — stable user GUID (optional; derived deterministically from name if omitted)
    ///   email     — email address (optional)
    ///   expiryMin — token lifetime in minutes, default 480 (8 hours)
    /// </summary>
    [HttpPost("mint-token")]
    public async Task<IActionResult> MintToken(
        [FromQuery] string  name,
        [FromQuery] string  role,
        [FromQuery] string? appId     = null,
        [FromQuery] string? sub       = null,
        [FromQuery] string? email     = null,
        [FromQuery] int     expiryMin = 480,
        CancellationToken   ct        = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "name is required (e.g. ?name=Alice+Smith)." });

        if (string.IsNullOrWhiteSpace(role))
            return BadRequest(new { error = "role is required (Agent | Supervisor | StandAlone | StandaloneMonitor | External)." });

        // Read the LocalJwt secret from configuration
        var secret = config["AuthValidation:LocalJwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            return StatusCode(500, new
            {
                error = "AuthValidation:LocalJwt:Secret is not configured or too short (< 32 chars). " +
                        "Add it to appsettings.json to use LOCAL_JWT mode."
            });

        var issuer   = config["AuthValidation:LocalJwt:Issuer"]   ?? "NICE.Collaboration.Local";
        var audience = config["AuthValidation:LocalJwt:Audience"] ?? "NICE.Collaboration.Api";

        var nameParts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts[0];
        var lastName  = nameParts.Length > 1 ? nameParts[1] : "User";

        // Name slug used as ExternalUserId in the DB
        var slug = name.Trim().ToLowerInvariant().Replace(" ", "-").Replace("'", "");

        // sub must be a GUID so CollaborationHub.CurrentUserId can parse it.
        // If caller supplies a valid GUID, use it; otherwise derive a deterministic one from the slug.
        Guid userGuid;
        if (!string.IsNullOrWhiteSpace(sub) && Guid.TryParse(sub, out var parsedSub))
            userGuid = parsedSub;
        else
            userGuid = SlugToGuid(!string.IsNullOrWhiteSpace(sub) ? sub!.Trim() : slug);

        // Resolve applicationId:
        //   • explicit appId param wins
        //   • StandAlone / StandaloneMonitor default to the Readi seed app
        //   • all other roles leave app empty (token still valid, hub rejects if app required)
        Guid resolvedAppId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(appId) && Guid.TryParse(appId, out var parsedApp))
            resolvedAppId = parsedApp;
        else if (role.Equals("StandAlone",       StringComparison.OrdinalIgnoreCase) ||
                 role.Equals("StandaloneMonitor", StringComparison.OrdinalIgnoreCase))
            resolvedAppId = Seeds["Readi"].Id;   // 00000000-0000-0000-0001-000000000002

        // ── Auto-provision DB rows so hub FK constraints are satisfied ────────────
        // This means callers of mint-token never need to call /demo/seed separately.
        // WARNING: if DB is unavailable (missing migrations, LocalDB not started, etc.)
        // we still return the token — the hub will reject the connection at connect time.
        // The warning is returned in the response body (not a header) to avoid Kestrel
        // rejecting non-ASCII characters in HTTP response headers.
        string? dbWarning = null;
        try
        {
            if (resolvedAppId != Guid.Empty)
            {
                // Ensure the application row exists.
                // Use explicit object[] to avoid C# collection-expression overload ambiguity.
                var existingApp = await db.Applications.FindAsync(
                    new object[] { resolvedAppId }, ct);
                if (existingApp is null)
                {
                    var kv = Seeds.FirstOrDefault(x => x.Value.Id == resolvedAppId);
                    if (kv.Key is not null)
                    {
                        await db.Applications.AddAsync(new CollaborationApplication
                        {
                            Id                = resolvedAppId,
                            Name              = kv.Key,
                            HashedApiKey      = kv.Value.ApiKey,
                            AuthProvider      = kv.Value.AuthProvider,
                            MaxAgentsOnline   = 20,
                            MaxUsersOnline    = 100,
                            BlobContainerPath = kv.Key.ToLowerInvariant(),
                            IsActive          = true,
                            CreatedAt         = DateTime.UtcNow
                        }, ct);
                        await db.SaveChangesAsync(ct);
                    }
                }
            }

            // Ensure the user row exists (needed by CurrentSessions FK -> Users)
            var existingUser = await db.Users.FindAsync(
                new object[] { userGuid }, ct);
            if (existingUser is null)
            {
                await db.Users.AddAsync(new CollaborationUser
                {
                    Id             = userGuid,
                    ExternalUserId = slug,
                    FirstName      = firstName,
                    LastName       = lastName,
                    Email          = email ?? $"{slug}@standalone.local",
                    IsActive       = true,
                    CreatedAt      = DateTime.UtcNow
                }, ct);
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: DB might not have migrations applied yet.
            // Capture a plain-ASCII warning that will be included in the JSON response body.
            // DO NOT write to Response.Headers here -- Kestrel rejects non-ASCII characters
            // in header values and would throw, causing a secondary 500 inside this catch block.
            dbWarning = $"DB auto-provision failed ({ex.GetType().Name}): {ex.Message} " +
                        "-- Apply EF Core migrations (dotnet ef database update) and restart the API.";
        }

        // ── Build JWT ─────────────────────────────────────────────────────────────
        var now       = DateTime.UtcNow;
        var expiry    = now.AddMinutes(expiryMin);
        var sessionId = Guid.NewGuid();

        // JwtSecurityTokenHandler maps ClaimTypes.Role → "role" in the JWT payload,
        // and maps "sub" → ClaimTypes.NameIdentifier on inbound validation.
        // Both hubs fall back to checking "role"/"sub" as short-form claim types too.
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,        userGuid.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti,        Guid.NewGuid().ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.GivenName,  firstName),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.FamilyName, lastName),
            new(System.Security.Claims.ClaimTypes.Role,                              role),
            new("sid",                                                                sessionId.ToString()),
        };

        if (resolvedAppId != Guid.Empty)
            claims.Add(new("app", resolvedAppId.ToString()));

        if (!string.IsNullOrWhiteSpace(email))
            claims.Add(new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, email));

        var key   = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(secret));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
                        key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          now,
            expires:            expiry,
            signingCredentials: creds);

        var tokenString = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(jwt);

        return Ok(new
        {
            token     = tokenString,
            subject   = userGuid.ToString(),
            firstName,
            lastName,
            role,
            appId     = resolvedAppId != Guid.Empty ? resolvedAppId.ToString() : (string?)null,
            email,
            issuedAt  = now,
            expiresAt = expiry,
            usage     = $"Open /recorder.html?token={Uri.EscapeDataString(tokenString)} to start a recording session.",
            // null when DB is healthy; populated when DB is unreachable or migrations missing.
            // The token is still valid -- fix the DB issue and reconnect.
            dbWarning
        });
    }

    /// <summary>
    /// Derives a stable, deterministic GUID from a name slug so the same user
    /// always gets the same DB row across page reloads and API restarts.
    /// Uses MD5 (crypto quality not required here — just stable hashing).
    /// </summary>
    private static Guid SlugToGuid(string slug)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("nice-standalone-user:" + slug));
        return new Guid(hash);
    }

    // ── Private helpers ───────────────────────────────────────────────────
    // ── POST /api/v1/demo/recordings/upload ──────────────────────────────────
    /// <summary>
    /// Receives a WebM screen-recording blob from the agent/supervisor browser
    /// and saves it to the local "Recordings" folder inside the API's content root.
    /// Returns { fileName, path, sizeBytes } so the JS can show a success toast.
    /// </summary>
    [HttpPost("recordings/upload")]
    [RequestSizeLimit(500 * 1024 * 1024)]   // 500 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    public async Task<IActionResult> UploadRecording(
        IFormFile file,
        [FromForm] string? collaborationId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file received." });

        // Read path from appsettings.json → RecordingStorage:RecordingsPath
        var recordingsRoot = config["RecordingStorage:RecordingsPath"]
                             ?? Path.Combine(AppContext.BaseDirectory, "Recordings");
        var dateFolder = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var saveDir    = Path.Combine(recordingsRoot, dateFolder);
        Directory.CreateDirectory(saveDir);

        // Build a unique file name: recording-<collab8>-<timestamp>.mp4
        var collabPart = !string.IsNullOrEmpty(collaborationId) && collaborationId.Length >= 8
            ? collaborationId[..8]
            : "demo";
        var timestamp  = DateTime.UtcNow.ToString("HHmmss");
        var ext        = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".mp4";
        var fileName   = $"recording-{collabPart}-{timestamp}{ext}";
        var fullPath   = Path.Combine(saveDir, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream, ct);

        return Ok(new
        {
            fileName,
            path      = fullPath,
            sizeBytes = file.Length,
            savedAt   = DateTime.UtcNow
        });
    }

    // ── POST /api/v1/demo/cleanup-stale-sessions ─────────────────────────────
    /// <summary>
    /// Marks all never-ended collaborations as Abandoned.
    /// These accumulate when sessions terminate abruptly (server restart, browser tab closed, etc.)
    /// without going through the normal EndCollaboration hub/API flow.
    ///
    /// <paramref name="olderThanHours"/>: when 0 (default) ALL stuck sessions are cleaned.
    /// Pass a positive value to restrict to sessions older than that many hours.
    ///
    /// Called by the Supervisor dashboard "Clean up stale sessions" button.
    /// </summary>
    [HttpPost("cleanup-stale-sessions")]
    public async Task<IActionResult> CleanupStaleSessions(
        [FromQuery] int olderThanHours = 0,
        CancellationToken ct = default)
    {
        // olderThanHours == 0 → clean ALL stuck sessions regardless of age.
        // olderThanHours  > 0 → restrict to sessions created before the cutoff.
        List<Collaboration> stale;

        if (olderThanHours > 0)
        {
            var cutoff = DateTime.UtcNow.AddHours(-olderThanHours);
            stale = await db.Collaborations
                .Where(c => c.EndedAt == null && c.CreatedAt < cutoff)
                .ToListAsync(ct);
        }
        else
        {
            // olderThanHours == 0: wipe every session that was never formally ended.
            stale = await db.Collaborations
                .Where(c => c.EndedAt == null)
                .ToListAsync(ct);
        }

        if (stale.Count == 0)
            return Ok(new { cleaned = 0, message = "No stale sessions found." });

        var now = DateTime.UtcNow;
        foreach (var c in stale)
        {
            c.EndedAt = now;
            c.Status  = "Abandoned";
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { cleaned = stale.Count, message = $"Marked {stale.Count} session(s) as Abandoned." });
    }

    // ── GET /api/v1/demo/sessions-debug ──────────────────────────────────────
    /// <summary>
    /// Returns all collaborations for the given app with their Status, EndedAt, and CreatedAt.
    /// Use this to diagnose why sessions appear in the supervisor sidebar.
    /// </summary>
    [HttpGet("sessions-debug/{appId:guid}")]
    public async Task<IActionResult> SessionsDebug(Guid appId, CancellationToken ct)
    {
        var rows = await db.Collaborations
            .AsNoTracking()
            .Where(c => c.ApplicationId == appId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Status,
                EndedAt   = c.EndedAt.HasValue ? c.EndedAt.Value.ToString("o") : (string?)null,
                CreatedAt = c.CreatedAt.ToString("o"),
                Age       = (int)(DateTime.UtcNow - c.CreatedAt).TotalMinutes + " min ago"
            })
            .ToListAsync(ct);

        return Ok(rows);
    }

    private record SeedApp(Guid Id, string AuthProvider, string ApiKey);
}

/// <summary>Request body for POST /api/v1/demo/users</summary>
public record DemoCreateUserRequest(
    string  Name,
    string  Role,
    Guid    ApplicationId,
    string? Token = null,
    string? Email = null
);
