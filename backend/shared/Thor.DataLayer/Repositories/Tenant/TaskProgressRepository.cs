using Microsoft.EntityFrameworkCore;
using Thor.DataLayer.Data;
using Thor.DataLayer.Models.Tenants;

namespace Thor.DataLayer.Repositories;

public sealed class TaskProgressRepository(TenantDbContext context)
    : Repository<TaskProgress>(context), ITaskProgressRepository
{
    public async Task<bool> AddIfTaskExistsAsync(TaskProgress progress, CancellationToken cancellationToken = default)
    {
        var taskExists = await context.Tasks.AnyAsync(t => t.Id == progress.TaskId, cancellationToken);
        if (!taskExists)
        {
            return false;
        }

        context.TaskProgresses.Add(progress);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
