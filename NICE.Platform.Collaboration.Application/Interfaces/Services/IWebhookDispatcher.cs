namespace NICE.Platform.Collaboration.Application.Interfaces.Services;
public interface IWebhookDispatcher
{
    Task DispatchAsync(string webhookUrl, string eventName, object payload, CancellationToken ct);
}
