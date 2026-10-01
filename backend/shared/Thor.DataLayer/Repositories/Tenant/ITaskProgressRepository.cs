using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public interface ITaskProgressRepository : IRepository<TaskProgress>
{
    /// <summary>
    /// Backs <c>POST /tasks/{taskId}/progress</c>: inserts <paramref name="progress"/> as a new
    /// row if <see cref="TaskProgress.TaskId"/> exists in the tenant database, or does nothing and
    /// returns <see langword="false"/> otherwise.
    /// </summary>
    Task<bool> AddIfTaskExistsAsync(TaskProgress progress, CancellationToken cancellationToken = default);
}
