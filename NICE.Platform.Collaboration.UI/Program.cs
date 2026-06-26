using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NICE.Platform.Collaboration.UI;
using NICE.Platform.Collaboration.UI.Services;
using NICE.Platform.Collaboration.Recording;   // TEST ONLY — for the /recorder-test page (see below)

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ── API base address (override in wwwroot/appsettings.json for production) ──
var apiBase = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBase) });

// ── Console services ───────────────────────────────────────────────────────
// This app hosts the supervisor-facing consoles (Standalone Monitor today;
// more consoles can be added under Components/Pages). CollaborationHubService
// drives the WebRTC live-view signalling the Monitor uses to watch a session.
// Screen *recording* itself now lives in the NICE.Platform.Collaboration.Recording
// NuGet package (an embeddable Blazor component for host apps) — it is intentionally
// not referenced here.
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICollaborationHubService, CollaborationHubService>();
builder.Services.AddScoped<IDemoApiService, DemoApiService>();

// ── TEST ONLY ───────────────────────────────────────────────────────────────
// Wires the screen-recording NuGet package so the /recorder-test page can verify
// it (Test C). The Monitor console itself does not need this. To remove: delete
// this block, the ProjectReference in the .csproj, Components/Pages/RecorderTest.razor,
// and the recording @using in _Imports.razor.
builder.Services.AddCollaborationRecording(o => o.ApiBaseUrl = apiBase);
builder.Services.PostConfigure<RecordingOptions>(o =>
{
    o.ApplicationName ??= builder.Configuration["RecordingApplicationName"] ?? "Readi";
    o.ApiKey          ??= builder.Configuration["RecordingApiKey"] ?? "chat-ui-key";
    o.UserType         = builder.Configuration["RecordingUserType"] ?? o.UserType;
});

await builder.Build().RunAsync();
