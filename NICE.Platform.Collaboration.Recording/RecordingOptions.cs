namespace NICE.Platform.Collaboration.Recording;

/// <summary>
/// Configuration for the embeddable <see cref="StandaloneRecorder"/> screen-recording
/// component. A host application binds this from configuration (section
/// <see cref="SectionName"/>) and/or overrides individual values via component parameters.
/// </summary>
public sealed class RecordingOptions
{
    public const string SectionName = "CollaborationRecording";

    /// <summary>
    /// Base URL of the Standalone Recording API that hosts the recording hub and
    /// chunk-upload endpoint, e.g. <c>https://recording-api.readi.example.com</c>.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The registered application's GUID, passed to the recording hub's
    /// <c>StartRecording</c> method. Identifies which application owns the session.
    /// </summary>
    public string ApplicationId { get; set; } = string.Empty;

    /// <summary>
    /// Application name/access key used by cross-app auth flows (for example, "Readi").
    /// Passed on hub/chunk requests so server-side token bridge can resolve app context.
    /// </summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// API key sent as <c>X-Api-Key</c> for validate-first auth flow.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// User type sent as <c>UserType</c> for validate-first auth flow.
    /// Defaults to <c>StandAlone</c>.
    /// </summary>
    public string UserType { get; set; } = "StandAlone";

    /// <summary>
    /// Optional <b>fallback</b> bearer token. Prefer passing the user-specific token
    /// at start time — <c>window.niceScreenRecording.start(userAccessToken)</c> (or
    /// <c>StandaloneRecorder.StartAsync(token)</c>) — since the token is per-user and
    /// this options object is app-wide. Used only when no token is passed to start.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Target video bitrate for MediaRecorder (bits per second). Default 2 Mbps.</summary>
    public int VideoBitsPerSecond { get; set; } = 2_000_000;

    /// <summary>MediaRecorder timeslice in milliseconds — how often a chunk is emitted. Default 2000.</summary>
    public int TimesliceMs { get; set; } = 2000;
}
