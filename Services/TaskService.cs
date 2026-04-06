using System.Linq;
using TaskDone.Models;

namespace TaskDone.Services;

public class TaskService
{
    private readonly List<TaskItem> _tasks = new();

    public IReadOnlyList<TaskItem> AllTasks => _tasks;

    public IEnumerable<TaskItem> GetTasksByFamilyId(string familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId))
        {
            return Enumerable.Empty<TaskItem>();
        }

        return _tasks.Where(t => t.FamilyId == familyId).ToList();
    }

    public void AddTask(TaskItem task)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        if (string.IsNullOrWhiteSpace(task.FamilyId))
            throw new InvalidOperationException("Task FamilyId is required.");

        if (string.IsNullOrWhiteSpace(task.TaskId))
            task.TaskId = Guid.NewGuid().ToString();

        _tasks.Add(task);
    }

    public void ToggleDone(TaskItem task)
    {
        if (task == null)
            return;

        var existing = GetTaskById(task.TaskId);
        if (existing != null)
        {
            existing.IsDone = !existing.IsDone;
        }
    }

    public void UpdateTask(TaskItem task)
    {
        if (task == null)
            return;

        var existing = GetTaskById(task.TaskId);
        if (existing == null)
            return;

        existing.Title = task.Title;
        existing.AssignedTo = task.AssignedTo;
        existing.DueDate = task.DueDate;
        existing.Notes = task.Notes;
        existing.IsDone = task.IsDone;
        existing.AcceptedByChild = task.AcceptedByChild;
    }

    public void RemoveTask(TaskItem task)
    {
        if (task == null)
            return;

        var existing = GetTaskById(task.TaskId);
        if (existing != null)
        {
            _tasks.Remove(existing);
        }
    }

    public TaskItem? GetTaskById(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return null;

        return _tasks.FirstOrDefault(t => t.TaskId == taskId);
    }
}
