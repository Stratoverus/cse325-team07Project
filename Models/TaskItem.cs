using System;
using Postgrest.Attributes;
using Postgrest.Models;
using System.Text.Json.Serialization;

namespace TaskDone.Models;

[Table("tasks")]
public class TaskItem : BaseModel
{
    [PrimaryKey("task_id", false)]
    public string TaskId { get; set; } = Guid.NewGuid().ToString();

    [Column("family_id")]
    public string FamilyId { get; set; } = string.Empty;

    [Column("created_by_user_id")]
    public string CreatedByUserId { get; set; } = string.Empty;

    [Column("assigned_to_user_id")]
    public string AssignedToUserId { get; set; } = string.Empty;

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string? Notes { get; set; }

    [Column("is_done")]
    public bool IsDone { get; set; }

    [Column("due_date")]
    public DateTime? DueDate { get; set; } = DateTime.Today;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("point_value")]
    public int? PointValue { get; set; } = 10;

    [Column("is_accepted")]
    public bool IsAccepted { get; set; } = false;
}
