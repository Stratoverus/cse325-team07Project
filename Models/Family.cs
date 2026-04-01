using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models; 

namespace TaskDone.Models;

[Table("families")]
public sealed class Family : BaseModel 
{
    [PrimaryKey("family_id", false)]
    public string FamilyId { get; set; } = Guid.NewGuid().ToString();

    [Column("family_name")]
    [JsonPropertyName("family_name")]
    public string FamilyName { get; set; } = string.Empty;

    [Column("created_by_user_id")]
    [JsonPropertyName("created_by_user_id")]
    public string CreatedBy { get; set; } = string.Empty; 

    [Column("created_at")]
    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}