using Thor.DataLayer.Data;
using Thor.DataLayer.Models;

namespace Thor.DataLayer.Repositories;

public sealed class TaskApiRefreshTokenRepository(MasterDbContext context)
    : Repository<TaskApiRefreshToken>(context), ITaskApiRefreshTokenRepository
{
}
