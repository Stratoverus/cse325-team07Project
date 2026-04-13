using TaskDone.Models;

namespace TaskDone.Services;

public sealed class FamilyService
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<FamilyService> _logger;

    public FamilyService(Supabase.Client supabase, ILogger<FamilyService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    public async Task<bool> UpdateFamilyNameAsync(string familyId, string newName)
    {
        if (string.IsNullOrWhiteSpace(familyId))
        {
            _logger.LogWarning("UpdateFamilyName failed: familyId is empty");
            return false;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            _logger.LogWarning("UpdateFamilyName failed: newName is empty");
            return false;
        }

        try
        {
            await _supabase
                .From<Family>()
                .Where(f => f.FamilyId == familyId)
                .Set(f => f.FamilyName, newName)
                .Set(f => f.UpdatedAt, DateTime.UtcNow)
                .Update();

            _logger.LogInformation("Family {FamilyId} name updated to {Name}", familyId, newName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update family name for {FamilyId}", familyId);
            return false;
        }
    }
}