namespace NICE.Platform.Collaboration.Application.Interfaces.Services;
public interface ISignalRNotifier
{
    Task NotifyGroupAsync(string group, string eventName, object payload, CancellationToken ct);
    Task NotifyUserAsync(string connectionId, string eventName, object payload, CancellationToken ct);
    Task NotifyGroupExceptAsync(string group, string excludeConnectionId, string eventName, object payload, CancellationToken ct);
}
