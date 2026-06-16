namespace NICE.Platform.Collaboration.Application.Interfaces.Auth;

/// <summary>
/// Issues and validates the engine-internal JWT returned to the client after
/// successful authentication via any provider (READI, NICE, ANON).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT embedding the resolved user identity and session context.
    /// </summary>
    string GenerateToken(
        Guid    userId,
        string  role,
        Guid    applicationId,
        Guid    sessionId,
        bool    isExternal,
        string? firstName    = null,
        string? lastName     = null,
        string? email        = null,
        string  authProvider = "UNKNOWN");

    /// <summary>
    /// Validates a previously issued token and extracts its core claims.
    /// </summary>
    (Guid userId, string role, Guid applicationId, Guid sessionId) ValidateToken(string token);
}
