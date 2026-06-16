namespace NICE.Platform.Collaboration.Application.Interfaces.Auth;

using NICE.Platform.Collaboration.Core.Enums;

/// <summary>
/// Selects the correct <see cref="IAuthValidator"/> implementation based on
/// the <c>X-Access-Key</c> header value (<see cref="AuthProvider"/>).
/// </summary>
public interface IAuthValidatorFactory
{
    /// <summary>
    /// Returns the <see cref="IAuthValidator"/> for the given <paramref name="provider"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when no validator is registered for <paramref name="provider"/>.
    /// </exception>
    IAuthValidator GetValidator(AuthProvider provider);
}
