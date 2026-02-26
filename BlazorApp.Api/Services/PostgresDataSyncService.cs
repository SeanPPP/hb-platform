using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// PostgreSQL数据同步服务
    /// 将SQL Server数据库中的数据同步到PostgreSQL数据库
    /// </summary>
    public class PostgresDataSyncService
    {
        private readonly SqlSugarContext _sqlServerContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly PostgresSqlSugarContext _postgresContext;
        private readonly ILogger<PostgresDataSyncService> _logger;
        private readonly IMapper _mapper;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="sqlServerContext">SQL Server数据库上下文</param>
        /// <param name="hqContext">HQ数据库上下文</param>
        /// <param name="postgresContext">PostgreSQL数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="mapper">对象映射器</param>
        public PostgresDataSyncService(
            SqlSugarContext sqlServerContext,
            HqSqlSugarContext hqContext,
            PostgresSqlSugarContext postgresContext,
            ILogger<PostgresDataSyncService> logger,
            IMapper mapper
        )
        {
            _sqlServerContext = sqlServerContext;
            _hqContext = hqContext;
            _postgresContext = postgresContext;
            _logger = logger;
            _mapper = mapper;
        }

        /// <summary>
        /// 同步所有数据到PostgreSQL
        /// </summary>
        /// <returns>同步结果列表</returns>
        public async Task<List<SyncResult>> SyncAllDataToPostgresAsync()
        {
            var results = new List<SyncResult>();

            try
            {
                _logger.LogInformation("🚀 开始将所有数据同步到PostgreSQL数据库...");

                // 1. 初始化PostgreSQL数据库表结构
                _postgresContext.InitializeTablesIfNeeded();
                _logger.LogInformation("✅ PostgreSQL数据库表结构初始化完成");

                // 2. 同步用户相关数据
                results.Add(await SyncUsersToPostgresAsync());
                results.Add(await SyncRolesToPostgresAsync());
                results.Add(await SyncUserRolesToPostgresAsync());
                results.Add(await SyncStoresToPostgresAsync());
                results.Add(await SyncUserStoresToPostgresAsync());

                // 3. 同步商品相关数据
                results.Add(await SyncProductsToPostgresAsync());
                results.Add(await SyncProductSetCodesToPostgresAsync());

                // 4. 同步仓库相关数据
                results.Add(await SyncWarehouseCategoriesToPostgresAsync());
                results.Add(await SyncWarehouseProductsToPostgresAsync());
                results.Add(await SyncChinaSuppliersToPostgresAsync());

                // 5. 同步国内商品相关数据
                results.Add(await SyncDomesticProductsToPostgresAsync());
                results.Add(await SyncProductPrefixCodesToPostgresAsync());
                results.Add(await SyncDomesticSetProductsToPostgresAsync());

                // 6. 同步货位相关数据
                results.Add(await SyncLocationsToPostgresAsync());
                results.Add(await SyncProductLocationsToPostgresAsync());

                // 8. 同步义乌相关数据
                results.Add(await SyncYiwuOrdersToPostgresAsync());
                results.Add(await SyncYiwuOrderDetailsToPostgresAsync());

                // 9. 同步货柜相关数据
                results.Add(await SyncContainersToPostgresAsync());
                results.Add(await SyncContainerDetailsToPostgresAsync());

                var successCount = results.Count(r => r.IsSuccess);
                _logger.LogInformation(
                    $"🎉 PostgreSQL数据同步完成！成功: {successCount}/{results.Count}"
                );

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ PostgreSQL数据同步过程中发生异常");
                results.Add(
                    new SyncResult
                    {
                        IsSuccess = false,
                        Message = $"同步异常: {ex.Message}",
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                    }
                );
                return results;
            }
        }

        /// <summary>
        /// 同步用户数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncUsersToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步用户数据到PostgreSQL...");

                // 从SQL Server获取所有用户数据
                var sqlServerUsers = await _sqlServerContext.UserDb.GetListAsync();

                if (!sqlServerUsers.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有用户数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                // 清空PostgreSQL中的用户表
                await _postgresContext.Db.Deleteable<User>().ExecuteCommandAsync();

                // 批量插入到PostgreSQL
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerUsers)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "用户数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 用户数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"用户数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "用户数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步角色数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncRolesToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步角色数据到PostgreSQL...");

                var sqlServerRoles = await _sqlServerContext.RoleDb.GetListAsync();

                if (!sqlServerRoles.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有角色数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<Role>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerRoles)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "角色数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 角色数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"角色数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "角色数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步用户角色关联数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncUserRolesToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步用户角色关联数据到PostgreSQL...");

                var sqlServerUserRoles = await _sqlServerContext.UserRoleDb.GetListAsync();

                if (!sqlServerUserRoles.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有用户角色关联数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<UserRole>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerUserRoles)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "用户角色关联数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 用户角色关联数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"用户角色关联数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "用户角色关联数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步门店数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncStoresToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步门店数据到PostgreSQL...");

                var sqlServerStores = await _sqlServerContext.StoreDb.GetListAsync();

                if (!sqlServerStores.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有门店数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<Store>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerStores)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "门店数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 门店数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"门店数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "门店数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步用户门店关联数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncUserStoresToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步用户门店关联数据到PostgreSQL...");

                var sqlServerUserStores = await _sqlServerContext.UserStoreDb.GetListAsync();

                if (!sqlServerUserStores.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有用户门店关联数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<UserStore>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerUserStores)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "用户门店关联数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 用户门店关联数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"用户门店关联数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "用户门店关联数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步商品数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncProductsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步商品数据到PostgreSQL...");

                var sqlServerProducts = await _sqlServerContext.ProductDb.GetListAsync();

                if (!sqlServerProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有商品数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<Product>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerProducts)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "商品数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 商品数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"商品数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "商品数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步商品套装码数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncProductSetCodesToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步商品套装码数据到PostgreSQL...");

                var sqlServerProductSetCodes =
                    await _sqlServerContext.ProductSetCodeDb.GetListAsync();

                if (!sqlServerProductSetCodes.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有商品套装码数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<ProductSetCode>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerProductSetCodes)
                    .PageSize(2000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "商品套装码数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 商品套装码数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"商品套装码数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "商品套装码数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步仓库分类数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncWarehouseCategoriesToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步仓库分类数据到PostgreSQL...");

                var sqlServerCategories =
                    await _sqlServerContext.WarehouseCategoryDb.GetListAsync();

                if (!sqlServerCategories.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有仓库分类数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<WarehouseCategory>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerCategories)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "仓库分类数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 仓库分类数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"仓库分类数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "仓库分类数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步仓库商品数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncWarehouseProductsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步仓库商品数据到PostgreSQL...");

                var sqlServerProducts = await _sqlServerContext.WarehouseProductDb.GetListAsync();

                if (!sqlServerProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有仓库商品数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<WarehouseProduct>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerProducts)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "仓库商品数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 仓库商品数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"仓库商品数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "仓库商品数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步中国供应商数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncChinaSuppliersToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步中国供应商数据到PostgreSQL...");

                var sqlServerSuppliers = await _sqlServerContext.ChinaSupplierDb.GetListAsync();

                if (!sqlServerSuppliers.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有中国供应商数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<ChinaSupplier>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerSuppliers)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "中国供应商数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 中国供应商数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"中国供应商数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "中国供应商数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步国内商品数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncDomesticProductsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步国内商品数据到PostgreSQL...");

                var sqlServerProducts = await _sqlServerContext.DomesticProductDb.GetListAsync();

                if (!sqlServerProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有国内商品数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<DomesticProduct>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerProducts)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "国内商品数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 国内商品数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"国内商品数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "国内商品数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步商品前缀码数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncProductPrefixCodesToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步商品前缀码数据到PostgreSQL...");

                var sqlServerPrefixCodes =
                    await _sqlServerContext.ProductPrefixCodeDb.GetListAsync();

                if (!sqlServerPrefixCodes.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有商品前缀码数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<ProductPrefixCode>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerPrefixCodes)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "商品前缀码数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 商品前缀码数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"商品前缀码数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "商品前缀码数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步套装商品数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncDomesticSetProductsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步套装商品数据到PostgreSQL...");

                var sqlServerSetProducts =
                    await _sqlServerContext.DomesticSetProductDb.GetListAsync();

                if (!sqlServerSetProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有套装商品数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<DomesticSetProduct>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerSetProducts)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "套装商品数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 套装商品数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"套装商品数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "套装商品数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步货位数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncLocationsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步货位数据到PostgreSQL...");

                var sqlServerLocations = await _sqlServerContext.LocationDb.GetListAsync();

                if (!sqlServerLocations.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有货位数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<Location>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerLocations)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "货位数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 货位数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"货位数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "货位数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步商品货位关联数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncProductLocationsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步商品货位关联数据到PostgreSQL...");

                var sqlServerProductLocations =
                    await _sqlServerContext.ProductLocationDb.GetListAsync();

                if (!sqlServerProductLocations.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有商品货位关联数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<ProductLocation>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerProductLocations)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "商品货位关联数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 商品货位关联数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"商品货位关联数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "商品货位关联数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步义乌订单数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncYiwuOrdersToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步义乌订单数据到PostgreSQL...");

                var sqlServerYiwuOrders = await _sqlServerContext.YIWU_OrderDb.GetListAsync();

                if (!sqlServerYiwuOrders.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有义乌订单数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<YIWU_Order>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerYiwuOrders)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "义乌订单数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 义乌订单数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"义乌订单数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "义乌订单数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步义乌订单详情数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncYiwuOrderDetailsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步义乌订单详情数据到PostgreSQL...");

                var sqlServerYiwuOrderDetails =
                    await _sqlServerContext.YIWU_OrderDetailDb.GetListAsync();

                if (!sqlServerYiwuOrderDetails.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有义乌订单详情数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<YIWU_OrderDetail>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerYiwuOrderDetails)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "义乌订单详情数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 义乌订单详情数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"义乌订单详情数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "义乌订单详情数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步货柜数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncContainersToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步货柜数据到PostgreSQL...");

                var sqlServerContainers = await _sqlServerContext.ContainerDb.GetListAsync();

                if (!sqlServerContainers.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有货柜数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<Container>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerContainers)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "货柜数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 货柜数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"货柜数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "货柜数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步货柜详情数据到PostgreSQL
        /// </summary>
        private async Task<SyncResult> SyncContainerDetailsToPostgresAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogInformation("开始同步货柜详情数据到PostgreSQL...");

                var sqlServerContainerDetails =
                    await _sqlServerContext.ContainerDetailDb.GetListAsync();

                if (!sqlServerContainerDetails.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "SQL Server中没有货柜详情数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                await _postgresContext.Db.Deleteable<ContainerDetail>().ExecuteCommandAsync();
                var insertCount = await _postgresContext
                    .Db.Insertable(sqlServerContainerDetails)
                    .PageSize(5000)
                    .ExecuteCommandAsync();

                result.IsSuccess = true;
                result.Message = "货柜详情数据同步成功";
                result.AddedCount = insertCount;

                _logger.LogInformation($"✅ 货柜详情数据同步完成 - 新增: {insertCount} 条");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"货柜详情数据同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "货柜详情数据同步失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 重新创建PostgreSQL数据库表结构
        /// </summary>
        /// <returns>操作结果</returns>
        public async Task<SyncResult> RecreatePostgresTablesAsync()
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                _logger.LogWarning("⚠️ 开始重新创建PostgreSQL数据库表结构，这将删除所有数据！");

                await Task.Run(() => _postgresContext.RecreateAllTables());

                result.IsSuccess = true;
                result.Message = "PostgreSQL数据库表结构重建成功";

                _logger.LogInformation("✅ PostgreSQL数据库表结构重建完成");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"PostgreSQL数据库表结构重建失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "PostgreSQL数据库表结构重建失败");
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }

        /// <summary>
        /// 同步分店零售价数据到PostgreSQL - 按分店并发版本
        /// 🚀 使用AutoMapper + 批量操作 + 按分店并发处理 + 重试机制 + 数据库事务优化
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <param name="maxConcurrency">最大并发分店数，默认10（进一步降低以减少HQ数据库压力）</param>
        /// <param name="batchSize">每分店批次大小，默认50000</param>+
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreRetailPricesToPostgresAsync(
            List<string>? selectedStoreCodes = null,
            int maxConcurrency = 10,
            int batchSize = 50000
        )
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var progressLock = new object();
            var totalProcessed = 0;
            var totalAdded = 0;
            var totalErrors = 0;
            var totalQueryTime = 0.0;
            var totalInsertTime = 0.0;

            try
            {
                var storeFilter =
                    selectedStoreCodes?.Any() == true
                        ? $"指定分店: {string.Join(", ", selectedStoreCodes)}"
                        : "全部分店";
                _logger.LogInformation(
                    "🚀 开始按分店并发同步零售价数据到PostgreSQL - {StoreInfo} 进程{ProcessId} 内存{Memory}MB",
                    storeFilter,
                    Environment.ProcessId,
                    GC.GetTotalMemory(false) / 1024 / 1024
                );

                // 1. 获取需要处理的分店代码列表
                List<string> storeCodesToProcess;
                if (selectedStoreCodes?.Any() == true)
                {
                    storeCodesToProcess = selectedStoreCodes;
                    _logger.LogInformation(
                        "📋 使用指定分店代码: {StoreCodes}",
                        string.Join(", ", storeCodesToProcess)
                    );
                }
                else
                {
                    // 从HQ数据库获取所有有数据的分店代码
                    storeCodesToProcess = await _hqContext
                        .Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                            (price, product) =>
                                new JoinQueryInfos(
                                    JoinType.Inner,
                                    price.H商品编码 == product.H商品编码
                                )
                        )
                        .Where(
                            (price, product) =>
                                !string.IsNullOrEmpty(price.H商品编码)
                                && !string.IsNullOrEmpty(price.H分店代码)
                                && price.H使用状态 == true
                                && product.H使用状态 == true
                        )
                        .GroupBy((price, product) => price.H分店代码)
                        .Select((price, product) => price.H分店代码)
                        .ToListAsync();

                    _logger.LogInformation(
                        "📊 从HQ获取到 {StoreCount} 个有数据的分店",
                        storeCodesToProcess.Count
                    );
                }

                if (!storeCodesToProcess.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "✅ 没有需要同步的分店数据";
                    return result;
                }

                // 2. 清空本地数据
                _logger.LogInformation("🗑️ 开始清空PostgreSQL数据...");
                _postgresContext.InitializeTablesIfNeeded();

                if (selectedStoreCodes?.Any() == true)
                {
                    await _postgresContext
                        .Db.Deleteable<StoreRetailPrice>()
                        .Where(x => x.StoreCode != null && selectedStoreCodes.Contains(x.StoreCode))
                        .ExecuteCommandAsync();
                }
                else
                {
                    await _postgresContext.Db.Deleteable<StoreRetailPrice>().ExecuteCommandAsync();
                }

                // 3. 按分店并发处理
                var storeTasks =
                    new List<
                        Task<(
                            int processed,
                            int added,
                            int errors,
                            double queryTime,
                            double insertTime
                        )>
                    >();
                var completedStores = 0;

                for (int storeIndex = 0; storeIndex < storeCodesToProcess.Count; storeIndex++)
                {
                    var currentStoreIndex = storeIndex;
                    var storeCode = storeCodesToProcess[currentStoreIndex];

                    var storeTask = Task.Run(async () =>
                    {
                        return await ProcessSingleStorePostgresAsync(
                            currentStoreIndex,
                            storeCode,
                            batchSize,
                            semaphore
                        );
                    });

                    storeTasks.Add(storeTask);

                    // 如果达到最大并发数，等待一些任务完成
                    if (storeTasks.Count >= maxConcurrency)
                    {
                        var completedTask = await Task.WhenAny(storeTasks);
                        storeTasks.Remove(completedTask);
                        var storeResult = await completedTask;

                        // 更新总体进度
                        lock (progressLock)
                        {
                            totalProcessed += storeResult.processed;
                            totalAdded += storeResult.added;
                            totalErrors += storeResult.errors;
                            totalQueryTime += storeResult.queryTime;
                            totalInsertTime += storeResult.insertTime;
                            completedStores++;
                        }
                    }
                }

                // 等待所有剩余任务完成
                while (storeTasks.Any())
                {
                    var completedTask = await Task.WhenAny(storeTasks);
                    storeTasks.Remove(completedTask);
                    var storeResult = await completedTask;

                    lock (progressLock)
                    {
                        totalProcessed += storeResult.processed;
                        totalAdded += storeResult.added;
                        totalErrors += storeResult.errors;
                        totalQueryTime += storeResult.queryTime;
                        totalInsertTime += storeResult.insertTime;
                        completedStores++;
                    }
                }

                result.AddedCount = totalAdded;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;

                var finalDuration = DateTime.UtcNow - result.StartTime;
                var finalSpeed = totalAdded > 0 ? totalAdded / finalDuration.TotalSeconds : 0;
                var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024;

                result.Message =
                    totalErrors == 0
                        ? $"🎉 按分店并发PostgreSQL同步成功！共处理 {storeCodesToProcess.Count} 个分店，{totalAdded:N0} 条记录，全部成功插入"
                        : $"⚠️ 按分店并发PostgreSQL同步部分成功！分店: {storeCodesToProcess.Count}，成功: {totalAdded:N0}, 失败: {totalErrors:N0}";

                _logger.LogInformation(
                    "✅ PostgreSQL同步完成: 分店{StoreCount}个 记录{RecordCount:N0}条 耗时{Duration:F1}秒 速度{Speed:F0}条/秒 进程{ProcessId} 内存{Memory:N0}MB",
                    storeCodesToProcess.Count,
                    totalAdded,
                    finalDuration.TotalSeconds,
                    finalSpeed,
                    Environment.ProcessId,
                    finalMemory
                );

                // 内存清理
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var cleanedMemory = GC.GetTotalMemory(false) / 1024 / 1024;
                _logger.LogInformation(
                    "🧹 内存清理完成: {BeforeMemory:N0}MB → {AfterMemory:N0}MB",
                    finalMemory,
                    cleanedMemory
                );
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"❌ 分店零售价PostgreSQL并发同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(
                    ex,
                    "❌ PostgreSQL并发同步失败 进程{ProcessId}: {Message}",
                    Environment.ProcessId,
                    ex.Message
                );
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                semaphore.Dispose();
            }

            return result;
        }

        /// <summary>
        /// 处理单个分店的PostgreSQL同步 - 带重试机制
        /// </summary>
        private async Task<(
            int processed,
            int added,
            int errors,
            double queryTime,
            double insertTime
        )> ProcessSingleStorePostgresAsync(
            int storeIndex,
            string storeCode,
            int batchSize,
            SemaphoreSlim semaphore
        )
        {
            const int maxRetries = 3;
            const int baseDelayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                List<DIC_商品零售价表>? hqStoreData = null;
                List<StoreRetailPrice>? localStoreData = null;

                try
                {
                    var storeStartTime = DateTime.Now;
                    var currentThreadId = Thread.CurrentThread.ManagedThreadId;
                    var processId = Environment.ProcessId;
                    var initialMemory = GC.GetTotalMemory(false) / 1024 / 1024;

                    if (attempt == 1)
                    {
                        _logger.LogInformation(
                            "🏪 分店{StoreCode}开始 线程{ThreadId} 进程{ProcessId} 内存{Memory}MB",
                            storeCode,
                            currentThreadId,
                            processId,
                            initialMemory
                        );
                    }
                    else
                    {
                        _logger.LogInformation(
                            "🔄 分店{StoreCode}重试第{Attempt}次 线程{ThreadId}",
                            storeCode,
                            attempt,
                            currentThreadId
                        );
                    }

                    await semaphore.WaitAsync();
                    try
                    {
                        using var localDb = PostgresSqlSugarContext.CreateConcurrentConnection(
                            _postgresContext.Configuration
                        );
                        using var hqDb = Data.HqSqlSugarContext.CreateConcurrentConnection(
                            _hqContext.Configuration
                        );

                        // 设置更长的超时时间
                        hqDb.Ado.CommandTimeOut = 120;
                        localDb.Ado.CommandTimeOut = 120;

                        // 步骤1：查询HQ数据 - 带重试机制
                        var queryStartTime = DateTime.Now;

                        // 添加随机延迟，避免所有连接同时查询
                        if (attempt > 1)
                        {
                            var delay = Random.Shared.Next(100, 1000);
                            await Task.Delay(delay);
                        }

                        var storeQuery = hqDb.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                                (price, product) =>
                                    new JoinQueryInfos(
                                        JoinType.Inner,
                                        price.H商品编码 == product.H商品编码
                                    )
                            )
                            .Where(
                                (price, product) =>
                                    price.H分店代码 == storeCode
                                    && !string.IsNullOrEmpty(price.H商品编码)
                                    && price.H使用状态 == true
                                    && product.H使用状态 == true
                            );

                        hqStoreData = await storeQuery
                            .Select((price, product) => price)
                            .ToListAsync();

                        var queryDuration = DateTime.Now - queryStartTime;

                        if (!hqStoreData.Any())
                        {
                            return (0, 0, 0, queryDuration.TotalSeconds, 0);
                        }

                        // 步骤2：转换数据格式
                        localStoreData = _mapper.Map<List<StoreRetailPrice>>(hqStoreData);

                        // 步骤3：直接批量插入
                        var storeAdded = 0;
                        var storeErrors = 0;

                        var insertStartTime = DateTime.Now;
                        try
                        {
                            await localDb
                                .Fastest<StoreRetailPrice>()
                                .PageSize(50000)
                                .BulkCopyAsync(localStoreData);
                            storeAdded += localStoreData.Count;
                        }
                        catch (Exception ex)
                        {
                            storeErrors += localStoreData.Count;
                            _logger.LogError(
                                ex,
                                "❌ 分店{StoreCode}插入失败 线程{ThreadId} 进程{ProcessId}: {Message}",
                                storeCode,
                                currentThreadId,
                                processId,
                                ex.Message
                            );
                        }

                        var insertDuration = DateTime.Now - insertStartTime;
                        var totalDuration = DateTime.Now - storeStartTime;
                        var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024;

                        var processedCount = localStoreData.Count;

                        _logger.LogInformation(
                            "✅ 分店{StoreCode}完成 记录{RecordCount:N0}条 成功{AddedCount:N0}条 失败{ErrorCount:N0}条 耗时{Duration:F1}秒 查询{QueryTime:F1}秒 插入{InsertTime:F1}秒 线程{ThreadId} 进程{ProcessId} 内存{Memory}MB",
                            storeCode,
                            processedCount,
                            storeAdded,
                            storeErrors,
                            totalDuration.TotalSeconds,
                            queryDuration.TotalSeconds,
                            insertDuration.TotalSeconds,
                            currentThreadId,
                            processId,
                            finalMemory
                        );

                        hqStoreData.Clear();
                        localStoreData.Clear();

                        return (
                            processedCount,
                            storeAdded,
                            storeErrors,
                            queryDuration.TotalSeconds,
                            insertDuration.TotalSeconds
                        );
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }
                catch (Exception ex) when (IsTransientError(ex) && attempt < maxRetries)
                {
                    var errorThreadId = Thread.CurrentThread.ManagedThreadId;
                    var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);

                    _logger.LogWarning(
                        "⚠️ 分店{StoreCode}第{Attempt}次尝试失败，{Delay}ms后重试 线程{ThreadId}: {Message}",
                        storeCode,
                        attempt,
                        delay,
                        errorThreadId,
                        ex.Message
                    );

                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    var errorThreadId = Thread.CurrentThread.ManagedThreadId;
                    var errorProcessId = Environment.ProcessId;

                    if (attempt == maxRetries)
                    {
                        _logger.LogError(
                            ex,
                            "❌ 分店{StoreCode}最终失败（{MaxRetries}次重试后） 线程{ThreadId} 进程{ProcessId}: {Message}",
                            storeCode,
                            maxRetries,
                            errorThreadId,
                            errorProcessId,
                            ex.Message
                        );
                    }
                    else
                    {
                        _logger.LogError(
                            ex,
                            "❌ 分店{StoreCode}处理失败 线程{ThreadId} 进程{ProcessId}: {Message}",
                            storeCode,
                            errorThreadId,
                            errorProcessId,
                            ex.Message
                        );
                    }

                    return (0, 0, 1, 0, 0);
                }
            }

            return (0, 0, 1, 0, 0);
        }

        /// <summary>
        /// 判断是否为可重试的瞬态错误
        /// </summary>
        private static bool IsTransientError(Exception ex)
        {
            // SQL Server瞬态错误（用于HQ数据库连接）
            if (ex is Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                return sqlEx.Number switch
                {
                    -1 => true, // 传输级错误
                    -2 => true, // 超时
                    2 => true, // 连接超时
                    53 => true, // 网络相关错误
                    121 => true, // 信号量超时
                    1205 => true, // 死锁
                    1222 => true, // 锁请求超时
                    8645 => true, // 内存不足
                    8651 => true, // 内存不足
                    _ => false,
                };
            }

            // PostgreSQL (Npgsql) 瞬态错误（用于PostgreSQL数据库连接）
            if (ex is Npgsql.NpgsqlException npgsqlEx)
            {
                return npgsqlEx.SqlState switch
                {
                    "08000" => true, // 连接异常
                    "08003" => true, // 连接不存在
                    "08006" => true, // 连接失败
                    "08P01" => true, // 协议违规
                    "53200" => true, // 内存不足
                    "53300" => true, // 磁盘空间不足
                    "53400" => true, // 配置文件错误
                    "57P01" => true, // 管理员关闭
                    "57P02" => true, // 崩溃关闭
                    "57P03" => true, // 无法连接
                    "57P04" => true, // 数据库暂停
                    "XX000" => true, // 内部错误
                    _ => false,
                };
            }

            // 网络和超时相关错误
            if (
                ex is System.Net.Sockets.SocketException
                || ex is System.IO.IOException
                || ex is System.TimeoutException
                || ex is System.ObjectDisposedException
                || ex is System.InvalidOperationException
            )
            {
                return true;
            }

            // 其他网络相关错误
            if (
                ex.Message.Contains("传输级错误")
                || ex.Message.Contains("transport-level error")
                || ex.Message.Contains("物理连接不可用")
                || ex.Message.Contains("connection not available")
                || ex.Message.Contains("超时")
                || ex.Message.Contains("timeout")
                || ex.Message.Contains("Exception while writing to stream")
                || ex.Message.Contains("Timeout during writing attempt")
                || ex.Message.Contains("Connection is not open")
                || ex.Message.Contains("The connection is broken")
                || ex.Message.Contains("server closed the connection")
                || ex.Message.Contains("connection pool exhausted")
            )
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 同步分店清货价数据到PostgreSQL
        /// 🚀 使用AutoMapper + 批量操作 + 数据库事务优化
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreClearancePricesToPostgresAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                var storeFilter =
                    selectedStoreCodes?.Any() == true
                        ? $"指定分店: {string.Join(", ", selectedStoreCodes)}"
                        : "全部分店";
                _logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店清货价数据到PostgreSQL - {storeFilter}"
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 确保PostgreSQL表结构存在
                _logger.LogInformation("🔧 检查并初始化PostgreSQL表结构...");
                _postgresContext.InitializeTablesIfNeeded();

                // 🔄 开启PostgreSQL数据库事务，确保数据一致性
                await _postgresContext.Db.Ado.BeginTranAsync();

                try
                {
                    // 2. 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                    var query = _hqContext
                        .Db.Queryable<DIC_商品清货价表, DIC_商品信息字典表>(
                            (clearance, product) =>
                                new JoinQueryInfos(
                                    JoinType.Inner,
                                    clearance.商品编码 == product.H商品编码
                                )
                        )
                        .Where(
                            (clearance, product) =>
                                !string.IsNullOrEmpty(clearance.商品编码)
                                && !string.IsNullOrEmpty(clearance.分店代码)
                                && product.H使用状态 == true
                        );

                    // 如果指定了分店代码，添加分店代码过滤条件
                    if (selectedStoreCodes?.Any() == true)
                    {
                        query = query.Where(
                            (clearance, product) => selectedStoreCodes.Contains(clearance.分店代码)
                        );
                    }

                    var hqClearancePrices = await query
                        .Select((clearance, product) => clearance)
                        .ToListAsync();

                    _logger.LogInformation(
                        $"📊 从HQ获取到 {hqClearancePrices.Count:N0} 条有效的分店清货价记录（已过滤无效商品和分店）"
                    );

                    if (!hqClearancePrices.Any())
                    {
                        result.IsSuccess = true;
                        result.Message = $"✅ HQ数据库中没有{storeFilter}的清货价数据，同步完成";
                        await _postgresContext.Db.Ado.CommitTranAsync();
                        return result;
                    }

                    // 3. 根据是否指定分店来决定删除策略
                    if (selectedStoreCodes?.Any() == true)
                    {
                        _logger.LogInformation(
                            $"🗑️ 正在清空PostgreSQL中指定分店的清货价数据: {string.Join(", ", selectedStoreCodes)}"
                        );
                        await _postgresContext
                            .Db.Deleteable<StoreClearancePrice>()
                            .Where(x => selectedStoreCodes.Contains(x.StoreCode!))
                            .ExecuteCommandAsync();
                        _logger.LogInformation("✅ 指定分店的清货价数据已清空");
                    }
                    else
                    {
                        _logger.LogInformation("🗑️ 正在清空PostgreSQL中所有分店清货价数据");
                        await _postgresContext
                            .Db.Deleteable<StoreClearancePrice>()
                            .ExecuteCommandAsync();
                        _logger.LogInformation("✅ 所有分店清货价数据已清空");
                    }

                    // 4. 🚀 使用AutoMapper批量转换HQ实体到本地实体
                    _logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localClearancePrices = _mapper.Map<List<StoreClearancePrice>>(
                        hqClearancePrices
                    );
                    _logger.LogInformation(
                        $"✅ 数据转换完成，共 {localClearancePrices.Count:N0} 条记录"
                    );

                    // 5. 分批批量插入到PostgreSQL（使用BulkCopy提高性能）
                    const int batchSize = 10000;
                    int totalAdded = 0;
                    int batchNumber = 1;

                    for (int i = 0; i < localClearancePrices.Count; i += batchSize)
                    {
                        var batch = localClearancePrices.Skip(i).Take(batchSize).ToList();
                        var progress = (double)(i + batch.Count) / localClearancePrices.Count * 100;

                        _logger.LogInformation(
                            $"📦 正在处理第 {batchNumber} 批，共 {batch.Count:N0} 条记录 (进度: {progress:F1}%)"
                        );

                        try
                        {
                            // 使用BulkCopy批量插入提高性能
                            await _postgresContext
                                .Db.Fastest<StoreClearancePrice>()
                                .BulkCopyAsync(batch);

                            totalAdded += batch.Count;
                            _logger.LogInformation(
                                $"✅ 第 {batchNumber} 批处理完成，新增 {batch.Count:N0} 条记录"
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"❌ 第 {batchNumber} 批处理失败");
                            throw; // 重新抛出异常以触发事务回滚
                        }

                        batchNumber++;
                    }

                    // 🎉 提交事务
                    await _postgresContext.Db.Ado.CommitTranAsync();
                    _logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.IsSuccess = true;
                    result.Message =
                        $"🎉 分店清货价数据PostgreSQL同步成功 - {storeFilter}！共处理 {totalAdded:N0} 条记录";

                    _logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    // 🔙 回滚事务
                    await _postgresContext.Db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"❌ 分店清货价PostgreSQL同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "❌ 同步分店清货价数据时发生错误");
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                _logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }

        /// <summary>
        /// 同步分店一品多码数据到PostgreSQL
        /// 🚀 使用AutoMapper + 批量操作 + 数据库事务优化
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreMultiCodeProductsToPostgresAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow };

            try
            {
                var storeFilter =
                    selectedStoreCodes?.Any() == true
                        ? $"指定分店: {string.Join(", ", selectedStoreCodes)}"
                        : "全部分店";
                _logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店一品多码数据到PostgreSQL - {storeFilter}"
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 确保PostgreSQL表结构存在
                _logger.LogInformation("🔧 检查并初始化PostgreSQL表结构...");
                _postgresContext.InitializeTablesIfNeeded();

                // 🔄 开启PostgreSQL数据库事务，确保数据一致性
                await _postgresContext.Db.Ado.BeginTranAsync();

                try
                {
                    // 2. 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                    var query = _hqContext
                        .Db.Queryable<DIC_分店一品多码表, DIC_商品信息字典表>(
                            (multiCode, product) =>
                                new JoinQueryInfos(
                                    JoinType.Inner,
                                    multiCode.H商品编码 == product.H商品编码
                                )
                        )
                        .Where(
                            (multiCode, product) =>
                                !string.IsNullOrEmpty(multiCode.H商品编码)
                                && !string.IsNullOrEmpty(multiCode.H分店代码)
                                && multiCode.H使用状态 == true
                                && product.H使用状态 == true
                        );

                    // 如果指定了分店代码，添加分店代码过滤条件
                    if (selectedStoreCodes?.Any() == true)
                    {
                        query = query.Where(
                            (multiCode, product) =>
                                !string.IsNullOrEmpty(multiCode.H分店代码)
                                && selectedStoreCodes.Contains(multiCode.H分店代码)
                        );
                    }

                    var hqMultiCodeProducts = await query
                        .Select((multiCode, product) => multiCode)
                        .ToListAsync();

                    _logger.LogInformation(
                        $"📊 从HQ获取到 {hqMultiCodeProducts.Count:N0} 条有效的分店一品多码记录（已过滤无效商品和分店）"
                    );

                    if (!hqMultiCodeProducts.Any())
                    {
                        result.IsSuccess = true;
                        result.Message = $"✅ HQ数据库中没有{storeFilter}的一品多码数据，同步完成";
                        await _postgresContext.Db.Ado.CommitTranAsync();
                        return result;
                    }

                    // 3. 根据是否指定分店来决定删除策略
                    if (selectedStoreCodes?.Any() == true)
                    {
                        _logger.LogInformation(
                            $"🗑️ 正在清空PostgreSQL中指定分店的一品多码数据: {string.Join(", ", selectedStoreCodes)}"
                        );
                        await _postgresContext
                            .Db.Deleteable<StoreMultiCodeProduct>()
                            .Where(x => selectedStoreCodes.Contains(x.StoreCode!))
                            .ExecuteCommandAsync();
                        _logger.LogInformation("✅ 指定分店的一品多码数据已清空");
                    }
                    else
                    {
                        _logger.LogInformation("🗑️ 正在清空PostgreSQL中所有分店一品多码数据");
                        await _postgresContext
                            .Db.Deleteable<StoreMultiCodeProduct>()
                            .ExecuteCommandAsync();
                        _logger.LogInformation("✅ 所有分店一品多码数据已清空");
                    }

                    // 4. 🚀 使用AutoMapper批量转换HQ实体到本地实体
                    _logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localMultiCodeProducts = _mapper.Map<List<StoreMultiCodeProduct>>(
                        hqMultiCodeProducts
                    );
                    _logger.LogInformation(
                        $"✅ 数据转换完成，共 {localMultiCodeProducts.Count:N0} 条记录"
                    );

                    // 5. 分批批量插入到PostgreSQL（使用BulkCopy提高性能）
                    const int batchSize = 20000;
                    int totalAdded = 0;
                    int batchNumber = 1;

                    for (int i = 0; i < localMultiCodeProducts.Count; i += batchSize)
                    {
                        var batch = localMultiCodeProducts.Skip(i).Take(batchSize).ToList();
                        var progress =
                            (double)(i + batch.Count) / localMultiCodeProducts.Count * 100;

                        _logger.LogInformation(
                            $"📦 正在处理第 {batchNumber} 批，共 {batch.Count:N0} 条记录 (进度: {progress:F1}%)"
                        );

                        try
                        {
                            // 确保表结构正确
                            if (batchNumber == 1)
                            {
                                try
                                {
                                    // 检查表是否存在，如不存在则创建
                                    if (
                                        !_postgresContext.Db.DbMaintenance.IsAnyTable(
                                            "storemulticodeproduct"
                                        )
                                    )
                                    {
                                        _logger.LogInformation(
                                            "🔧 storemulticodeproduct 表不存在，正在创建..."
                                        );
                                        _postgresContext.Db.CodeFirst.InitTables<StoreMultiCodeProduct>();
                                        _logger.LogInformation(
                                            "✅ storemulticodeproduct 表创建成功"
                                        );
                                    }
                                    else
                                    {
                                        // 检查表结构是否匹配
                                        var columns =
                                            _postgresContext.Db.DbMaintenance.GetColumnInfosByTableName(
                                                "storemulticodeproduct"
                                            );
                                        _logger.LogInformation(
                                            $"📋 当前表结构: {string.Join(", ", columns.Select(c => $"{c.DbColumnName}({c.DataType})"))}"
                                        );

                                        // 检查是否有UUID/uuid列
                                        bool hasUuidColumn = columns.Any(c =>
                                            c.DbColumnName.Equals(
                                                "UUID",
                                                StringComparison.OrdinalIgnoreCase
                                            )
                                            || c.DbColumnName.Equals(
                                                "uuid",
                                                StringComparison.OrdinalIgnoreCase
                                            )
                                        );

                                        if (!hasUuidColumn)
                                        {
                                            _logger.LogWarning(
                                                "⚠️  表缺少UUID列，将重新创建表结构"
                                            );
                                            _postgresContext.Db.DbMaintenance.DropTable<StoreMultiCodeProduct>();
                                            _postgresContext.Db.CodeFirst.InitTables<StoreMultiCodeProduct>();
                                            _logger.LogInformation(
                                                "✅ storemulticodeproduct 表重新创建成功"
                                            );
                                        }
                                    }
                                }
                                catch (Exception tableEx)
                                {
                                    _logger.LogError(tableEx, "❌ 表结构检查/创建失败");
                                    throw;
                                }
                            }

                            // 使用BulkCopy批量插入提高性能
                            await _postgresContext
                                .Db.Fastest<StoreMultiCodeProduct>()
                                .BulkCopyAsync(batch);

                            totalAdded += batch.Count;
                            _logger.LogInformation(
                                $"✅ 第 {batchNumber} 批处理完成，新增 {batch.Count:N0} 条记录"
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"❌ 第 {batchNumber} 批处理失败: {ex.Message}");
                            throw; // 重新抛出异常以触发事务回滚
                        }

                        batchNumber++;
                    }

                    // 🎉 提交事务
                    await _postgresContext.Db.Ado.CommitTranAsync();
                    _logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.IsSuccess = true;
                    result.Message =
                        $"🎉 分店一品多码数据PostgreSQL同步成功 - {storeFilter}！共处理 {totalAdded:N0} 条记录";

                    _logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    // 🔙 回滚事务
                    await _postgresContext.Db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = $"❌ 分店一品多码PostgreSQL同步失败: {ex.Message}";
                result.ErrorCount = 1;
                _logger.LogError(ex, "❌ 同步分店一品多码数据时发生错误");
            }
            finally
            {
                result.EndTime = DateTime.UtcNow;
                result.Duration = result.EndTime - result.StartTime;
                _logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }
    }
}
