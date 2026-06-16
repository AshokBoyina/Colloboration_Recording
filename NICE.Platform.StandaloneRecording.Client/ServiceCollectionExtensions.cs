namespace NICE.Platform.StandaloneRecording.Client;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for wiring the Standalone Recording client into a host
/// application's DI container (ASP.NET Core, WinForms, WPF, etc.).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Standalone Recording client configuration.
    ///
    /// Usage in <c>Program.cs</c> or <c>Startup.ConfigureServices</c>:
    /// <code>
    /// builder.Services.AddStandaloneRecordingClient(builder.Configuration);
    /// </code>
    ///
    /// Or with an inline override:
    /// <code>
    /// builder.Services.AddStandaloneRecordingClient(config, overrides =>
    /// {
    ///     overrides.UserName = currentUser.FullName;
    ///     overrides.AccessToken = currentUser.NiceToken;
    /// });
    /// </code>
    /// </summary>
    public static IServiceCollection AddStandaloneRecordingClient(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<StandaloneRecordingConfig>? configure = null)
    {
        services.Configure<StandaloneRecordingConfig>(
            configuration.GetSection(StandaloneRecordingConfig.SectionName));

        if (configure is not null)
            services.PostConfigure<StandaloneRecordingConfig>(configure);

        // Register a named HttpClient scoped to the standalone recording API
        services.AddHttpClient(StandaloneRecordingClient.HttpClientName, (sp, client) =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StandaloneRecordingConfig>>().Value;
            if (!string.IsNullOrWhiteSpace(cfg.ApiBaseUrl))
                client.BaseAddress = new Uri(cfg.ApiBaseUrl.TrimEnd('/') + "/");
            if (!string.IsNullOrWhiteSpace(cfg.ApiKey))
                client.DefaultRequestHeaders.Add("X-Api-Key", cfg.ApiKey);
            if (!string.IsNullOrWhiteSpace(cfg.ApplicationName))
                client.DefaultRequestHeaders.Add("X-Access-Key", cfg.ApplicationName);
        });

        services.AddScoped<StandaloneRecordingClient>();
        return services;
    }
}
