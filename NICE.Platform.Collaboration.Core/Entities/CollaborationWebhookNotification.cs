namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Outbound webhook delivery record.
/// Each row represents one attempt to deliver an event to an application's webhook URL.
/// Failed deliveries can be retried; the retry count and last error are tracked here.
/// </summary>
public class CollaborationWebhookNotification
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    /// <summary>Related collaboration, if the event is scoped to one.</summary>
    public Guid? CollaborationId { get; set; }

    /// <summary>
    /// Event type being delivered, e.g. "collaboration.started", "agent.transferred",
    /// "recording.ready", "session.ended".
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The JSON payload sent to the webhook endpoint.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Target URL the payload was posted to.</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>
    /// Delivery status: Pending | Delivered | Failed | Retrying | Abandoned.
    /// </summary>
    public string Status { get; set; } = "Pending";

    /// <summary>HTTP status code received from the target. Null if no response was received.</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Number of delivery attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>UTC time of the most recent delivery attempt.</summary>
    public DateTime? LastAttemptAt { get; set; }

    /// <summary>Error message or response body from the last failed attempt.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>UTC time the webhook was successfully delivered. Null until delivered.</summary>
    public DateTime? DeliveredAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public CollaborationApplication Application { get; set; } = null!;
}
