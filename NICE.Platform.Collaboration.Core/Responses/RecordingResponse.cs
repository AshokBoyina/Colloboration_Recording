namespace NICE.Platform.Collaboration.Core.Responses;

public class RecordingResponse
{
    public Guid     Id              { get; set; }
    public Guid     CollaborationId { get; set; }
    public string   RecordingType   { get; set; } = default!;
    public string   Status          { get; set; } = default!;
    public DateTime StartedAt       { get; set; }
    public DateTime? StoppedAt      { get; set; }
    public int?     DurationSeconds { get; set; }
    public string?  BlobUri         { get; set; }
}
