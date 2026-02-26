using SqlSugar;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// 批量操作辅助类 - 优化数据库操作性能
    /// </summary>
    public static class BatchOperationHelper
    {
        /// <summary>
        /// 默认批次大小
        /// </summary>
        public const int DEFAULT_BATCH_SIZE = 500;

        /// <summary>
        /// 大批次大小（用于简单操作）
        /// </summary>
        public const int LARGE_BATCH_SIZE = 1000;

        /// <summary>
        /// 小批次大小（用于复杂操作）
        /// </summary>
        public const int SMALL_BATCH_SIZE = 100;

        /// <summary>
        /// 执行批量插入操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="db">数据库上下文</param>
        /// <param name="entities">要插入的实体列表</param>
        /// <param name="batchSize">批次大小</param>
        /// <returns>插入的记录数</returns>
        public static async Task<int> BatchInsertAsync<T>(ISqlSugarClient db, List<T> entities, int batchSize = DEFAULT_BATCH_SIZE) 
            where T : class, new()
        {
            if (entities == null || !entities.Any())
                return 0;

            return await db.Insertable(entities)
                .PageSize(batchSize)
                .ExecuteCommandAsync();
        }

        /// <summary>
        /// 执行批量更新操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="db">数据库上下文</param>
        /// <param name="entities">要更新的实体列表</param>
        /// <param name="batchSize">批次大小</param>
        /// <returns>更新的记录数</returns>
        public static async Task<int> BatchUpdateAsync<T>(ISqlSugarClient db, List<T> entities, int batchSize = DEFAULT_BATCH_SIZE) 
            where T : class, new()
        {
            if (entities == null || !entities.Any())
                return 0;

            return await db.Updateable(entities)
                .PageSize(batchSize)
                .ExecuteCommandAsync();
        }

        /// <summary>
        /// 执行批量删除操作（软删除）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="db">数据库上下文</param>
        /// <param name="entities">要删除的实体列表</param>
        /// <param name="batchSize">批次大小</param>
        /// <returns>删除的记录数</returns>
        public static async Task<int> BatchSoftDeleteAsync<T>(ISqlSugarClient db, List<T> entities, int batchSize = DEFAULT_BATCH_SIZE) 
            where T : class, new()
        {
            if (entities == null || !entities.Any())
                return 0;

            return await db.Updateable(entities)
                .PageSize(batchSize)
                .ExecuteCommandAsync();
        }

        /// <summary>
        /// 执行批量物理删除操作
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="db">数据库上下文</param>
        /// <param name="ids">要删除的ID列表</param>
        /// <param name="batchSize">批次大小</param>
        /// <returns>删除的记录数</returns>
        public static async Task<int> BatchDeleteByIdsAsync<T>(ISqlSugarClient db, List<object> ids, int batchSize = DEFAULT_BATCH_SIZE) 
            where T : class, new()
        {
            if (ids == null || !ids.Any())
                return 0;

            var totalDeleted = 0;
            var batches = ids.Chunk(batchSize);

            foreach (var batch in batches)
            {
                var deleted = await db.Deleteable<T>()
                    .In(batch.ToList())
                    .ExecuteCommandAsync();
                totalDeleted += deleted;
            }

            return totalDeleted;
        }

        /// <summary>
        /// 执行批量合并操作（存在则更新，不存在则插入）
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="db">数据库上下文</param>
        /// <param name="entities">要合并的实体列表</param>
        /// <param name="batchSize">批次大小</param>
        /// <returns>操作的记录数</returns>
        public static async Task<int> BatchMergeAsync<T>(ISqlSugarClient db, List<T> entities, int batchSize = DEFAULT_BATCH_SIZE) 
            where T : class, new()
        {
            if (entities == null || !entities.Any())
                return 0;

            return await db.Storageable(entities)
                .PageSize(batchSize)
                .ExecuteCommandAsync();
        }

        /// <summary>
        /// 分批执行操作，避免内存溢出
        /// </summary>
        /// <param name="items">要处理的数据</param>
        /// <param name="batchSize">批次大小</param>
        /// <param name="processor">处理函数</param>
        /// <returns>处理结果</returns>
        public static async Task<List<TResult>> ProcessInBatchesAsync<T, TResult>(
            IEnumerable<T> items, 
            int batchSize, 
            Func<IEnumerable<T>, Task<IEnumerable<TResult>>> processor)
        {
            var results = new List<TResult>();
            var batches = items.Chunk(batchSize);

            foreach (var batch in batches)
            {
                var batchResults = await processor(batch);
                results.AddRange(batchResults);
            }

            return results;
        }

        /// <summary>
        /// 执行事务中的批量操作
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="operation">要执行的操作</param>
        /// <returns>操作结果</returns>
        public static async Task<T> ExecuteInTransactionAsync<T>(ISqlSugarClient db, Func<Task<T>> operation)
        {
            var result = await db.Ado.UseTranAsync(operation);
            return result.Data;
        }

        /// <summary>
        /// 执行事务中的批量操作（无返回值）
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="operation">要执行的操作</param>
        public static async Task ExecuteInTransactionAsync(ISqlSugarClient db, Func<Task> operation)
        {
            await db.Ado.UseTranAsync(operation);
        }

        /// <summary>
        /// 批量验证实体唯一性
        /// </summary>
        /// <param name="db">数据库上下文</param>
        /// <param name="keySelector">键选择器</param>
        /// <param name="values">要验证的值</param>
        /// <returns>已存在的值列表</returns>
        public static async Task<List<TKey>> ValidateUniquenessAsync<T, TKey>(
            ISqlSugarClient db, 
            System.Linq.Expressions.Expression<Func<T, TKey>> keySelector, 
            List<TKey> values) 
            where T : class, new()
        {
            if (values == null || !values.Any())
                return new List<TKey>();

            // 分批查询，避免IN子句过长
            var existingValues = new List<TKey>();
            var batches = values.Chunk(1000); // IN子句建议不超过1000个值

            foreach (var batch in batches)
            {
                var batchExisting = await db.Queryable<T>()
                    .Where(x => batch.Contains(keySelector.Compile()(x)))
                    .Select(keySelector)
                    .ToListAsync();
                
                existingValues.AddRange(batchExisting);
            }

            return existingValues;
        }

        /// <summary>
        /// 获取推荐的批次大小
        /// </summary>
        /// <param name="totalCount">总数量</param>
        /// <param name="complexity">操作复杂度（1-5，1最简单，5最复杂）</param>
        /// <returns>推荐的批次大小</returns>
        public static int GetRecommendedBatchSize(int totalCount, int complexity = 3)
        {
            // 根据数据量和复杂度动态调整批次大小
            var baseSize = complexity switch
            {
                1 => LARGE_BATCH_SIZE,  // 简单操作，如状态更新
                2 => DEFAULT_BATCH_SIZE, // 一般操作
                3 => DEFAULT_BATCH_SIZE, // 中等复杂度
                4 => SMALL_BATCH_SIZE,   // 复杂操作，如关联查询
                5 => 50,                 // 非常复杂的操作
                _ => DEFAULT_BATCH_SIZE
            };

            // 如果数据量很小，不需要分批
            if (totalCount <= 100)
                return totalCount;

            // 确保批次大小合理
            return Math.Min(baseSize, Math.Max(50, totalCount / 10));
        }
    }
}
