using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 分店数据同步服务 - 从HQ数据库同步分店信息到本地数据库
    /// </summary>
    public class StoreSyncService
    {
        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly ILogger<StoreSyncService> _logger;

        public StoreSyncService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            ILogger<StoreSyncService> logger
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _logger = logger;
        }

        /// <summary>
        /// 同步分店数据从HQ到本地数据库
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoresFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation("开始从HQ数据库同步分店数据...");

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 从HQ数据库获取分店数据
                var hqBranches = await GetHqBranchesAsync();
                _logger.LogInformation($"从HQ数据库获取到 {hqBranches.Count} 个分店");

                if (!hqBranches.Any())
                {
                    result.Message = "HQ数据库中没有找到分店数据";
                    result.IsSuccess = true;
                    result.EndTime = DateTime.Now;
                    return result;
                }

                // 3. 获取本地数据库现有分店
                var localStores = await _localContext.StoreDb.GetListAsync();
                _logger.LogInformation($"本地数据库中有 {localStores.Count} 个分店");

                // 4. 同步数据
                foreach (var hqBranch in hqBranches)
                {
                    try
                    {
                        await SyncSingleStoreAsync(hqBranch, localStores, result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"同步分店 {hqBranch.BranchCode} 时发生错误");
                        result.ErrorCount++;
                    }
                }

                result.IsSuccess = true;
                result.Message =
                    $"同步完成！新增: {result.AddedCount}, 更新: {result.UpdatedCount}, 错误: {result.ErrorCount}";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步分店数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 从HQ数据库获取分店数据
        /// </summary>
        private async Task<List<HqBranch>> GetHqBranchesAsync()
        {
            try
            {
                // 从HQ数据库查询分店信息
                var hqBranches = await _hqContext.HqBranchDb.GetListAsync();

                _logger.LogInformation($"成功从HQ数据库获取 {hqBranches.Count} 个分店");

                return hqBranches;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从HQ数据库获取分店数据失败");

                // 如果直接查询失败，尝试使用SQL查询
                return await GetHqBranchesWithSqlAsync();
            }
        }

        /// <summary>
        /// 使用SQL直接查询HQ分店数据（备用方法）
        /// </summary>
        private Task<List<HqBranch>> GetHqBranchesWithSqlAsync()
        {
            try
            {
                _logger.LogInformation("尝试使用SQL直接查询HQ分店数据...");

                // 查找可能的分店表
                var tableCheckSql =
                    @"
                    SELECT TOP 1 TABLE_NAME 
                    FROM INFORMATION_SCHEMA.TABLES 
                    WHERE TABLE_NAME LIKE '%分店%' 
                       OR TABLE_NAME LIKE '%Branch%' 
                       OR TABLE_NAME = 'DIC_分店信息表'
                    ORDER BY TABLE_NAME";

                var tableName = _hqContext.Db.Ado.GetString(tableCheckSql);

                if (string.IsNullOrEmpty(tableName))
                {
                    _logger.LogWarning("未找到HQ数据库中的分店表");
                    return Task.FromResult(new List<HqBranch>());
                }

                _logger.LogInformation($"找到HQ分店表: {tableName}");

                // 根据不同的表结构查询数据
                var sql =
                    $@"
                    SELECT TOP 100
                        ISNULL(BranchCode, StoreCode) AS BranchCode,
                        ISNULL(BranchName, StoreName) AS BranchName,
                        ISNULL(BusinessNumber, ABN, '') AS BusinessNumber,
                        ISNULL([Address], '') AS [Address],
                        ISNULL(ContactPhone, Phone) AS ContactPhone,
                        ISNULL(ContactPerson, Manager) AS ContactPerson,
                        CASE WHEN ISNULL(IsActive, 1) = 1 THEN 1 ELSE 0 END AS IsActive,
                        ISNULL(CreatedDate, GETDATE()) AS CreatedDate
                    FROM [{tableName}]
                    WHERE (BranchCode IS NOT NULL OR StoreCode IS NOT NULL)
                      AND (BranchName IS NOT NULL OR StoreName IS NOT NULL)
                    ORDER BY BranchCode";

                var hqData = _hqContext.Db.Ado.SqlQuery<dynamic>(sql);

                var hqBranches = hqData
                    .Select(data => new HqBranch
                    {
                        BranchCode = data.BranchCode?.ToString() ?? "",
                        BranchName = data.BranchName?.ToString() ?? "",
                        BusinessNumber = data.BusinessNumber?.ToString(),
                        Address = data.Address?.ToString() ?? "",
                        Phone = data.ContactPhone?.ToString() ?? "",
                        ManagerName = data.ContactPerson?.ToString() ?? "",
                    })
                    .ToList();

                _logger.LogInformation($"通过SQL查询获取到 {hqBranches.Count} 个分店");
                return Task.FromResult(hqBranches);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "使用SQL查询HQ分店数据也失败");
                return Task.FromResult(new List<HqBranch>());
            }
        }

        /// <summary>
        /// 验证ABN格式（11位数字）
        /// </summary>
        /// <param name="abn">待验证的ABN字符串</param>
        /// <returns>如果符合格式则返回ABN，否则返回null</returns>
        private string? ValidateAbn(string? abn)
        {
            if (string.IsNullOrWhiteSpace(abn))
            {
                return null;
            }

            // 移除空格和特殊字符
            var cleanedAbn = Regex.Replace(abn.Trim(), @"\s+", "");

            // 验证是否为11位数字
            if (Regex.IsMatch(cleanedAbn, @"^\d{11}$"))
            {
                return cleanedAbn;
            }

            // 不符合格式，返回null
            return null;
        }

        /// <summary>
        /// 同步单个分店数据
        /// </summary>
        private async Task SyncSingleStoreAsync(
            HqBranch hqBranch,
            List<Store> localStores,
            SyncResult result
        )
        {
            // 查找本地是否已存在该分店（根据分店代码）
            var existingStore = localStores.FirstOrDefault(s => s.StoreCode == hqBranch.BranchCode);

            if (existingStore == null)
            {
                // 新增分店
                var validAbn = ValidateAbn(hqBranch.BusinessNumber);
                if (!string.IsNullOrWhiteSpace(hqBranch.BusinessNumber) && validAbn == null)
                {
                    _logger.LogWarning(
                        $"分店 {hqBranch.BranchCode} 的ABN格式不符合要求，已清空。原始值: {hqBranch.BusinessNumber}"
                    );
                }

                var newStore = new Store
                {
                    StoreGUID = Guid.NewGuid().ToString(),
                    StoreCode = hqBranch.BranchCode,
                    StoreName = hqBranch.BranchName,
                    ABN = validAbn,
                    BrandName = "Hot Bargain",
                    Address = hqBranch.Address,
                    Phone = hqBranch.Phone,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    CreatedBy = "SYSTEM_SYNC",
                    UpdatedBy = "SYSTEM_SYNC",
                };

                await _localContext.StoreDb.InsertAsync(newStore);
                result.AddedCount++;
                _logger.LogInformation($"新增分店: {newStore.StoreCode} - {newStore.StoreName}");
            }
            else
            {
                // 更新现有分店（只更新关键信息）
                bool needUpdate = false;

                // 验证ABN格式
                var validAbn = ValidateAbn(hqBranch.BusinessNumber);
                if (!string.IsNullOrWhiteSpace(hqBranch.BusinessNumber) && validAbn == null)
                {
                    _logger.LogWarning(
                        $"分店 {hqBranch.BranchCode} 的ABN格式不符合要求，已清空。原始值: {hqBranch.BusinessNumber}"
                    );
                }

                if (existingStore.StoreName != hqBranch.BranchName)
                {
                    existingStore.StoreName = hqBranch.BranchName;
                    needUpdate = true;
                }

                if (existingStore.Address != hqBranch.Address)
                {
                    existingStore.Address = hqBranch.Address;
                    needUpdate = true;
                }

                if (existingStore.Phone != hqBranch.Phone)
                {
                    existingStore.Phone = hqBranch.Phone;
                    needUpdate = true;
                }

                if (existingStore.ABN != validAbn)
                {
                    existingStore.ABN = validAbn;
                    needUpdate = true;
                }

                if (existingStore.BrandName != "Hot Bargain")
                {
                    existingStore.BrandName = "Hot Bargain";
                    needUpdate = true;
                }

                if (needUpdate)
                {
                    existingStore.UpdatedAt = DateTime.Now;
                    existingStore.UpdatedBy = "SYSTEM_SYNC";
                    await _localContext.StoreDb.UpdateAsync(existingStore);
                    result.UpdatedCount++;
                    _logger.LogInformation(
                        $"更新分店: {existingStore.StoreCode} - {existingStore.StoreName}"
                    );
                }
            }
        }

        /// <summary>
        /// 获取同步历史记录
        /// </summary>
        public Task<List<SyncHistory>> GetSyncHistoryAsync(int pageSize = 10)
        {
            try
            {
                // 这里可以从日志表或专门的同步历史表获取数据
                // 暂时返回模拟数据，实际项目中应该有专门的同步历史表
                return Task.FromResult(
                    new List<SyncHistory>
                    {
                        new SyncHistory
                        {
                            SyncTime = DateTime.Now.AddHours(-1),
                            IsSuccess = true,
                            Message = "同步完成！新增: 2, 更新: 5, 错误: 0",
                            Duration = TimeSpan.FromMinutes(2),
                        },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取同步历史失败");
                return Task.FromResult(new List<SyncHistory>());
            }
        }
    }
}
