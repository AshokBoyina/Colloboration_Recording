namespace NICE.Platform.Collaboration.Recording;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI wiring for the embeddable screen-recording component. Call one of these from
/// the host Blazor application's <c>Program.cs</c>, then drop
/// <c>&lt;StandaloneRecorder /&gt;</c> onto any page/component.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RecordingOptions"/> bound from configuration section
    /// <see cref="RecordingOptions.SectionName"/> ("CollaborationRecording").
    /// <code>
    /// builder.Services.AddCollaborationRecording(builder.Configuration);
    /// </code>
    /// </summary>
    public static IServiceCollection AddCollaborationRecording(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RecordingOptions>(
            configuration.GetSection(RecordingOptions.SectionName));
        return services;
    }

    /// <summary>
    /// Registers <see cref="RecordingOptions"/> via an inline configuration delegate.
    /// <code>
    /// builder.Services.AddCollaborationRecording(o =>
    /// {
    ///     o.ApiBaseUrl    = "https://recording-api.example.com";
    ///     o.ApplicationId = appGuid.ToString();
    ///     o.AccessToken   = currentUser.Token;
    /// });
    /// </code>
    /// </summary>
    public static IServiceCollection AddCollaborationRecording(
        this IServiceCollection services, Action<RecordingOptions> configure)
    {
        services.Configure(configure);
        return services;
    }
}
