namespace NICE.Platform.Collaboration.Infrastructure.Persistence;

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NICE.Platform.Collaboration.Core.Entities;

public class CollaborationDbContext(DbContextOptions<CollaborationDbContext> options) : DbContext(options)
{
    // ── Applications & Users ──────────────────────────────────────────────
    public DbSet<CollaborationApplication>             Applications        { get; set; }
    public DbSet<CollaborationApplicationUserTypeConfig> UserTypeConfigs   { get; set; }
    public DbSet<CollaborationUser>                    Users               { get; set; }
    public DbSet<CollaborationApplicationUser>         ApplicationUsers    { get; set; }

    // ── Sessions ──────────────────────────────────────────────────────────
    public DbSet<CollaborationCurrentSession>          CurrentSessions     { get; set; }
    public DbSet<CollaborationUserSession>             UserSessions        { get; set; }
    public DbSet<CollaborationSessionCache>            SessionCache        { get; set; }

    // ── Collaborations & Participants ─────────────────────────────────────
    public DbSet<Collaboration>                        Collaborations      { get; set; }
    public DbSet<CollaborationParticipant>             Participants        { get; set; }

    // ── Messages & Attachments ────────────────────────────────────────────
    public DbSet<CollaborationMessage>                 Messages            { get; set; }
    public DbSet<CollaborationAttachment>              Attachments         { get; set; }
    public DbSet<CollaborationBotMessage>              BotMessages         { get; set; }

    // ── Recordings & Transfers ────────────────────────────────────────────
    public DbSet<CollaborationRecording>               Recordings          { get; set; }
    public DbSet<CollaborationTransferRequest>         TransferRequests    { get; set; }

    // ── Audit & Webhooks ──────────────────────────────────────────────────
    public DbSet<CollaborationAuditLog>                AuditLogs           { get; set; }
    public DbSet<CollaborationWebhookNotification>     WebhookNotifications { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // All tables live in the [Collaboration] schema — avoids the default dbo prefix.
        builder.HasDefaultSchema("Collaboration");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
