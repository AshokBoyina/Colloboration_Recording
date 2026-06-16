namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Lightweight SQL-backed key-value store that replaces Redis for session data.
/// Each row holds one piece of ephemeral data (e.g. a session token payload,
/// a collaboration-in-progress mapping, or an agent-queue entry).
/// Rows whose ExpiresAt has passed are ignored on read and swept by a background job.
/// </summary>
public class CollaborationSessionCache
{
    /// <summary>Surrogate PK (GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Application-defined cache key — unique, max 500 chars.
    /// Convention: "{category}:{discriminator}", e.g. "session:abc123", "queue:appId".
    /// </summary>
    public string CacheKey { get; set; } = string.Empty;

    /// <summary>JSON-serialised value, or plain string for simple scalars.</summary>
    public string CacheValue { get; set; } = string.Empty;

    /// <summary>UTC expiry. Null means the entry never expires automatically.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>UTC timestamp when the row was created or last updated.</summary>
    public DateTime CreatedAt { get; set; }
}
