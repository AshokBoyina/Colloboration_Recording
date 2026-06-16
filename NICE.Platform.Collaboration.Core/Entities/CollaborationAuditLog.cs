namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Immutable audit trail for all significant events that occur within the platform.
/// Rows are never updated or deleted.
/// </summary>
public class CollaborationAuditLog
{
    public Guid Id { get; set; }

    /// <summary>Related collaboration, if the event is scoped to one.</summary>
    public Guid? CollaborationId { get; set; }

    /// <summary>Related application.</summary>
    public Guid? ApplicationId { get; set; }

    /// <summary>User who triggered the event. Null for system-generated events.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Event category: Auth | Session | Chat | Transfer | Recording | Admin | System.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Specific event name, e.g. "UserAuthenticated", "CollaborationStarted",
    /// "AgentTransferred", "RecordingStarted".
    /// </summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>
    /// JSON payload with event-specific details. Schema varies by EventName.
    /// Stored as nvarchar(max) — consider moving to a JSON column type in SQL 2022+.
    /// </summary>
    public string? Payload { get; set; }

    /// <summary>Client IP address at time of event.</summary>
    public string? IpAddress { get; set; }

    public DateTime OccurredAt { get; set; }
}
