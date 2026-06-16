namespace NICE.Platform.Collaboration.Infrastructure.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;

/// <summary>
/// Issues and validates the internal engine JWT returned to the client after
/// successful authentication.
///
/// Token claims:
///   sub          — userId
///   given_name   — firstName
///   family_name  — lastName
///   email        — email
///   role         — userType (External / Internal / Agent / Supervisor / StandAlone)
///   app          — applicationId
///   sid          — sessionId
///   provider     — authProvider (READI / NICE / ANON)
///
/// Token lifetime:
///   External users → shorter TTL (ExpiryMinutesExternal, default 30 min)
///   All other roles → standard TTL (ExpiryMinutes, default 60 min)
/// </summary>
public sealed class JwtTokenService(
    IConfiguration configuration,
    ILogger<JwtTokenService> logger) : ITokenService
{
    private const string ClaimApp      = "app";
    private const string ClaimSid      = "sid";
    private const string ClaimProvider = "provider";

    public string GenerateToken(
        Guid     userId,
        string   role,
        Guid     applicationId,
        Guid     sessionId,
        bool     isExternal,
        string?  firstName    = null,
        string?  lastName     = null,
        string?  email        = null,
        string   authProvider = "UNKNOWN")
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key        = jwtSection["Key"]
                         ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer     = jwtSection["Issuer"]   ?? "NICE.Platform.Collaboration";
        var audience   = jwtSection["Audience"] ?? "NICE.Platform.Collaboration.Clients";

        var expiryMinutes = isExternal
            ? int.TryParse(jwtSection["ExpiryMinutesExternal"], out var ext) ? ext : 30
            : int.TryParse(jwtSection["ExpiryMinutes"],         out var std) ? std : 60;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,        userId.ToString()),
            new(JwtRegisteredClaimNames.Jti,        Guid.NewGuid().ToString()),
            new(ClaimTypes.Role,                    role),
            new(ClaimApp,                           applicationId.ToString()),
            new(ClaimSid,                           sessionId.ToString()),
            new(ClaimProvider,                      authProvider)
        };

        if (!string.IsNullOrWhiteSpace(firstName))
            claims.Add(new(JwtRegisteredClaimNames.GivenName,  firstName));
        if (!string.IsNullOrWhiteSpace(lastName))
            claims.Add(new(JwtRegisteredClaimNames.FamilyName, lastName));
        if (!string.IsNullOrWhiteSpace(email))
            claims.Add(new(JwtRegisteredClaimNames.Email,      email));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds      = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiry     = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expiry,
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        logger.LogDebug(
            "Issued session JWT for user {UserId}, role={Role}, app={AppId}, session={SessionId}, expires={Expiry:u}.",
            userId, role, applicationId, sessionId, expiry);

        return tokenString;
    }

    public (Guid userId, string role, Guid applicationId, Guid sessionId) ValidateToken(string token)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var key        = jwtSection["Key"]
                         ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var issuer     = jwtSection["Issuer"]   ?? "NICE.Platform.Collaboration";
        var audience   = jwtSection["Audience"] ?? "NICE.Platform.Collaboration.Clients";

        var tokenHandler       = new JwtSecurityTokenHandler();
        var validationParams   = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = issuer,
            ValidateAudience         = true,
            ValidAudience            = audience,
            ValidateLifetime         = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuerSigningKey = true,
            ClockSkew                = TimeSpan.FromMinutes(2)
        };

        var principal = tokenHandler.ValidateToken(token, validationParams, out _);

        var userId        = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role          = principal.FindFirstValue(ClaimTypes.Role)!;
        var applicationId = Guid.Parse(principal.FindFirstValue(ClaimApp)!);
        var sessionId     = Guid.Parse(principal.FindFirstValue(ClaimSid)!);

        return (userId, role, applicationId, sessionId);
    }
}
