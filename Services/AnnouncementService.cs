using System;
using System.Linq;
using TaskDone.Models;

namespace TaskDone.Services;

public class AnnouncementService
{
    private readonly List<Announcement> _announcements = new();

    public Task<List<Announcement>> GetAnnouncementsByFamilyIdAsync(string familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId))
        {
            return Task.FromResult(new List<Announcement>());
        }

        var announcements = _announcements
            .Where(a => a.FamilyId == familyId)
            .OrderByDescending(a => a.CreatedAt)
            .ToList();

        return Task.FromResult(announcements);
    }

    public Task CreateAnnouncementAsync(Announcement announcement)
    {
        if (announcement == null)
            throw new ArgumentNullException(nameof(announcement));

        if (string.IsNullOrWhiteSpace(announcement.FamilyId))
            throw new InvalidOperationException("Announcement FamilyId is required.");

        if (string.IsNullOrWhiteSpace(announcement.CreatedBy))
            throw new InvalidOperationException("Announcement CreatedBy is required.");

        if (string.IsNullOrWhiteSpace(announcement.AnnouncementId))
            announcement.AnnouncementId = Guid.NewGuid().ToString();

        _announcements.Add(announcement);
        return Task.CompletedTask;
    }
}
