namespace NICE.Platform.Collaboration.Core.Entities;

/// <summary>
/// File or media attachment linked to a CollaborationMessage.
/// The file itself lives in Azure Blob Storage; this row holds the metadata.
/// </summary>
public class CollaborationAttachment
{
    public Guid Id { get; set; }

    public Guid MessageId { get; set; }

    /// <summary>Original filename as uploaded by the sender.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type, e.g. "image/png", "application/pdf".</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>Blob Storage URI (or relative container path).</summary>
    public string BlobUri { get; set; } = string.Empty;

    /// <summary>Thumbnail URI for images/videos. Null for other file types.</summary>
    public string? ThumbnailUri { get; set; }

    public DateTime UploadedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public CollaborationMessage Message { get; set; } = null!;
}
