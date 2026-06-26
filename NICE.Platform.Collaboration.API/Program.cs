// ── Standalone Recording API ──────────────────────────────────────────────────
// Exposes: RecordingHub, AuthController, RecordingsController, DemoController.
// Full collaboration endpoints (CollaborationHub, Messages, Users, etc.) are
// excluded — available in the full NICE Platform Collaboration Engine.
// ─────────────────────────────────────────────────────────────────────────────

using NICE.Platform.Collaboration.Application;
using NICE.Platform.Collaboration.Infrastructure;
using NICE.Platform.Collaboration.API.Middleware;
using NICE.Platform.Collaboration.API.Hubs;
using NICE.Platform.Collaboration.API.Services;
using NICE.Platform.Collaboration.Application.Interfaces.Services;
using NICE.Platform.Collaboration.Core.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration);

// CORS — allow any localhost origin in development; lock down in production
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin =>
    {
        var uri = new Uri(origin);
        return uri.Host is "localhost" or "127.0.0.1";
    })
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "NICE Platform Standalone Recording API",
        Version     = "v1",
        Description = "Standalone Screen Recording engine for Readi. " +
                      "Authenticate via POST /api/v1/auth/validate, then open " +
                      "a SignalR connection to /hubs/recording."
    });

    const string apiKeyScheme = "X-Api-Key";
    options.AddSecurityDefinition(apiKeyScheme, new OpenApiSecurityScheme
    {
        Name        = "X-Api-Key",
        Type        = SecuritySchemeType.ApiKey,
        In          = ParameterLocation.Header,
        Description = "Application API key. Required for POST /api/v1/auth/validate."
    });

    const string bearerScheme = "Bearer";
    options.AddSecurityDefinition(bearerScheme, new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Internal JWT issued after successful auth validation."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = bearerScheme
                }
            },
            []
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
});

// ── SignalR ────────────────────────────────────────────────────────────────
var signalRBuilder = builder.Services.AddSignalR(opts =>
{
    opts.MaximumReceiveMessageSize = 512 * 1024;  // 512 KB — handles recording chunk metadata
});

// FeatureFlags:UseAzureSignalR → route SignalR through Azure SignalR Service (Default mode).
// No hub/client code changes: MapHub routes stay the same; Azure SignalR proxies the sockets.
if (builder.Configuration.GetValue<bool>("FeatureFlags:UseAzureSignalR"))
{
    var azureConn = builder.Configuration["Azure:SignalR:ConnectionString"];
    if (string.IsNullOrWhiteSpace(azureConn) ||
        azureConn.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            "FeatureFlags:UseAzureSignalR is true but Azure:SignalR:ConnectionString is not set " +
            "(Section 9 of appsettings.json).");

    signalRBuilder.AddAzureSignalR(azureConn);
}

// ── ISignalRNotifier registered here (avoids Infrastructure→API circular dep) ──
builder.Services.AddScoped<ISignalRNotifier, SignalRNotifier>();
builder.Services.AddScoped<SignalRAccessTokenBridge>();

// ── JWT bearer auth ────────────────────────────────────────────────────────
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtConfig = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = jwtConfig["Issuer"],
            ValidateAudience         = true,
            ValidAudience            = jwtConfig["Audience"],
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtConfig["Key"]!)),
            ClockSkew                = TimeSpan.FromMinutes(2)
        };

        // SignalR WebSocket connections pass the token as ?access_token=...
        // If the incoming token is an external READI token, bridge it to an
        // internal session JWT before normal JwtBearer validation.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = async ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    var bridge = ctx.HttpContext.RequestServices
                        .GetRequiredService<SignalRAccessTokenBridge>();

                    if (bridge.IsInternalSessionToken(accessToken))
                    {
                        ctx.Token = accessToken;
                        return;
                    }

                    var appName = ctx.Request.Query["applicationName"].FirstOrDefault()
                                  ?? ctx.Request.Query["appName"].FirstOrDefault()
                                  ?? ctx.Request.Query["accessKey"].FirstOrDefault()
                                  ?? ctx.Request.Headers["X-Access-Key"].FirstOrDefault();

                    var bridged = await bridge.TryExchangeAsync(
                        accessToken, appName, ctx.HttpContext.RequestAborted);

                    ctx.Token = string.IsNullOrWhiteSpace(bridged)
                        ? accessToken
                        : bridged;
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Startup: create DB schema + clear stale sessions ─────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<NICE.Platform.Collaboration.Infrastructure.Persistence.CollaborationDbContext>();

    // EnsureCreated creates all tables from the EF Core model if they don't
    // exist yet. It is idempotent (no-op if tables already exist) and avoids
    // the need to run 'dotnet ef database update' manually.
    //
    // NOTE: EnsureCreated and EF Core Migrations are mutually exclusive.
    // When this project adopts formal migrations, replace this call with
    //   db.Database.Migrate()
    // and remove EnsureCreated from here.
    try
    {
        db.Database.EnsureCreated();
        app.Logger.LogInformation("Database schema is ready (EnsureCreated).");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex,
            "Database schema creation failed. " +
            "Check the connection string in appsettings.Development.json and ensure SQL Server / LocalDB is running.");
    }

    try
    {
        var stale = db.CurrentSessions.ToList();
        if (stale.Count > 0)
        {
            db.CurrentSessions.RemoveRange(stale);
            db.SaveChanges();
            app.Logger.LogInformation("Startup cleanup: removed {Count} stale CurrentSessions rows.", stale.Count);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Startup cleanup of CurrentSessions failed (non-fatal).");
    }
}

// ── Startup: ensure local storage folders exist ───────────────────────────
try
{
    var recordingsPath  = app.Configuration["LocalStorage:RecordingsPath"];
    var attachmentsPath = app.Configuration["LocalStorage:AttachmentsPath"];
    if (!string.IsNullOrWhiteSpace(recordingsPath))  Directory.CreateDirectory(recordingsPath);
    if (!string.IsNullOrWhiteSpace(attachmentsPath)) Directory.CreateDirectory(attachmentsPath);
    app.Logger.LogInformation("Local storage folders ready: Recordings={R}", recordingsPath);
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not create local storage folders (non-fatal).");
}

// ── Middleware pipeline ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "NICE Standalone Recording API v1");
        c.RoutePrefix = string.Empty;
    });
}

// ── Static files — serves recorder.html + monitor.html from wwwroot ──────────
app.UseStaticFiles();

app.UseCors();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// ── Hubs ─────────────────────────────────────────────────────────────────
// CollaborationHub is included: standalone pages use it for WebRTC signaling
// (StartStandaloneSession, JoinStandaloneSession, ShareScreenOffer/Answer).
// RecordingHub handles the actual recorded media chunk streaming.
app.MapHub<CollaborationHub>(HubRoutes.Collaboration);
app.MapHub<RecordingHub>(HubRoutes.Recording);

app.Run();

public partial class Program { }
