using Microsoft.EntityFrameworkCore;

namespace Thor.DataLayer.Repositories;

public class Repository<TEntity>(DbContext context) : IRepository<TEntity> where TEntity : class
{
    private readonly DbSet<TEntity> _set = context.Set<TEntity>();

    public async Task<TEntity?> GetByIdAsync(object id, CancellationToken cancellationToken = default) =>
        await _set.FindAsync([id], cancellationToken);

    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _set.ToListAsync(cancellationToken);

    public async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default) =>
        await _set.AddAsync(entity, cancellationToken);

    public void Update(TEntity entity) => _set.Update(entity);

    public void Remove(TEntity entity) => _set.Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
