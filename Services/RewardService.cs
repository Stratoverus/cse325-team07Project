using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaskDone.Models;

namespace TaskDone.Services;

public sealed class RewardService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly ILogger<RewardService> _logger;

    public RewardService(HttpClient httpClient, IOptions<SupabaseOptions> options, ILogger<RewardService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<Reward>> GetRewardsAsync(string familyId, string accessToken, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint($"/rest/v1/rewards?family_id=eq.{Uri.EscapeDataString(familyId)}&select=*");
        using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get rewards. HTTP {StatusCode}", (int)response.StatusCode);
            return new List<Reward>();
        }

        var rows = await response.Content.ReadFromJsonAsync<List<RewardRow>>(JsonOptions, cancellationToken);
        return rows?.Select(FromRow).ToList() ?? new List<Reward>();
    }
    public async Task<string?> GetFamilyIdAsync(string userId, string accessToken, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint($"/rest/v1/family_members?user_id=eq.{Uri.EscapeDataString(userId)}&select=family_id&limit=1");
        using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var rows = await response.Content.ReadFromJsonAsync<List<FamilyMemberRow>>(JsonOptions, cancellationToken);
        return rows?.Count > 0 ? rows[0].FamilyId : null;
    }

    private sealed class FamilyMemberRow
    {
        [JsonPropertyName("family_id")]
        public string FamilyId { get; set; } = string.Empty;
    }

    public async Task<FamilyMember?> GetFamilyMemberAsync(string userId, string accessToken, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint($"/rest/v1/family_members?user_id=eq.{Uri.EscapeDataString(userId)}&select=*&limit=1");
        using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var rows = await response.Content.ReadFromJsonAsync<List<FamilyMember>>(JsonOptions, cancellationToken);
        return rows?.Count > 0 ? rows[0] : null;
    }

    public async Task UpdatePointsAsync(string familyMemberId, int newBalance, string accessToken, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint($"/rest/v1/family_members?family_member_id=eq.{Uri.EscapeDataString(familyMemberId)}");
        using var request = CreateRequest(HttpMethod.Patch, endpoint, accessToken);
        request.Content = JsonContent.Create(new { points_balance = newBalance });
        await _httpClient.SendAsync(request, cancellationToken);
    }

    public async Task<Reward> SaveRewardAsync(Reward reward, string accessToken, CancellationToken cancellationToken = default)
    {
        var endpoint = BuildEndpoint("/rest/v1/rewards?on_conflict=reward_id");
        using var request = CreateRequest(HttpMethod.Post, endpoint, accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");
        request.Content = JsonContent.Create(new[] { ToRow(reward) });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to save reward (HTTP {(int)response.StatusCode}): {body}");
        }

        var rows = JsonSerializer.Deserialize<List<RewardRow>>(body, JsonOptions);
        return rows?.Count > 0 ? FromRow(rows[0]) : reward;
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

    private static RewardRow ToRow(Reward reward) => new()
    {
        RewardId = string.IsNullOrWhiteSpace(reward.RewardId) ? Guid.NewGuid().ToString() : reward.RewardId,
        FamilyId = reward.FamilyId,
        Name = reward.Name,
        Description = reward.Description,
        CostPoints = reward.CostPoints,
        CreatedByUserId = reward.CreatedByUserId,
        CreatedAt = reward.CreatedAt,
        UpdatedAt = reward.UpdatedAt
    };

    private static Reward FromRow(RewardRow row) => new()
    {
        RewardId = row.RewardId,
        FamilyId = row.FamilyId,
        Name = row.Name,
        Description = row.Description,
        CostPoints = row.CostPoints,
        CreatedByUserId = row.CreatedByUserId,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class RewardRow
    {
        [JsonPropertyName("reward_id")]
        public string RewardId { get; set; } = string.Empty;

        [JsonPropertyName("family_id")]
        public string FamilyId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("cost_points")]
        public int CostPoints { get; set; }

        [JsonPropertyName("created_by_user_id")]
        public string CreatedByUserId { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}