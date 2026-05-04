using System.Linq.Expressions;
using SqlSugar;

namespace BlazorApp.Api.Repositories.Interfaces
{
    public interface IRepository<TEntity>
        where TEntity : class, new()
    {
        ISugarQueryable<TEntity> Query();

        Task<TEntity?> FirstAsync(Expression<Func<TEntity, bool>> predicate);

        Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate);

        Task<int> InsertAsync(TEntity entity);

        Task<int> UpdateAsync(TEntity entity);
    }
}
