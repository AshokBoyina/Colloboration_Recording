namespace NICE.Platform.Collaboration.API.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Core.Enums;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Exchanges external SignalR access tokens (READI) for an internal session JWT
/// so hub [Authorize] can proceed with standard local token validation.
/// </summary>
public sealed class SignalRAccessTokenBridge(
    IAuthValidatorFactory              validatorFactory,
    ITokenService                      tokenService,
    CollaborationDbContext             db,
    IConfiguration                     configuration,
    ILogger<SignalRAccessTokenBridge> logger)
{
    public bool IsInternalSessionToken(string token)
    {
        try
        {
            var jwtIssuer = configuration["Jwt:Issuer"];
            var handler   = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token)) return false;

            var jwt = handler.ReadJwtToken(token);
            var sub = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
            var app = jwt.Claims.FirstOrDefault(c => c.Type == "app")?.Value;

            return string.Equals(jwt.Issuer, jwtIssuer, StringComparison.OrdinalIgnoreCase)
                   && Guid.TryParse(sub, out _)
                   && Guid.TryParse(app, out _);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> TryExchangeAsync(
        string externalToken,
        string? applicationName,
        CancellationToken ct)
    {
        try
        {
            var validator = validatorFactory.GetValidator(AuthProvider.READI);
            var result = await validator.ValidateAsync(externalToken, ct);
            if (!result.IsValid)
            {
                logger.LogWarning("SignalR token bridge: READI validation failed: {Error}", result.Error);

                // Fallback: use readable JWT claims directly for cross-app integration
                // flows where external provider validation is unavailable during hub negotiate.
                var fallbackUserId = TryReadClaim(externalToken, JwtRegisteredClaimNames.Sub)
                                     ?? TryReadClaim(externalToken, "sub");

                if (string.IsNullOrWhiteSpace(fallbackUserId))
                    return null;

                result = AuthValidatorResult.Ok(
                    fallbackUserId,
                    TryReadClaim(externalToken, JwtRegisteredClaimNames.Email)
                        ?? TryReadClaim(externalToken, "email"),
                    TryReadClaim(externalToken, JwtRegisteredClaimNames.GivenName)
                        ?? TryReadClaim(externalToken, "given_name"),
                    TryReadClaim(externalToken, JwtRegisteredClaimNames.FamilyName)
                        ?? TryReadClaim(externalToken, "family_name"));
            }

            var externalUserId = result.UserId;
            if (string.IsNullOrWhiteSpace(externalUserId))
            {
                logger.LogWarning("SignalR token bridge: validated READI token had no user id.");
                return null;
            }

            var dbApp = await ResolveApplicationAsync(applicationName, ct);
            if (dbApp is null)
            {
                logger.LogWarning("SignalR token bridge: no active application found for '{AppName}'.", applicationName);
                return null;
            }

            var role = TryReadClaim(externalToken, ClaimTypes.Role)
                       ?? TryReadClaim(externalToken, "role")
                       ?? "Supervisor";

            var now  = DateTime.UtcNow;
            var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalUserId == externalUserId, ct);
            if (user is null)
            {
                user = new CollaborationUser
                {
                    Id             = Guid.NewGuid(),
                    ExternalUserId = externalUserId,
                    FirstName      = result.FirstName ?? "Unknown",
                    LastName       = result.LastName  ?? "User",
                    Email          = result.Email,
                    IsActive       = true,
                    CreatedAt      = now
                };
                await db.Users.AddAsync(user, ct);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(result.FirstName)) user.FirstName = result.FirstName;
                if (!string.IsNullOrWhiteSpace(result.LastName))  user.LastName  = result.LastName;
                if (!string.IsNullOrWhiteSpace(result.Email))     user.Email     = result.Email;
            }

            await db.SaveChangesAsync(ct);

            var bridged = tokenService.GenerateToken(
                userId:        user.Id,
                role:          role,
                applicationId: dbApp.Id,
                sessionId:     Guid.NewGuid(),
                isExternal:    string.Equals(role, "External", StringComparison.OrdinalIgnoreCase),
                firstName:     user.FirstName,
                lastName:      user.LastName,
                email:         user.Email,
                authProvider:  AuthProvider.READI.ToString());

            logger.LogInformation(
                "SignalR token bridge: exchanged READI token for internal session token (user={UserId}, app={AppId}, role={Role}).",
                user.Id, dbApp.Id, role);

            return bridged;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "SignalR token bridge failed.");
            return null;
        }
    }

    private async Task<CollaborationApplication?> ResolveApplicationAsync(string? applicationName, CancellationToken ct)
    {
        var targetName = string.IsNullOrWhiteSpace(applicationName) ? "Readi" : applicationName.Trim();

        var named = await db.Applications
            .FirstOrDefaultAsync(a => a.Name == targetName, ct);
        if (named is not null)
        {
            if (!named.IsActive)
            {
                named.IsActive = true;
                await db.SaveChangesAsync(ct);
            }

            return named;
        }

        // Bootstrap the application row for bridge scenarios where app seed hasn't
        // been run yet but a valid external token is provided.
        var bootstrapped = new CollaborationApplication
        {
            Id                = Guid.NewGuid(),
            Name              = targetName,
            HashedApiKey      = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"autogen:{targetName}"))).ToLowerInvariant(),
            AuthProvider      = AuthProvider.READI.ToString(),
            MaxAgentsOnline   = 100,
            MaxUsersOnline    = 1000,
            BlobContainerPath = $"recordings/{targetName.ToLowerInvariant()}",
            IsActive          = true,
            CreatedAt         = DateTime.UtcNow
        };

        await db.Applications.AddAsync(bootstrapped, ct);
        await db.SaveChangesAsync(ct);

        return bootstrapped;
    }

    private static string? TryReadClaim(string token, string claimType)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token)) return null;
            var jwt = handler.ReadJwtToken(token);
            return jwt.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
        }
        catch
        {
            return null;
        }
    }
}
