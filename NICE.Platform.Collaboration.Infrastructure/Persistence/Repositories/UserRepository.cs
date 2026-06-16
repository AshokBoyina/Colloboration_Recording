namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Application.Interfaces.Repositories;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public class UserRepository(CollaborationDbContext db) : IUserRepository
{
    public Task<CollaborationUser?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Users.FindAsync([id], ct).AsTask();

    public Task<CollaborationUser?> GetByExternalIdAsync(string externalId, CancellationToken ct)
        => db.Users.FirstOrDefaultAsync(u => u.ExternalUserId == externalId, ct);

    public async Task<IEnumerable<CollaborationUser>> GetActiveByApplicationAsync(
        Guid applicationId, CancellationToken ct)
        => await db.ApplicationUsers
            .Where(au => au.ApplicationId == applicationId && au.IsActive)
            .Select(au => au.User)
            .ToListAsync(ct);

    public async Task AddAsync(CollaborationUser user, CancellationToken ct)
    {
        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CollaborationUser user, CancellationToken ct)
    {
        db.Users.Update(user);
        await db.SaveChangesAsync(ct);
    }
}
