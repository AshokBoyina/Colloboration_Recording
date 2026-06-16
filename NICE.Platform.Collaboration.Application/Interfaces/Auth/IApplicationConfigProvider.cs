namespace NICE.Platform.Collaboration.Application.Interfaces.Auth;

using NICE.Platform.Collaboration.Application.Auth;

/// <summary>
/// Retrieves the configuration for a registered application by name.
///
/// Current implementation: JSON mock (appsettings → Applications section).
/// Future implementation:  SQL table (ApplicationConfiguration).
///
/// The application name is supplied by the client via the X-Access-Key header.
/// </summary>
public interface IApplicationConfigProvider
{
    /// <summary>
    /// Returns the <see cref="ApplicationConfig"/> for the given application name,
    /// or <c>null</c> if no application with that name is registered.
    /// </summary>
    Task<ApplicationConfig?> GetByNameAsync(string applicationName, CancellationToken ct = default);
}
