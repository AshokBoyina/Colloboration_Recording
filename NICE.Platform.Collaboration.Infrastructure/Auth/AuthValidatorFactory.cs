namespace NICE.Platform.Collaboration.Infrastructure.Auth;

using Microsoft.Extensions.DependencyInjection;
using NICE.Platform.Collaboration.Application.Interfaces.Auth;
using NICE.Platform.Collaboration.Core.Enums;
using NICE.Platform.Collaboration.Infrastructure.Auth.Validators;

/// <summary>
/// Standalone Recording edition: READI, LOCAL_JWT and ANON validators are wired.
/// READI is the production (non-demo) staff provider — selected per application
/// via StaffAuthProvider = "READI", or globally via FeatureFlags:UseReadiAuth.
/// NICE remains excluded (full Collaboration Engine only).
/// </summary>
public sealed class AuthValidatorFactory(IServiceProvider serviceProvider) : IAuthValidatorFactory
{
    public IAuthValidator GetValidator(AuthProvider provider) =>
        provider switch
        {
            AuthProvider.READI     => serviceProvider.GetRequiredService<ReadiAuthValidator>(),
            AuthProvider.ANON      => serviceProvider.GetRequiredService<AnonymousAuthValidator>(),
            AuthProvider.LOCAL_JWT => serviceProvider.GetRequiredService<LocalJwtAuthValidator>(),
            AuthProvider.NICE      => throw new InvalidOperationException(
                "Auth provider 'NICE' is not available in the Standalone Recording package. " +
                "Use READI, LOCAL_JWT or ANON (set Applications:<AppName>:StaffAuthProvider)."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                $"Unknown auth provider '{provider}'.")
        };
}
