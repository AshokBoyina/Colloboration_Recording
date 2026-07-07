using NICE.Platform.Collaboration.UI.Models;

namespace NICE.Platform.Collaboration.UI.Services;

public interface IAuthService
{
    UserSession Current { get; }
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Redeems a one-time launch <b>handoff code</b> (from ?code= on the /launch page)
    /// for the session JWT and establishes the in-memory session. No token ever
    /// appears in a URL. The session is intentionally NOT persisted to local storage.
    /// </summary>
    Task<LoginResponse> RedeemHandoffAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Mints a short-lived, single-use SignalR <b>hub ticket</b> from the current
    /// session JWT (sent as an Authorization header). The ticket — not the JWT — is
    /// what goes in the hub connection URL.
    /// </summary>
    Task<string?> GetHubTicketAsync(CancellationToken ct = default);

    /// <summary>Try to restore a previously saved session from local storage.</summary>
    Task TryRestoreAsync();
    void Logout();
    Task LogoutAsync(CancellationToken ct = default);
    event Action? OnChange;
}

