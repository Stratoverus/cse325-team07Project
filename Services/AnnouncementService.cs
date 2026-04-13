using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaskDone.Models;

namespace TaskDone.Services;

public sealed class AnnouncementService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOptions _options;
    private readonly UserProfileService _userProfileService;
    private readonly ILogger<AnnouncementService> _logger;

    public AnnouncementService(
        HttpClient httpClient,
        IOptions<SupabaseOptions> options,
        UserProfileService userProfileService,
        ILogger<AnnouncementService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _userProfileService = userProfileService;
        _logger = logger;
    }

    public async Task<List<Announcement>> GetAnnouncementsByFamilyIdAsync(string familyId, string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return new List<Announcement>();
        }

        var announcements = await GetAnnouncementsByFamilyIdWithJoinAsync(familyId, accessToken, cancellationToken);
        if (announcements is not null)
        {
            return announcements;
        }

        var endpoint = BuildEndpoint($"/rest/v1/announcements?family_id=eq.{Uri.EscapeDataString(familyId)}&select=announcement_id,family_id,message,created_by,created_at");
        using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to get announcements for family {FamilyId}. HTTP {StatusCode}", familyId, (int)response.StatusCode);
            return new List<Announcement>();
        }

        var rows = await response.Content.ReadFromJsonAsync<List<AnnouncementRow>>(JsonOptions, cancellationToken);
        if (rows is null)
        {
            return new List<Announcement>();
        }

        var simpleAnnouncements = new List<Announcement>(rows.Count);
        foreach (var row in rows)
        {
            var announcement = FromRow(row);
            announcement.CreatedByName = await ResolveCreatedByNameAsync(row.CreatedBy, accessToken, cancellationToken);
            simpleAnnouncements.Add(announcement);
        }

        return simpleAnnouncements.OrderByDescending(a => a.CreatedAt).ToList();
    }

    private async Task<List<Announcement>?> GetAnnouncementsByFamilyIdWithJoinAsync(string familyId, string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = BuildEndpoint($"/rest/v1/announcements?family_id=eq.{Uri.EscapeDataString(familyId)}&select=announcement_id,family_id,message,created_by,created_at,user_profiles(first_name)");
            using var request = CreateRequest(HttpMethod.Get, endpoint, accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var rows = await response.Content.ReadFromJsonAsync<List<AnnouncementRowWithUserProfile>>(JsonOptions, cancellationToken);
            if (rows is null)
            {
                return null;
            }

            var announcements = new List<Announcement>(rows.Count);
            foreach (var row in rows)
            {
                var announcement = FromRow(row);
                announcement.CreatedByName = row.UserProfiles?.FirstOrDefault()?.FirstName ?? string.Empty;

                if (string.IsNullOrWhiteSpace(announcement.CreatedByName))
                {
                    announcement.CreatedByName = await ResolveCreatedByNameAsync(row.CreatedBy, accessToken, cancellationToken);
                }

                announcements.Add(announcement);
            }

            return announcements.OrderByDescending(a => a.CreatedAt).ToList();
        }
        catch
        {
            return null;
        }
    }

    public async Task<Announcement> CreateAnnouncementAsync(Announcement announcement, string accessToken, CancellationToken cancellationToken = default)
    {
        if (announcement == null)
            throw new ArgumentNullException(nameof(announcement));

        if (string.IsNullOrWhiteSpace(announcement.FamilyId))
            throw new InvalidOperationException("Announcement FamilyId is required.");

        if (string.IsNullOrWhiteSpace(announcement.CreatedBy))
            throw new InvalidOperationException("Announcement CreatedBy is required.");

        if (string.IsNullOrWhiteSpace(announcement.AnnouncementId))
            announcement.AnnouncementId = Guid.NewGuid().ToString();

        var endpoint = BuildEndpoint("/rest/v1/announcements?on_conflict=announcement_id");
        using var request = CreateRequest(HttpMethod.Post, endpoint, accessToken);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");

        // ✅ FIX: send correctly mapped row (snake_case)
        request.Content = JsonContent.Create(new[] { ToRow(announcement) });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to save announcement (HTTP {(int)response.StatusCode}): {body}");
        }

        var createdRows = JsonSerializer.Deserialize<List<AnnouncementRow>>(body, JsonOptions);
        var createdAnnouncement = createdRows?.Count > 0 ? FromRow(createdRows[0]) : announcement;

        createdAnnouncement.CreatedByName = await ResolveCreatedByNameAsync(createdAnnouncement.CreatedBy, accessToken, cancellationToken);

        return createdAnnouncement;
    }

    private async Task<string> ResolveCreatedByNameAsync(string createdByUserId, string accessToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(createdByUserId) || string.IsNullOrWhiteSpace(accessToken))
        {
            return createdByUserId ?? string.Empty;
        }

        var profile = await _userProfileService.GetProfileAsync(createdByUserId, accessToken, cancellationToken);

        if (profile is null)
        {
            return createdByUserId;
        }

        /* 
        if (!string.IsNullOrWhiteSpace(profile.FirstName))
        {
            return profile.FirstName;
        }
        */

        if (!string.IsNullOrWhiteSpace(profile.PreferredDisplayName))
        {
            return profile.PreferredDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(profile.FullName))
        {
            var parts = profile.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : profile.FullName;
        }

        return createdByUserId;
    }

    // ✅ FIX: return AnnouncementRow (NOT Announcement)
    private static AnnouncementRow ToRow(Announcement announcement) => new()
    {
        AnnouncementId = announcement.AnnouncementId,
        FamilyId = announcement.FamilyId,
        Message = announcement.Message,
        CreatedBy = announcement.CreatedBy,
        CreatedAt = announcement.CreatedAt
    };

    private static Announcement FromRow(AnnouncementRow row) => new()
    {
        AnnouncementId = row.AnnouncementId,
        FamilyId = row.FamilyId,
        Message = row.Message,
        CreatedBy = row.CreatedBy,
        CreatedAt = row.CreatedAt
    };

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ✅ FIXED: snake_case mapping for Supabase
    private class AnnouncementRow
    {
        [JsonPropertyName("announcement_id")]
        public string AnnouncementId { get; set; } = string.Empty;

        [JsonPropertyName("family_id")]
        public string FamilyId { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("created_by")]
        public string CreatedBy { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }
    }

    private sealed class AnnouncementRowWithUserProfile : AnnouncementRow
    {
        [JsonPropertyName("user_profiles")]
        public List<AnnouncementUserProfileRow>? UserProfiles { get; set; }
    }

    private sealed class AnnouncementUserProfileRow
    {
        [JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;
    }
}