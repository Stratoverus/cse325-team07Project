namespace TaskDone.Models;

public class TaskItem
{
    public string Title { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime DueDate { get; set; } = DateTime.Today;
    public bool IsDone { get; set; }
    public string? Notes { get; set; }
}
