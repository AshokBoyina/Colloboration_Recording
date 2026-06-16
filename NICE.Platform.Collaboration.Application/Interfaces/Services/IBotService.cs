namespace NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Abstraction over the external bot backend.
/// apiKey / apiAccessKey are supplied per-call (captured from the login form)
/// so credentials never need to live in appsettings.
/// </summary>
public interface IBotService
{
    /// <summary>
    /// Sends <paramref name="userMessage"/> to the bot and returns the reply text.
    /// </summary>
    /// <param name="sessionId">Collaboration / chat session identifier.</param>
    /// <param name="userMessage">The customer's message text.</param>
    /// <param name="apiKey">X-Api-Key forwarded to the bot backend (empty = omit header).</param>
    /// <param name="apiAccessKey">X-API-Access-Key forwarded to the bot backend (empty = omit header).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> SendMessageAsync(
        string sessionId,
        string userMessage,
        string apiKey,
        string apiAccessKey,
        CancellationToken ct);

    /// <summary>
    /// Checks whether the bot backend recommends escalating to a human agent.
    /// </summary>
    Task<bool> ShouldEscalateToAgentAsync(
        string sessionId,
        string apiKey,
        string apiAccessKey,
        CancellationToken ct);

    /// <summary>Notifies the bot that the session has ended (best-effort).</summary>
    Task EndSessionAsync(
        string sessionId,
        string apiKey,
        string apiAccessKey,
        CancellationToken ct);
}
