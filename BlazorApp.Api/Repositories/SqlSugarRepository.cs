using System.Linq.Expressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Repositories.Interfaces;
using SqlSugar;

namespace BlazorApp.Api.Repositories
{
    public class SqlSugarRepository<TEntity> : IRepository<TEntity>
        where TEntity : class, new()
    {
        protected readonly SqlSugarContext Context;

        public SqlSugarRepository(SqlSugarContext context)
        {
            Context = context;
        }

        protected ISqlSugarClient Db => Context.Db;

        public ISugarQueryable<TEntity> Query()
        {
            return Db.Queryable<TEntity>();
        }

        public async Task<TEntity?> FirstAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await Query().Where(predicate).FirstAsync();
        }

        public Task<List<TEntity>> ListAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return Query().Where(predicate).ToListAsync();
        }

        public Task<int> InsertAsync(TEntity entity)
        {
            return Db.Insertable(entity).ExecuteCommandAsync();
        }

        public Task<int> UpdateAsync(TEntity entity)
        {
            return Db.Updateable(entity).ExecuteCommandAsync();
        }
    }
}
