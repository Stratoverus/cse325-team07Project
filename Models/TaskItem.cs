using System;

namespace TaskDone.Models;

public class TaskItem
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public string FamilyId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; } = DateTime.Today;
    public bool IsDone { get; set; }
    public bool AcceptedByChild { get; set; }
    public string? Notes { get; set; }
    public int PointValue { get; set; } = 10;
}
