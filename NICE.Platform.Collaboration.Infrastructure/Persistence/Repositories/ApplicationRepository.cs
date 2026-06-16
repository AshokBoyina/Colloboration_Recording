namespace NICE.Platform.Collaboration.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Application.Interfaces.Repositories;
using NICE.Platform.Collaboration.Core.Entities;
using NICE.Platform.Collaboration.Infrastructure.Persistence;

public class ApplicationRepository(CollaborationDbContext db) : IApplicationRepository
{
    public Task<CollaborationApplication?> GetByIdAsync(Guid id, CancellationToken ct)
        => db.Applications.FindAsync([id], ct).AsTask();

    public Task<CollaborationApplication?> GetByApiKeyHashAsync(string hash, CancellationToken ct)
        => db.Applications.FirstOrDefaultAsync(a => a.HashedApiKey == hash, ct);

    public Task<CollaborationApplication?> GetByNameAsync(string name, CancellationToken ct)
        => db.Applications.FirstOrDefaultAsync(a => a.Name == name, ct);

    public async Task AddAsync(CollaborationApplication app, CancellationToken ct)
    {
        await db.Applications.AddAsync(app, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(CollaborationApplication app, CancellationToken ct)
    {
        db.Applications.Update(app);
        await db.SaveChangesAsync(ct);
    }
}
