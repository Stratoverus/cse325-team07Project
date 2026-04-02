using System.Text.Json.Serialization;
using Postgrest.Attributes;
using Postgrest.Models;

namespace TaskDone.Models;

[Table("family_members")]
public sealed class FamilyMember : BaseModel
{
    [PrimaryKey("family_member_id", false)]
    public string FamilyMemberId { get; set; } = Guid.NewGuid().ToString();

    [Column("family_id")]
    [JsonPropertyName("family_id")]
    public string FamilyId { get; set; } = string.Empty;

    [Column("user_id")]
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("role")]
    [JsonPropertyName("role")]
    public string Role { get; set; } = "parent";

    [Column("joined_at")]
    [JsonPropertyName("joined_at")]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}