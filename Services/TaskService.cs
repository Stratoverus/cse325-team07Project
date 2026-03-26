using TaskDone.Models;

namespace TaskDone.Services;

public class TaskService
{
    private readonly List<TaskItem> _tasks = new();

    public IReadOnlyList<TaskItem> AllTasks => _tasks;

    public void AddTask(TaskItem task)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        _tasks.Add(task);
    }

    public void ToggleDone(TaskItem task)
    {
        if (task == null)
            return;

        task.IsDone = !task.IsDone;
    }

    public void RemoveTask(TaskItem task)
    {
        if (task == null)
            return;

        _tasks.Remove(task);
    }
}
