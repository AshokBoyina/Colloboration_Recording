using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NICE.Platform.Collaboration.UI.Models;
using Microsoft.JSInterop;

namespace NICE.Platform.Collaboration.UI.Services;

public class AuthService(HttpClient http, IJSRuntime js) : IAuthService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private UserSession _current = new();

    public UserSession Current   => _current;
    public event Action? OnChange;

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                "api/v1/collaboration/auth/validate");

            req.Headers.TryAddWithoutValidation("X-Api-Key",    request.ApiKey);
            req.Headers.TryAddWithoutValidation("X-Access-Key", request.ApplicationName);
            req.Headers.TryAddWithoutValidation("AuthToken",    request.AuthToken);
            req.Headers.TryAddWithoutValidation("UserType",     request.UserType);

            var resp = await http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errorMsg;
                try
                {
                    var errDoc = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
                    errorMsg = errDoc.TryGetProperty("error", out var e) ? e.GetString() ?? json : json;
                }
                catch { errorMsg = json; }
                return new LoginResponse { Error = $"HTTP {(int)resp.StatusCode}: {errorMsg}" };
            }

            var doc = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);

            if (!doc.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
            {
                var err = doc.TryGetProperty("error", out var ep) ? ep.GetString() : "Unknown error";
                return new LoginResponse { Error = err ?? "Login failed." };
            }

            var sessionToken = doc.TryGetProperty("sessionToken", out var tp) ? tp.GetString() : null;
            if (string.IsNullOrEmpty(sessionToken))
                return new LoginResponse { Error = "No session token returned." };

            var user            = doc.TryGetProperty("user", out var up) ? up : default;
            var userId          = Str(user, "userId")          ?? Guid.NewGuid().ToString();
            var firstName       = Str(user, "firstName")       ?? "";
            var lastName        = Str(user, "lastName")        ?? "";
            var userType        = Str(user, "userType")        ?? request.UserType;
            var applicationId   = Str(user, "applicationId")  ?? string.Empty;
            var applicationName = Str(user, "applicationName") ?? request.ApplicationName;

            _current = new UserSession
            {
                Token            = sessionToken,
                UserId           = userId,
                DisplayName      = $"{firstName} {lastName}".Trim(),
                UserType         = userType,
                ApplicationId    = applicationId,
                ApplicationName  = applicationName,
                // Bot API credentials come from the login form, not from appsettings.
                // X-Api-Key → BotApiKey; X-Access-Key (ApplicationName) → BotApiAccessKey.
                BotApiKey        = request.BotApiKey,
                BotApiAccessKey  = request.BotApiAccessKey
            };

            // Persist full session so a browser refresh can restore it without re-login
            var sessionJson = JsonSerializer.Serialize(_current, JsonOpts);
            await js.InvokeVoidAsync("chatStorage.saveSession", sessionJson);
            OnChange?.Invoke();

            return new LoginResponse
            {
                Success     = true,
                Token       = sessionToken,
                UserId      = userId,
                DisplayName = _current.DisplayName,
                UserType    = userType
            };
        }
        catch (Exception ex)
        {
            return new LoginResponse { Error = ex.Message };
        }
    }

    public async Task<LoginResponse> RedeemHandoffAsync(string code, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, "api/v1/collaboration/monitor/handoff/redeem")
            {
                Content = JsonContent.Create(new { code })
            };

            var resp = await http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                string errorMsg;
                try
                {
                    var errDoc = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
                    errorMsg = errDoc.TryGetProperty("error", out var e) ? e.GetString() ?? json : json;
                }
                catch { errorMsg = json; }
                return new LoginResponse { Error = $"Launch failed (HTTP {(int)resp.StatusCode}): {errorMsg}" };
            }

            var doc          = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
            var sessionToken = doc.TryGetProperty("sessionToken", out var tp) ? tp.GetString() : null;
            if (string.IsNullOrEmpty(sessionToken))
                return new LoginResponse { Error = "Launch redeem returned no session token." };

            var user = doc.TryGetProperty("user", out var up) ? up : default;

            // In-memory only — do NOT persist to local storage (security decision).
            _current = new UserSession
            {
                Token           = sessionToken,
                UserId          = Str(user, "userId")          ?? string.Empty,
                DisplayName     = Str(user, "displayName")     ?? string.Empty,
                UserType        = Str(user, "userType")        ?? "StandaloneMonitor",
                ApplicationId   = Str(user, "applicationId")   ?? string.Empty,
                ApplicationName = Str(user, "applicationName") ?? string.Empty
            };
            OnChange?.Invoke();

            return new LoginResponse
            {
                Success     = true,
                Token       = sessionToken,
                UserId      = _current.UserId,
                DisplayName = _current.DisplayName,
                UserType    = _current.UserType
            };
        }
        catch (Exception ex)
        {
            return new LoginResponse { Error = ex.Message };
        }
    }

    public async Task<string?> GetHubTicketAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_current.Token)) return null;
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Post, "api/v1/collaboration/monitor/hub-ticket");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _current.Token);

            var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var doc  = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
            return doc.TryGetProperty("ticket", out var tp) ? tp.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public void Logout()
    {
        _current = new();
        // Fire-and-forget clear (sync callers can't await)
        _ = js.InvokeVoidAsync("chatStorage.clear").AsTask();
        OnChange?.Invoke();
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        _current = new();
        try { await js.InvokeVoidAsync("chatStorage.clear", ct); } catch { }
        OnChange?.Invoke();
    }
    private static string? Str(System.Text.Json.JsonElement el, string prop) =>
        el.ValueKind == System.Text.Json.JsonValueKind.Object &&
        el.TryGetProperty(prop, out var v) ? v.GetString() : null;

    public async Task TryRestoreAsync()
    {
        try
        {
            // chatStorage.loadSession() reads from localStorage key 'nice_session'
            var saved = await js.InvokeAsync<string?>("chatStorage.loadSession");
            if (!string.IsNullOrEmpty(saved))
            {
                var restored = System.Text.Json.JsonSerializer.Deserialize<UserSession>(saved, JsonOpts);
                if (restored is not null && !string.IsNullOrEmpty(restored.Token))
                {
                    // Reject expired tokens — don't silently restore a dead session.
                    // The user will be redirected to /login which will prompt re-auth.
                    if (IsJwtExpired(restored.Token))
                    {
                        try { await js.InvokeVoidAsync("chatStorage.clear"); } catch { }
                        return;
                    }

                    _current = restored;
                    OnChange?.Invoke();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Returns true if the JWT's <c>exp</c> claim is in the past (or cannot be parsed).
    /// Decodes base64url payload without any external library — pure string manipulation.
    /// </summary>
    private static bool IsJwtExpired(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return true;

            // Base64url → base64 standard
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "=";  break;
            }

            var bytes   = Convert.FromBase64String(payload);
            var json    = System.Text.Encoding.UTF8.GetString(bytes);
            var doc     = System.Text.Json.JsonDocument.Parse(json);
            var root    = doc.RootElement;

            if (!root.TryGetProperty("exp", out var expProp)) return false; // no exp → treat as valid

            var exp         = expProp.GetInt64();
            var nowEpoch    = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return nowEpoch >= exp;
        }
        catch
        {
            return true; // if we can't parse, treat as expired (safe default)
        }
    }
}
