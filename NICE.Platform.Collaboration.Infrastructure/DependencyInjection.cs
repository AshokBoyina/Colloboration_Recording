namespace NICE.Platform.Collaboration.Infrastructure;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NICE.Platform.Collaboration.Application.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Application.Interfaces.Repositories;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Infrastructure.Auth;
using NICE.Platform.Collaboration.Infrastructure.Auth.Settings;
using NICE.Platform.Collaboration.Infrastructure.Auth.Validators;
using NICE.Platform.Collaboration.Infrastructure.Persistence;
using NICE.Platform.Collaboration.Infrastructure.Persistence.Repositories;
using NICE.Platform.Collaboration.Infrastructure.Session;
using NICE.Platform.Collaboration.Infrastructure.Settings;
using NICE.Platform.Collaboration.Infrastructure.Bot;
using NICE.Platform.Collaboration.Infrastructure.Storage;
using NICE.Platform.Collaboration.Infrastructure.WebRTC;
using MediatR;

/// <summary>
/// Standalone Recording edition — wires only the services needed for
/// screen recording + auth.  Bot, Webhook, Collaboration and Azure Blob
/// services are excluded (available in the full Collaboration Engine).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services, IConfiguration config)
    {
        // ── MediatR — Recordings + OnboardUser handlers in this assembly ─────
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        // ── Feature flags ─────────────────────────────────────────────────────
        var flags = config
            .GetSection(FeatureFlagSettings.SectionName)
            .Get<FeatureFlagSettings>() ?? new FeatureFlagSettings();

        services.Configure<FeatureFlagSettings>(
            config.GetSection(FeatureFlagSettings.SectionName));

        // ── Auth validation settings ──────────────────────────────────────────
        services.Configure<AuthValidationSettings>(
            config.GetSection(AuthValidationSettings.SectionName));

        // ── Auth validators — READI (production) + LOCAL_JWT + Anonymous ──────
        services.AddSingleton<AnonymousAuthValidator>();
        services.AddSingleton<LocalJwtAuthValidator>();
        // READI calls an external HTTP endpoint, so register it as a typed
        // HttpClient (IHttpClientFactory manages handler pooling/lifetime).
        services.AddHttpClient<ReadiAuthValidator>();
        services.AddSingleton<IAuthValidatorFactory, AuthValidatorFactory>();

        // ── Application config provider ───────────────────────────────────────
        services.AddSingleton<IApplicationConfigProvider, JsonApplicationConfigProvider>();

        // ── JWT token service ─────────────────────────────────────────────────
        services.AddScoped<ITokenService, JwtTokenService>();

        // ── Legacy multi-provider JWT auth (kept for backwards compat) ────────
        services.Configure<Dictionary<string, AuthProviderConfig>>(
            config.GetSection("AuthProviders"));
        services.AddSingleton<IExternalAuthService, MultiProviderJwtAuthService>();

        // ── EF Core — SQL Server ──────────────────────────────────────────────
        services.AddDbContext<CollaborationDbContext>(opts =>
            opts.UseSqlServer(config.GetConnectionString("DefaultConnection")));

        // ── Session store — SQL (no Redis) ────────────────────────────────────
        services.AddScoped<ISessionStore, SqlSessionStore>();
        services.AddHostedService<SessionCacheCleanupService>();

        // ── In-memory recording session tracker ───────────────────────────────
        services.AddSingleton<IRecordingSessionTracker, RecordingSessionTracker>();

        // ── Storage — local disk (standalone; Azure Blob in full engine) ──────
        services.AddScoped<IBlobStorageService, LocalDiskStorageService>();
        services.AddSingleton<IRecordingStreamStore, LocalDiskStreamStore>();

        // ── ICE / STUN for WebRTC ─────────────────────────────────────────────
        if (flags.UseCustomTurn)
            services.AddSingleton<IIceServerProvider, TurnStunProvider>();
        else
            services.AddSingleton<IIceServerProvider, GoogleStunProvider>();

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<IApplicationRepository,  ApplicationRepository>();
        services.AddScoped<IUserRepository,          UserRepository>();
        services.AddScoped<IRecordingRepository,     RecordingRepository>();

        // ── Bot service — NoOp in standalone (no real bot integration) ────────
        services.AddScoped<IBotService, NoOpBotService>();

        // ISignalRNotifier is registered in Program.cs (avoids circular dep).

        return services;
    }
}
