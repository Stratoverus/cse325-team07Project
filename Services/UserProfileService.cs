using System.Collections.Concurrent;
using TaskDone.Models;

namespace TaskDone.Services;

public sealed class UserProfileService
{
    private readonly ConcurrentDictionary<string, UserProfile> _profiles = new();

    public UserProfile? GetProfile(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        _profiles.TryGetValue(userId, out var profile);
        return profile;
    }

    public UserProfile SaveProfile(UserProfile profile)
    {
        profile.UpdatedUtc = DateTime.UtcNow;
        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = DateTime.UtcNow;
        }

        _profiles.AddOrUpdate(profile.UserId, profile, (_, _) => profile);
        return profile;
    }

    public UserProfile EnsureDraftProfile(string userId, string email)
    {
        return _profiles.GetOrAdd(userId, _ => new UserProfile
        {
            UserId = userId,
            Email = email,
            TimeZone = TimeZoneInfo.Local.Id,
            RoleInFamily = "Parent",
            IsFirstLoginComplete = false
        });
    }
}
