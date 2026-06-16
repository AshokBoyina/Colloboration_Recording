namespace NICE.Platform.Collaboration.API.Services;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NICE.Platform.Collaboration.API.Hubs;
using NICE.Platform.Collaboration.Application.Interfaces.Services;

/// <summary>
/// Implements <see cref="ISignalRNotifier"/> by delegating to
/// <see cref="IHubContext{CollaborationHub}"/>.
/// Registered in Program.cs (API layer) so Infrastructure stays decoupled from hub types.
/// </summary>
public sealed class SignalRNotifier(
    IHubContext<CollaborationHub> collabHub,
    ILogger<SignalRNotifier>      logger) : ISignalRNotifier
{
    public async Task NotifyGroupAsync(
        string group, string eventName, object payload, CancellationToken ct)
    {
        logger.LogDebug("→ group [{Group}] event [{Event}]", group, eventName);
        await collabHub.Clients.Group(group).SendAsync(eventName, payload, ct);
    }

    public async Task NotifyUserAsync(
        string connectionId, string eventName, object payload, CancellationToken ct)
    {
        logger.LogDebug("→ connection [{ConnId}] event [{Event}]", connectionId, eventName);
        await collabHub.Clients.Client(connectionId).SendAsync(eventName, payload, ct);
    }

    public async Task NotifyGroupExceptAsync(
        string group, string excludeConnectionId, string eventName, object payload, CancellationToken ct)
    {
        logger.LogDebug(
            "→ group [{Group}] except [{ConnId}] event [{Event}]",
            group, excludeConnectionId, eventName);
        await collabHub.Clients
            .GroupExcept(group, excludeConnectionId)
            .SendAsync(eventName, payload, ct);
    }
}
