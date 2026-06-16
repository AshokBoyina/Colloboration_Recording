namespace NICE.Platform.Collaboration.Infrastructure.Session;

using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

/// <summary>
/// SQL Server-backed implementation of <see cref="ISessionStore"/>.
/// Stores ephemeral key-value session data in the <c>Collaboration.SessionCache</c> table.
/// This is the permanent implementation — Redis has been removed from the project.
///
/// Lifecycle:
///   • <see cref="SetAsync"/>   → upsert (insert or update) a cache row.
///   • <see cref="GetAsync"/>   → read a non-expired row, null if missing or stale.
///   • <see cref="RemoveAsync"/> → hard-delete the row.
///   • <see cref="ExistsAsync"/> → check without fetching the value.
///
/// Expired rows are left in place and filtered on read.
/// A background <see cref="SessionCacheCleanupService"/> (registered in DI) sweeps them
/// periodically so the table does not grow unbounded.
/// </summary>
public class SqlSessionStore(CollaborationDbContext db) : ISessionStore
{
    // ── Write ─────────────────────────────────────────────────────────────────

    public async Task SetAsync(string key, string value, TimeSpan? expiry, CancellationToken ct)
    {
        var expiresAt = expiry.HasValue
            ? DateTime.UtcNow.Add(expiry.Value)
            : (DateTime?)null;

        var existing = await db.SessionCache
            .FirstOrDefaultAsync(r => r.CacheKey == key, ct);

        if (existing is null)
        {
            db.SessionCache.Add(new CollaborationSessionCache
            {
                Id         = Guid.NewGuid(),
                CacheKey   = key,
                CacheValue = value,
                ExpiresAt  = expiresAt,
                CreatedAt  = DateTime.UtcNow,
            });
        }
        else
        {
            existing.CacheValue = value;
            existing.ExpiresAt  = expiresAt;
            existing.CreatedAt  = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var row = await db.SessionCache
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CacheKey == key, ct);

        if (row is null)
            return null;

        // Treat expired rows as if they do not exist.
        if (row.ExpiresAt.HasValue && row.ExpiresAt.Value < now)
            return null;

        return row.CacheValue;
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        var row = await db.SessionCache
            .FirstOrDefaultAsync(r => r.CacheKey == key, ct);

        if (row is not null)
        {
            db.SessionCache.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }

    // ── Exists ────────────────────────────────────────────────────────────────

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.SessionCache
            .AsNoTracking()
            .AnyAsync(r => r.CacheKey == key
                        && (r.ExpiresAt == null || r.ExpiresAt > now), ct);
    }
}
