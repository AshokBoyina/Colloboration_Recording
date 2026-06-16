namespace NICE.Platform.Collaboration.API.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Core.Requests;
using NICE.Platform.Collaboration.Core.Responses;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Enums;
using NICE.Platform.Collaboration.Infrastructure.Persistence;
using NICE.Platform.Collaboration.Infrastructure.Settings;

/// <summary>
/// Authentication entry point for the Collaboration Engine.
///
/// Every client calls POST api/v1/collaboration/auth/validate before opening
/// a SignalR connection. All parameters arrive as HTTP headers — there is no body.
///
/// Required headers:
///   X-Api-Key    — the registered application's secret API key.
///                  Identifies the application and determines the staff auth provider.
///   X-Access-Key — the Application Name (e.g. "SurveyPortal", "Readi").
///                  Used to load the application config.
///   AuthToken    — the token to validate (format depends on auth provider selected).
///   UserType     — External | Internal | Agent | Supervisor | StandAlone
///
/// Auth provider routing:
///   • UserType = External  → Always uses Anonymous (ANON) auth regardless of app config.
///                            The AuthToken is decoded locally; no external provider call.
///   • All other UserTypes  → Staff provider, resolved by precedence:
///                            (1) the application's explicit StaffAuthProvider override,
///                            (2) else FeatureFlags:UseReadiAuth → READI (non-demo default),
///                            (3) else a 500 (nothing configured).
///
/// Flow:
///   1. Validate and read X-Api-Key, X-Access-Key, AuthToken, UserType headers
///   2. Look up application config by X-Access-Key from appsettings / SQL
///   3. Route to auth provider: External → ANON; others → app's StaffAuthProvider
///   4. Validate AuthToken via the selected provider
///   5. Upsert CollaborationUser + CollaborationApplicationUser in DB
///   6. Issue an engine session JWT with the real DB user GUID
///   7. Return the session token + UserType-filtered ApplicationConfig
/// </summary>
[ApiController]
[Route("api/v1/collaboration/[controller]")]
[AllowAnonymous]
public class AuthController(
    IApplicationConfigProvider       appConfigProvider,
    IAuthValidatorFactory            validatorFactory,
    ITokenService                    tokenService,
    IOptions<FeatureFlagSettings>    featureFlags,
    CollaborationDbContext            db) : ControllerBase
{
    /// <summary>
    /// Validates the external token and returns an engine session token + application config.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(AuthValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthValidationResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AuthValidationResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        // 1. Read required headers ────────────────────────────────────────────
        if (!TryGetHeader(AuthHeaders.ApiKey, out var apiKey))
            return BadRequest(Fail($"Missing required header '{AuthHeaders.ApiKey}'."));

        if (!TryGetHeader(AuthHeaders.AccessKey, out var applicationName))
            return BadRequest(Fail($"Missing required header '{AuthHeaders.AccessKey}' (Application Name)."));

        if (!TryGetHeader(AuthHeaders.AuthToken, out var authToken))
            return BadRequest(Fail($"Missing required header '{AuthHeaders.AuthToken}'."));

        if (!TryGetHeader(AuthHeaders.UserType, out var userTypeRaw))
            return BadRequest(Fail($"Missing required header '{AuthHeaders.UserType}'."));

        // 2. Parse UserType ───────────────────────────────────────────────────
        if (!Enum.TryParse<UserType>(userTypeRaw, ignoreCase: true, out var userType))
        {
            return BadRequest(Fail(
                $"Invalid '{AuthHeaders.UserType}' value '{userTypeRaw}'. " +
                $"Accepted: {string.Join(", ", Enum.GetNames<UserType>())}."));
        }

        // 3. Look up application config ───────────────────────────────────────
        var appConfig = await appConfigProvider.GetByNameAsync(applicationName!, ct);
        if (appConfig is null)
        {
            return BadRequest(Fail(
                $"Application '{applicationName}' is not registered. " +
                $"Check the '{AuthHeaders.AccessKey}' header value."));
        }

        // 4. Route to the correct auth provider ──────────────────────────────
        //    External users always authenticate anonymously (ANON) — the token is decoded
        //    locally without calling any external identity provider.
        //
        //    Staff (Agent, Supervisor, Internal, StandAlone, …) provider precedence:
        //      1. The application's explicit StaffAuthProvider (per-app override), else
        //      2. FeatureFlags:UseReadiAuth → READI (global non-demo default), else
        //      3. error — nothing configured.
        AuthProvider provider;
        if (userType == UserType.External)
        {
            provider = AuthProvider.ANON;
        }
        else if (!string.IsNullOrWhiteSpace(appConfig.StaffAuthProvider))
        {
            // (1) Per-application override wins, even when the global flag is on.
            if (!Enum.TryParse<AuthProvider>(appConfig.StaffAuthProvider, ignoreCase: true, out provider))
            {
                return StatusCode(StatusCodes.Status500InternalServerError, Fail(
                    $"Application '{applicationName}' has an invalid StaffAuthProvider " +
                    $"'{appConfig.StaffAuthProvider}'. Accepted values: READI, ANON, LOCAL_JWT."));
            }
        }
        else if (featureFlags.Value.UseReadiAuth)
        {
            // (2) No per-app provider set — fall back to the global READI default.
            provider = AuthProvider.READI;
        }
        else
        {
            // (3) Neither a per-app provider nor the global flag — cannot route.
            return StatusCode(StatusCodes.Status500InternalServerError, Fail(
                $"Application '{applicationName}' has no StaffAuthProvider configured and " +
                "FeatureFlags:UseReadiAuth is false. Set a StaffAuthProvider " +
                "(READI | ANON | LOCAL_JWT) on the application, or enable FeatureFlags:UseReadiAuth."));
        }

        // 5. Validate the token via the selected provider ─────────────────────
        var validator = validatorFactory.GetValidator(provider);
        var result    = await validator.ValidateAsync(authToken!, ct);

        if (!result.IsValid)
            return Unauthorized(Fail(result.Error ?? "Token validation failed."));

        // 6. Upsert user in DB ────────────────────────────────────────────────
        //    The ExternalUserId is the raw token in mock mode (demo users are
        //    pre-created via DemoController so the token is already their slug).
        var (dbUserId, dbAppId) = await UpsertUserAsync(
            externalUserId: result.UserId ?? authToken ?? Guid.NewGuid().ToString(),
            firstName:      result.FirstName ?? string.Empty,
            lastName:       result.LastName  ?? string.Empty,
            email:          result.Email,
            applicationName: applicationName!,
            role:            userType.ToString(),
            ct);

        // 7. Issue internal session token using the real DB GUID ──────────────
        var sessionId    = Guid.NewGuid();
        var isExternal   = userType == UserType.External;
        var sessionToken = tokenService.GenerateToken(
            userId:        dbUserId,
            role:          userType.ToString(),
            applicationId: dbAppId,
            sessionId:     sessionId,
            isExternal:    isExternal,
            firstName:     result.FirstName,
            lastName:      result.LastName,
            email:         result.Email,
            authProvider:  provider.ToString());

        // 8. Build UserType-specific config slice ─────────────────────────────
        var configDto = BuildConfigDto(appConfig, userType);

        // 9. Return ───────────────────────────────────────────────────────────
        return Ok(new AuthValidationResponse
        {
            Success      = true,
            SessionToken = sessionToken,
            User = new AuthenticatedUserDto
            {
                UserId          = dbUserId.ToString(),
                FirstName       = result.FirstName,
                LastName        = result.LastName,
                Email           = result.Email,
                AuthProvider    = provider.ToString(),
                UserType        = userType.ToString(),
                ApplicationName = appConfig.Name,
                ApplicationId   = dbAppId,
                SessionId       = sessionId
            },
            ApplicationConfig = configDto
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Reads a required header. Returns false if absent or blank.</summary>
    private bool TryGetHeader(string headerName, out string? value)
    {
        value = Request.Headers[headerName].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>Creates a failed <see cref="AuthValidationResponse"/>.</summary>
    private static AuthValidationResponse Fail(string error) =>
        new() { Success = false, Error = error };

    /// <summary>
    /// Upserts a CollaborationUser (by ExternalUserId) and a CollaborationApplicationUser.
    /// Returns the internal DB GUID for the user and the application.
    ///
    /// Safe for both demo (slug tokens like "alice-smith") and real READI/NICE users
    /// (opaque sub claims like "readi|xyz"). ExternalUserId is the stable cross-session key.
    /// </summary>
    private async Task<(Guid DbUserId, Guid DbAppId)> UpsertUserAsync(
        string  externalUserId,
        string  firstName,
        string  lastName,
        string? email,
        string  applicationName,
        string  role,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var dbApp = await db.Applications
            .FirstOrDefaultAsync(a => a.Name == applicationName, ct)
            ?? throw new InvalidOperationException(
                $"Application '{applicationName}' not found in DB. Run demo seed or register the app first.");

        // The Users.Email column is NOT NULL. Some providers/flows (e.g. a StandaloneMonitor
        // demo token) carry no email claim, so synthesize a non-null placeholder to avoid a
        // SQL NOT NULL violation. Capped to the column's 256-char max.
        var safeEmail = string.IsNullOrWhiteSpace(email)
            ? $"no-email+{externalUserId}@local.invalid"
            : email!;
        if (safeEmail.Length > 256) safeEmail = safeEmail[..256];

        // Upsert user by stable external identity
        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId, ct);
        if (user is null)
        {
            user = new CollaborationUser
            {
                Id             = Guid.NewGuid(),
                ExternalUserId = externalUserId,
                FirstName      = firstName,
                LastName       = lastName,
                Email          = safeEmail,
                IsActive       = true,
                CreatedAt      = now
            };
            await db.Users.AddAsync(user, ct);
        }
        else
        {
            user.FirstName = firstName;
            user.LastName  = lastName;
            if (!string.IsNullOrWhiteSpace(email)) user.Email = email;
        }

        // Upsert role membership
        var appUser = await db.ApplicationUsers.FirstOrDefaultAsync(
            au => au.UserId == user.Id && au.ApplicationId == dbApp.Id, ct);
        if (appUser is null)
        {
            await db.ApplicationUsers.AddAsync(new CollaborationApplicationUser
            {
                ApplicationId = dbApp.Id,
                UserId        = user.Id,
                Role          = role,
                IsActive      = true,
                AddedAt       = now
            }, ct);
        }
        else
        {
            appUser.Role     = role;
            appUser.IsActive = true;
        }

        await db.SaveChangesAsync(ct);
        return (user.Id, dbApp.Id);
    }

    /// <summary>
    /// Builds the UserType-specific config slice from ApplicationConfig
    /// (loaded from JSON mock or, in Phase 2, from SQL).
    /// </summary>
    private static ApplicationConfigDto BuildConfigDto(ApplicationConfig appConfig, UserType userType) =>
        userType switch
        {
            UserType.External => new ApplicationConfigDto
            {
                ApplicationName = appConfig.Name,
                ExternalConfig  = new ExternalUserConfigDto
                {
                    ChatMode            = appConfig.External?.ChatMode ?? "BotThenHuman",
                    CanShareScreen      = appConfig.External?.CanShareScreen ?? false,
                    NeedScreenRecording = appConfig.External?.NeedScreenRecording ?? false
                }
            },
            UserType.Internal => new ApplicationConfigDto
            {
                ApplicationName = appConfig.Name,
                InternalConfig  = new InternalUserConfigDto
                {
                    ChatMode            = appConfig.Internal?.ChatMode ?? "TalkWithAvailableAgent",
                    CanShareScreen      = appConfig.Internal?.CanShareScreen ?? false,
                    NeedScreenRecording = appConfig.Internal?.NeedScreenRecording ?? false
                }
            },
            UserType.Agent => new ApplicationConfigDto
            {
                ApplicationName = appConfig.Name,
                AgentConfig     = new AgentConfigDto
                {
                    CanHandOffToOtherAgent = appConfig.Agent?.CanHandOffToOtherAgent ?? true,
                    MaxParallelChats       = appConfig.Agent?.MaxParallelChats > 0
                        ? appConfig.Agent.MaxParallelChats : 5
                }
            },
            UserType.Supervisor => new ApplicationConfigDto
            {
                ApplicationName  = appConfig.Name,
                SupervisorConfig = new SupervisorConfigDto
                {
                    MaxParallelChats       = appConfig.Supervisor?.MaxParallelChats > 0
                        ? appConfig.Supervisor.MaxParallelChats : 10,
                    CanHandOffToOtherAgent = true
                }
            },
            UserType.StandAlone or UserType.Standalone => new ApplicationConfigDto
            {
                ApplicationName  = appConfig.Name,
                StandaloneConfig = new StandaloneConfigDto
                {
                    AutoRecordScreen = appConfig.StandAlone?.AutoRecordScreen ?? false
                }
            },
            // StandaloneMonitor uses the same config slice as Supervisor (read-only watcher)
            UserType.StandaloneMonitor => new ApplicationConfigDto
            {
                ApplicationName  = appConfig.Name,
                SupervisorConfig = new SupervisorConfigDto
                {
                    MaxParallelChats       = appConfig.Supervisor?.MaxParallelChats > 0
                        ? appConfig.Supervisor.MaxParallelChats : 10,
                    CanHandOffToOtherAgent = false
                }
            },
            _ => new ApplicationConfigDto { ApplicationName = appConfig.Name }
        };
}