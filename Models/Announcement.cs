using System;

namespace TaskDone.Models;

public class Announcement
{
    public string AnnouncementId { get; set; } = Guid.NewGuid().ToString();
    public string FamilyId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string DisplayCreatedBy => string.IsNullOrWhiteSpace(CreatedByName) ? CreatedBy : CreatedByName;
}
