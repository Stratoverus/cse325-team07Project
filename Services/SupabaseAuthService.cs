using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaskDone.Models;

namespace TaskDone.Services;

public sealed class SupabaseAuthService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseAuthService> _logger;

    public SupabaseSession? CurrentSession { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(CurrentSession?.AccessToken);
    public string? CurrentUserId => CurrentSession?.User?.Id;
    public string? CurrentUserEmail => CurrentSession?.User?.Email;

    public event Action? AuthStateChanged;

    public SupabaseAuthService(HttpClient httpClient, IOptions<SupabaseOptions> options, ILogger<SupabaseAuthService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AuthResult> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return AuthResult.Failure("Supabase is not configured. Set SUPABASE_URL and SUPABASE_KEY in launchSettings profile environment variables.");
        }

        var endpoint = BuildEndpoint("/auth/v1/token?grant_type=password");

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
            var errorCodeSuffix = string.IsNullOrWhiteSpace(error?.Code) ? string.Empty : $" [code: {error.Code}]";

            _logger.LogWarning("Supabase login failed for {Email}. Status: {StatusCode}. Error: {Error}", email, (int)response.StatusCode, errorMessage);
            return AuthResult.Failure($"{errorMessage}{errorCodeSuffix} (HTTP {(int)response.StatusCode})");
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
        NotifyAuthStateChanged();

        _logger.LogInformation("Supabase login successful for {Email}", session.User?.Email ?? email);
        return AuthResult.Success();
    }

    public async Task<AuthResult> SignUpWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Url) || string.IsNullOrWhiteSpace(_options.AnonKey))
        {
            return AuthResult.Failure("Supabase is not configured. Set SUPABASE_URL and SUPABASE_KEY in launchSettings profile environment variables.");
        }

        var endpoint = BuildEndpoint("/auth/v1/signup");

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
            NotifyAuthStateChanged();
            _logger.LogInformation("Supabase signup successful and session created for {Email}", email);
            return AuthResult.Success("Account created. You are now logged in.");
        }

        _logger.LogInformation("Supabase signup successful for {Email}; awaiting email confirmation", user.Email);
        return AuthResult.Success("Account created. Check your email to verify your account, then sign in.");
    }

    public Task SignOutAsync()
    {
        CurrentSession = null;
        NotifyAuthStateChanged();
        return Task.CompletedTask;
    }

    private Uri BuildEndpoint(string relativePath)
    {
        var baseUrl = _options.Url.TrimEnd('/');
        return new Uri($"{baseUrl}{relativePath}");
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
