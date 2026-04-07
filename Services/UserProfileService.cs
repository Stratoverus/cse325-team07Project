using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaskDone.Models;

namespace TaskDone.Services;

public sealed class UserProfileService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(HttpClient httpClient, IOptions<SupabaseOptions> options, ILogger<UserProfileService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UserProfile?> GetProfileAsync(string userId, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        var endpoint = BuildEndpoint($"/rest/v1/user_profiles?user_id=eq.{Uri.EscapeDataString(userId)}&select=*");
        using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get profile for {UserId}. HTTP {StatusCode}", userId, (int)response.StatusCode);
            return null;
        }

        var profiles = await response.Content.ReadFromJsonAsync<List<SupabaseUserProfileRow>>(JsonOptions, cancellationToken);
        return profiles?.Count > 0 ? FromRow(profiles[0]) : null;
    }

    public async Task<UserProfile> SaveProfileAsync(UserProfile profile, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("User is not authenticated.");
        }

        profile.UpdatedUtc = DateTime.UtcNow;
        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = DateTime.UtcNow;
        }

        var endpoint = BuildEndpoint("/rest/v1/user_profiles?on_conflict=user_id");
        var row = ToRow(profile);

        using var request = CreateRequest(HttpMethod.Post, endpoint, accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");
        request.Content = JsonContent.Create(new[] { row });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to save profile (HTTP {(int)response.StatusCode}): {body}");
        }

        var rows = JsonSerializer.Deserialize<List<SupabaseUserProfileRow>>(body, JsonOptions);
        if (rows is null || rows.Count == 0)
        {
            return profile;
        }

        return FromRow(rows[0]);
    }

    public async Task<UserProfile> EnsureDraftProfileAsync(string userId, string email, string accessToken, CancellationToken cancellationToken = default)
    {
        var existing = await GetProfileAsync(userId, accessToken, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var profile = new UserProfile
        {
            UserId = userId,
            Email = email,
            TimeZone = TimeZoneInfo.Local.Id,
            IsFirstLoginComplete = false
        };

        return await SaveProfileAsync(profile, accessToken, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri endpoint, string accessToken)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Add("apikey", _options.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private Uri BuildEndpoint(string relativePath)
    {
        var baseUrl = _options.Url.TrimEnd('/');
        return new Uri($"{baseUrl}{relativePath}");
    }


    private static SupabaseUserProfileRow ToRow(UserProfile profile)
    {
        return new SupabaseUserProfileRow
        {
            UserId = profile.UserId,
            Email = profile.Email,
            FullName = profile.FullName,
            Age = profile.Age,
            PreferredDisplayName = profile.PreferredDisplayName,
            TimeZone = profile.TimeZone,
            IsFirstLoginComplete = profile.IsFirstLoginComplete,
            CreatedUtc = profile.CreatedUtc,
            UpdatedUtc = profile.UpdatedUtc
        };
    }

    private static UserProfile FromRow(SupabaseUserProfileRow row)
    {
        return new UserProfile
        {
            UserId = row.UserId,
            Email = row.Email,
            FullName = row.FullName,
            Age = row.Age,
            PreferredDisplayName = row.PreferredDisplayName,
            TimeZone = row.TimeZone,
            IsFirstLoginComplete = row.IsFirstLoginComplete,
            CreatedUtc = row.CreatedUtc,
            UpdatedUtc = row.UpdatedUtc
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class SupabaseUserProfileRow
    {
        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("age")]
        public int? Age { get; set; }

        [JsonPropertyName("preferred_display_name")]
        public string PreferredDisplayName { get; set; } = string.Empty;

        [JsonPropertyName("time_zone")]
        public string TimeZone { get; set; } = string.Empty;

        [JsonPropertyName("is_first_login_complete")]
        public bool IsFirstLoginComplete { get; set; }

        [JsonPropertyName("created_utc")]
        public DateTime CreatedUtc { get; set; }

        [JsonPropertyName("updated_utc")]
        public DateTime UpdatedUtc { get; set; }
    }
}
