namespace NICE.Platform.Collaboration.Infrastructure.Settings;

/// <summary>
/// Individual on/off flags controlling which infrastructure implementation is
/// resolved at startup.  All default to <c>false</c> so the local/free path
/// is used unless explicitly enabled in <c>appsettings.json</c>.
///
/// Flip a flag to <c>true</c> when you are ready to migrate that concern to Azure.
/// No application code (handlers, hubs, controllers) changes — only the DI
/// registration switches implementation.
/// </summary>
public class FeatureFlagSettings
{
    public const string SectionName = "FeatureFlags";

    // NOTE: Mock auth is NOT controlled here — it is controlled by AuthValidation:UseMock
    // in appsettings.json and read by AuthValidationSettings.  There is no UseMockAuth flag.

    /// <summary>
    /// When <c>true</c>, SignalR uses Azure SignalR Service (horizontal scale, multi-instance).
    /// When <c>false</c>, in-process SignalR is used (single-instance, no extra infra cost).
    /// </summary>
    public bool UseAzureSignalR { get; set; }

    // Recording storage is always the local/network file system (RecordingStorage:*).
    // Azure Blob has been removed; there is no storage feature flag.

    /// <summary>
    /// When <c>true</c>, a dedicated TURN server is used for WebRTC relay
    /// (needed for peers behind strict symmetric NAT in production).
    /// When <c>false</c>, Google's free public STUN servers are used (sufficient for dev/test).
    /// </summary>
    public bool UseCustomTurn { get; set; }

    /// <summary>
    /// When <c>true</c>, bot replies are fetched from the real external bot API
    /// configured in the "BotApi" section (NiceBotApiService).
    /// When <c>false</c>, <see cref="NoOpBotService"/> is registered; the UI-side
    /// keyword-matching mock in ExternalChat.razor handles bot responses instead.
    /// Flip to <c>true</c> once NiceBotApi credentials are in place.
    /// </summary>
    public bool UseRealBot { get; set; }

    /// <summary>
    /// Global default for non-demo staff (non-External) authentication.
    /// When <c>true</c>, staff logins use the READI provider unless an application
    /// explicitly overrides it with its own <c>StaffAuthProvider</c> in appsettings.
    /// When <c>false</c>, the per-application <c>StaffAuthProvider</c> is required.
    /// Routing precedence (see AuthController): per-app StaffAuthProvider ▸ this flag ▸ error.
    /// External users always authenticate via ANON regardless of this flag.
    /// </summary>
    public bool UseReadiAuth { get; set; }
}
