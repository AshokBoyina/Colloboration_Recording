namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// Metadata for a screen or session recording linked to a collaboration.
/// The actual recording file lives in Azure Blob Storage.
/// </summary>
public class CollaborationRecording
{
    public Guid Id { get; set; }

    public Guid CollaborationId { get; set; }

    /// <summary>
    /// Recording type: Screen | Session | Audio.
    /// </summary>
    public string RecordingType { get; set; } = "Screen";

    /// <summary>UTC timestamp when recording started.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC timestamp when recording stopped. Null while recording is in progress.</summary>
    public DateTime? StoppedAt { get; set; }

    /// <summary>Recording duration in seconds. Populated when StoppedAt is set.</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>Blob Storage URI of the recording file.</summary>
    public string? BlobUri { get; set; }

    /// <summary>File size in bytes. Populated after upload completes.</summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// Processing status: Pending | Processing | Ready | Failed.
    /// </summary>
    public string Status { get; set; } = "Pending";

    // ── Soft delete ───────────────────────────────────────────────────────
    /// <summary>True once the recording's media has been deleted from storage.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp when the recording was deleted.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>The user who deleted the recording (optional, for audit).</summary>
    public Guid? DeletedByUserId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Collaboration CollaborationEntity { get; set; } = null!;
}
