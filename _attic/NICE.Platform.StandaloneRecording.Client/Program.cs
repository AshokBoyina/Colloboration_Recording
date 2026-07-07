using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NICE.Platform.Collaboration.Recording;          // the consumed NuGet package
using NICE.Platform.StandaloneRecording.Client;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Bind this customer app's settings (wwwroot/appsettings.json → "StandaloneRecording").
var cfg = builder.Configuration
    .GetSection(StandaloneRecordingConfig.SectionName)
    .Get<StandaloneRecordingConfig>() ?? new StandaloneRecordingConfig();

var apiBase = cfg.ApiBaseUrl.TrimEnd('/');
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase + "/") });

// Helper from this project: mints/obtains the user token + health checks.
builder.Services.AddStandaloneRecordingClient(builder.Configuration);

// The NuGet package under test. ApiBaseUrl is app-wide; the user-specific token and
// the collaborationId are passed at start time from the Recorder page.
builder.Services.AddCollaborationRecording(o =>
{
    o.ApiBaseUrl       = apiBase;
    o.ApplicationName  = cfg.ApplicationName;
    o.ApiKey           = cfg.ApiKey;
    o.UserType         = cfg.UserRole;
    o.AccessToken      = string.IsNullOrWhiteSpace(cfg.AccessToken) ? null : cfg.AccessToken;
});

await builder.Build().RunAsync();
