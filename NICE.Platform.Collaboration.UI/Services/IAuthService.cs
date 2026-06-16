using NICE.Platform.Collaboration.UI.Models;

namespace NICE.Platform.Collaboration.UI.Services;

public interface IAuthService
{
    UserSession Current { get; }
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    /// <summary>Try to restore a previously saved session from local storage.</summary>
    Task TryRestoreAsync();
    void Logout();
    Task LogoutAsync(CancellationToken ct = default);
    event Action? OnChange;
}

