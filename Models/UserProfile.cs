namespace TaskDone.Models;

public sealed class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string PreferredDisplayName { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public bool IsFirstLoginComplete { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
