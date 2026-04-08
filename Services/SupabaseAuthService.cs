using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using TaskDone.Models;

namespace TaskDone.Services;

public sealed class SupabaseAuthService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseAuthService> _logger;
    private readonly IJSRuntime _jsRuntime;
    private readonly SemaphoreSlim _restoreLock = new(1, 1);
    private bool _restoreAttempted;

    private const string SessionStorageKey = "taskdone.auth.session.v1";
    private const string InvalidSupabaseUrlMessage = "Supabase URL is invalid. Configure SUPABASE_URL as a full URL such as https://your-project.supabase.co.";

    public SupabaseSession? CurrentSession { get; private set; }

    public bool IsAuthenticated =>
        !string.IsNullOrWhiteSpace(CurrentSession?.AccessToken) &&
        !IsAccessTokenExpired(CurrentSession.AccessToken);
    public string? CurrentUserId => CurrentSession?.User?.Id;
    public string? CurrentUserEmail => CurrentSession?.User?.Email;

    public event Action? AuthStateChanged;

    public SupabaseAuthService(
        HttpClient httpClient,
        IOptions<SupabaseOptions> options,
        ILogger<SupabaseAuthService> logger,
        IJSRuntime jsRuntime)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _jsRuntime = jsRuntime;
    }

    public async Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return AuthResult.Failure("Supabase is not configured. Set SUPABASE_URL and SUPABASE_KEY.");
        }

        if (!TryBuildEndpoint("/auth/v1/token?grant_type=password", out var endpoint))
        {
            return AuthResult.Failure(InvalidSupabaseUrlMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                email,
                password
            })
        };

        request.Headers.Add("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AnonKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<SupabaseAuthError>(body);
            var errorMessage = ResolveErrorMessage(error, body, "Unable to log in. Check credentials and Supabase configuration.");

            _logger.LogWarning("Supabase login failed for {Email}. Status: {StatusCode}. Error: {Error} [Code: {Code}]", email, (int)response.StatusCode, errorMessage, error?.Code ?? "unknown");
            
            // Show user-friendly message for 400 (invalid credentials), detailed message for other errors
            var userMessage = (int)response.StatusCode == 400 
                ? "Invalid email or password. Please try again."
                : errorMessage;
            
            return AuthResult.Failure(userMessage);
        }

        var session = TryDeserialize<SupabaseSession>(body);
        if (session is null ||
            string.IsNullOrWhiteSpace(session.AccessToken) ||
            session.User is null ||
            string.IsNullOrWhiteSpace(session.User.Id) ||
            string.IsNullOrWhiteSpace(session.User.Email))
        {
            return AuthResult.Failure("Supabase returned an invalid login response (missing user identity).");
        }

        CurrentSession = session;
        await PersistSessionAsync(CurrentSession, cancellationToken);
        await EnsureAppUserRecordAsync(session.User.Id, session.User.Email, session.AccessToken, cancellationToken);
        NotifyAuthStateChanged();

        _logger.LogInformation("Supabase login successful for {Email}", session.User?.Email ?? email);
        return AuthResult.Success();
    }

    public string GetOAuthSignInUrl(string provider, string redirectTo)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return string.Empty;
        }

        if (!TryBuildEndpoint("/auth/v1/authorize", out var endpoint))
        {
            _logger.LogWarning("Unable to build OAuth sign-in URL because SUPABASE_URL is invalid. Value: {SupabaseUrl}", _options.Url);
            return string.Empty;
        }

        var query = new Dictionary<string, string>
        {
            { "provider", provider },
            { "redirect_to", redirectTo },
            { "scopes", "openid profile email" }
        };

        var queryString = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return $"{endpoint}?{queryString}";
    }

    public async Task SetSessionAsync(SupabaseSession session, CancellationToken cancellationToken = default)
    {
        if (session is null || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return;
        }

        CurrentSession = session;
        await PersistSessionAsync(CurrentSession, cancellationToken);
        NotifyAuthStateChanged();
    }

    public async Task EnsureAppUserRecordAsync(string userId, string email, string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryBuildEndpoint("/rest/v1/user_profiles?on_conflict=user_id", out var endpoint))
            {
                _logger.LogWarning("Unable to provision user_profiles row because SUPABASE_URL is invalid. Value: {SupabaseUrl}", _options.Url);
                return;
            }

            var payload = new[]
            {
                new SupabaseProfileSeed
                {
                    UserId = userId,
                    Email = email,
                    TimeZone = TimeZoneInfo.Local.Id,
                    UpdatedUtc = DateTime.UtcNow
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.Add("apikey", _options.AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Unable to provision user_profiles row for {UserId}. Status: {StatusCode}. Body: {Body}", userId, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to provision app user record for {UserId}", userId);
        }
    }

    public async Task<AuthResult> SignUpWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return AuthResult.Failure("Supabase is not configured. Set SUPABASE_URL and SUPABASE_KEY.");
        }

        if (!TryBuildEndpoint("/auth/v1/signup", out var endpoint))
        {
            return AuthResult.Failure(InvalidSupabaseUrlMessage);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                email,
                password
            })
        };

        request.Headers.Add("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AnonKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<SupabaseAuthError>(body);
            var errorMessage = ResolveErrorMessage(error, body, "Unable to sign up. Check Supabase auth settings and try again.");
            var errorCodeSuffix = string.IsNullOrWhiteSpace(error?.Code) ? string.Empty : $" [code: {error.Code}]";

            if ((int)response.StatusCode == 429 ||
                (int)response.StatusCode == 425 ||
                errorMessage.Contains("over email send rate limit", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(error?.Code, "over_email_send_rate_limit", StringComparison.OrdinalIgnoreCase))
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                var retryText = retryAfter.HasValue
                    ? $" Try again in about {Math.Max(1, (int)Math.Ceiling(retryAfter.Value.TotalMinutes))} minute(s)."
                    : " Try again in a few minutes.";

                errorMessage = "Supabase signup is currently rate-limited." + retryText + " If this account was already created, try logging in now.";
            }

            _logger.LogWarning("Supabase signup failed for {Email}. Status: {StatusCode}. Error: {Error}", email, (int)response.StatusCode, errorMessage);
            return AuthResult.Failure($"{errorMessage}{errorCodeSuffix} (HTTP {(int)response.StatusCode})");
        }

        var signUpResponse = TryDeserialize<SupabaseSignUpResponse>(body);
        if (signUpResponse is null)
        {
            return AuthResult.Failure("Supabase returned an invalid signup response.");
        }

        var user = signUpResponse.User;
        if (user is null &&
            !string.IsNullOrWhiteSpace(signUpResponse.Id) &&
            !string.IsNullOrWhiteSpace(signUpResponse.Email))
        {
            user = new SupabaseUser
            {
                Id = signUpResponse.Id,
                Email = signUpResponse.Email
            };
        }

        if (user is null || string.IsNullOrWhiteSpace(user.Id) || string.IsNullOrWhiteSpace(user.Email))
        {
            return AuthResult.Failure("Supabase returned an invalid signup response.");
        }

        var session = signUpResponse.Session;
        if (session is not null && session.User is null)
        {
            session.User = user;
        }

        if (!string.IsNullOrWhiteSpace(session?.AccessToken))
        {
            CurrentSession = session;
            await PersistSessionAsync(CurrentSession, cancellationToken);
            await EnsureAppUserRecordAsync(user.Id, user.Email, session.AccessToken, cancellationToken);
            NotifyAuthStateChanged();
            _logger.LogInformation("Supabase signup successful and session created for {Email}", email);
            return AuthResult.Success("Account created. You are now logged in.");
        }

        _logger.LogInformation("Supabase signup successful for {Email}; awaiting email confirmation", user.Email);
        return AuthResult.Success("Account created. Check your email to verify your account, then sign in.");
    }

    public async Task SignOutAsync()
    {
        CurrentSession = null;
        await ClearPersistedSessionAsync();
        NotifyAuthStateChanged();
    }

    public async Task<bool> EnsureSessionRestoredAsync(CancellationToken cancellationToken = default)
    {
        if (_restoreAttempted)
        {
            return true;
        }

        await _restoreLock.WaitAsync(cancellationToken);
        try
        {
            if (_restoreAttempted)
            {
                return true;
            }

            var restoreResult = await TryGetStoredSessionAsync(cancellationToken);
            if (!restoreResult.CouldAccessStorage)
            {
                // During prerender, JS interop may be unavailable. Retry on next interactive render.
                return false;
            }

            var serializedSession = restoreResult.SerializedSession;
            if (string.IsNullOrWhiteSpace(serializedSession))
            {
                _restoreAttempted = true;
                return true;
            }

            var restoredSession = TryDeserialize<SupabaseSession>(serializedSession);
            if (restoredSession is null || restoredSession.User is null || string.IsNullOrWhiteSpace(restoredSession.AccessToken))
            {
                await ClearPersistedSessionAsync();
                _restoreAttempted = true;
                return true;
            }

            if (IsAccessTokenExpired(restoredSession.AccessToken))
            {
                var refreshed = await RefreshSessionAsync(restoredSession.RefreshToken, cancellationToken);
                if (!refreshed)
                {
                    CurrentSession = null;
                    await ClearPersistedSessionAsync();
                }
            }
            else
            {
                CurrentSession = restoredSession;
            }

            _restoreAttempted = true;
            NotifyAuthStateChanged();
            return true;
        }
        finally
        {
            _restoreLock.Release();
        }
    }

    private bool TryBuildEndpoint(string relativePath, out Uri endpoint)
    {
        endpoint = default!;

        var baseUri = TryGetSupabaseBaseUri();
        if (baseUri is null)
        {
            return false;
        }

        var normalizedPath = relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";
        endpoint = new Uri(baseUri, normalizedPath);
        return true;
    }

    private Uri? TryGetSupabaseBaseUri()
    {
        var raw = _options.Url?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim('"', '\'', ' ');

        if (Uri.TryCreate(raw, UriKind.Absolute, out var absoluteUri) && !string.IsNullOrWhiteSpace(absoluteUri.Host))
        {
            return EnsureTrailingSlash(absoluteUri);
        }

        if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var assumedHttps = $"https://{raw}";
            if (Uri.TryCreate(assumedHttps, UriKind.Absolute, out var httpsUri) && !string.IsNullOrWhiteSpace(httpsUri.Host))
            {
                return EnsureTrailingSlash(httpsUri);
            }
        }

        return null;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        if (uri.AbsoluteUri.EndsWith('/'))
        {
            return uri;
        }

        return new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static string ResolveErrorMessage(SupabaseAuthError? error, string rawBody, string fallback)
    {
        if (error is not null)
        {
            if (!string.IsNullOrWhiteSpace(error.ErrorDescription))
            {
                return error.ErrorDescription;
            }

            if (!string.IsNullOrWhiteSpace(error.Message))
            {
                return error.Message;
            }

            if (!string.IsNullOrWhiteSpace(error.Msg))
            {
                return error.Msg;
            }

            if (!string.IsNullOrWhiteSpace(error.Error))
            {
                return error.Error;
            }
        }

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            return rawBody.Length > 300 ? rawBody[..300] : rawBody;
        }

        return fallback;
    }

    private void NotifyAuthStateChanged() => AuthStateChanged?.Invoke();

    private async Task PersistSessionAsync(SupabaseSession? session, CancellationToken cancellationToken = default)
    {
        if (session is null)
        {
            await ClearPersistedSessionAsync();
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(session, JsonOptions);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", cancellationToken, SessionStorageKey, json);
        }
        catch (InvalidOperationException)
        {
            // JS runtime might not be available during prerender.
        }
        catch (JSDisconnectedException)
        {
            // Circuit disconnected before storage operation completed.
        }
    }

    private async Task<SessionRestoreReadResult> TryGetStoredSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var serializedSession = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", cancellationToken, SessionStorageKey);
            return new SessionRestoreReadResult(true, serializedSession);
        }
        catch (InvalidOperationException)
        {
            return new SessionRestoreReadResult(false, null);
        }
        catch (JSDisconnectedException)
        {
            return new SessionRestoreReadResult(false, null);
        }
    }

    private async Task ClearPersistedSessionAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", SessionStorageKey);
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task<bool> RefreshSessionAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        if (!TryBuildEndpoint("/auth/v1/token?grant_type=refresh_token", out var endpoint))
        {
            _logger.LogWarning("Unable to refresh session because SUPABASE_URL is invalid. Value: {SupabaseUrl}", _options.Url);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                refresh_token = refreshToken
            })
        };

        request.Headers.Add("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AnonKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var session = TryDeserialize<SupabaseSession>(body);
        if (session is null || session.User is null || string.IsNullOrWhiteSpace(session.AccessToken))
        {
            return false;
        }

        CurrentSession = session;
        await PersistSessionAsync(CurrentSession, cancellationToken);
        return true;
    }

    private static bool IsAccessTokenExpired(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return true;
        }

        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return true;
        }

        try
        {
            var payloadJson = DecodeBase64Url(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty("exp", out var expElement))
            {
                return false;
            }

            if (!expElement.TryGetInt64(out var expUnix))
            {
                return false;
            }

            var expiresUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix);
            return expiresUtc <= DateTimeOffset.UtcNow;
        }
        catch
        {
            return true;
        }
    }

    private static string DecodeBase64Url(string input)
    {
        var normalized = input.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (normalized.Length % 4);
        if (padding is > 0 and < 4)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        var bytes = Convert.FromBase64String(normalized);
        return Encoding.UTF8.GetString(bytes);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private sealed record SessionRestoreReadResult(bool CouldAccessStorage, string? SerializedSession);

    private sealed class SupabaseProfileSeed
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("time_zone")]
        public string TimeZone { get; set; } = string.Empty;

        [JsonPropertyName("updated_utc")]
        public DateTime UpdatedUtc { get; set; }
    }
}
