using System.Diagnostics;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 商品数据一致性检测与修复服务
    /// 负责检测和修复商品基础表(Product)与三张关联表之间的数据一致性问题：
    /// - StoreRetailPrice（分店零售价表）
    /// - StoreMultiCodeProduct（分店多码表）
    /// - ProductSetCode（套装编码表）
    /// </summary>
    public class ProductIntegrityService : IProductIntegrityService
    {
        private readonly SqlSugarContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ProductIntegrityService> _logger;

        /// <summary>
        /// SQL 命令超时时间（秒），用于大数据量操作
        /// </summary>
        private const int CommandTimeoutSeconds = 300;

        /// <summary>
        /// 检测报告返回的最大样本编码数量
        /// </summary>
        private const int MaxSampleCount = 100;

        public ProductIntegrityService(
            SqlSugarContext db,
            IConfiguration configuration,
            ILogger<ProductIntegrityService> logger
        )
        {
            _db = db;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// 判断当前数据库是否为 PostgreSQL
        /// </summary>
        private bool IsPostgreSQL => _db.Db.CurrentConnectionConfig.DbType == DbType.PostgreSQL;

        /// <summary>
        /// SQL 注入防护：转义单引号
        /// </summary>
        private static string EscapeSql(string value)
        {
            return value.Replace("'", "''");
        }

        #region Check Methods

        public async Task<ApiResponse<ProductIntegrityCheckResultDto>> CheckIntegrityAsync(
            List<string>? storeCodes = null
        )
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Console.WriteLine("[数据一致性检测] ===== 开始检测 =====");

                var stores = await GetActiveStoresAsync(storeCodes);
                Console.WriteLine($"[数据一致性检测] 活跃分店数量: {stores.Count}");

                var result = new ProductIntegrityCheckResultDto();

                result.ProductSetCodeReport = await CheckProductSetCodeAsync();

                var semaphore = new SemaphoreSlim(5);
                var storeTasks = stores.Select(async store =>
                {
                    await semaphore.WaitAsync();
                    ISqlSugarClient? storeDb = null;
                    try
                    {
                        storeDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                        storeDb.Ado.CommandTimeOut = CommandTimeoutSeconds;

                        var storeReport = new StoreIntegrityReport
                        {
                            StoreCode = store.StoreCode,
                            StoreName = store.StoreName ?? store.StoreCode,
                        };

                        var retailPriceReport = await CheckStoreRetailPriceForStoreAsync(
                            storeDb, store.StoreCode
                        );
                        storeReport.TableReports.Add(retailPriceReport);

                        var multiCodeReport = await CheckStoreMultiCodeProductForStoreAsync(
                            storeDb, store.StoreCode
                        );
                        storeReport.TableReports.Add(multiCodeReport);

                        Console.WriteLine(
                            $"  [分店 {store.StoreCode}] StoreRetailPrice - 孤立: {retailPriceReport.OrphanedCount}, 缺失: {retailPriceReport.MissingCount}"
                        );
                        Console.WriteLine(
                            $"  [分店 {store.StoreCode}] StoreMultiCodeProduct - 孤立: {multiCodeReport.OrphanedCount}, 缺失: {multiCodeReport.MissingCount}"
                        );

                        return storeReport;
                    }
                    finally
                    {
                        semaphore.Release();
                        storeDb?.Dispose();
                    }
                });

                var storeResults = await Task.WhenAll(storeTasks);
                result.StoreReports = storeResults.OrderBy(s => s.StoreCode).ToList();

                stopwatch.Stop();
                result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;

                var totalOrphaned = result.StoreReports.Sum(s =>
                    s.TableReports.Sum(t => t.OrphanedCount));
                var totalMissing = result.StoreReports.Sum(s =>
                    s.TableReports.Sum(t => t.MissingCount));

                _logger.LogInformation(
                    "数据一致性检测完成，{StoreCount} 个分店，总孤立 {Orphaned}，总缺失 {Missing}，耗时 {Duration:F2}s",
                    result.StoreReports.Count, totalOrphaned, totalMissing, result.DurationSeconds
                );
                Console.WriteLine(
                    $"[数据一致性检测] ===== 检测完成，{result.StoreReports.Count} 个分店，总孤立 {totalOrphaned}，总缺失 {totalMissing}，耗时 {result.DurationSeconds:F2}s ====="
                );

                return ApiResponse<ProductIntegrityCheckResultDto>.OK(result, "数据一致性检测完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据一致性检测失败");
                Console.WriteLine($"[数据一致性检测] ❌ 检测失败: {ex.Message}");
                return ApiResponse<ProductIntegrityCheckResultDto>.Error(
                    "检测失败: " + ex.Message,
                    "DATABASE_ERROR"
                );
            }
        }

        private async Task<TableIntegrityReport> CheckStoreRetailPriceForStoreAsync(
            ISqlSugarClient db,
            string storeCode
        )
        {
            var report = new TableIntegrityReport { TableName = "StoreRetailPrice" };
            var escapedCode = EscapeSql(storeCode);

            var totalSql =
                $@"SELECT COUNT(DISTINCT t.ProductCode) FROM StoreRetailPrice t
                   WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL AND t.StoreCode = '{escapedCode}'";
            report.TotalChecked = await db.Ado.GetIntAsync(totalSql);

            var orphanedCountSql =
                $@"SELECT COUNT(DISTINCT t.ProductCode) FROM StoreRetailPrice t
                   WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL AND t.StoreCode = '{escapedCode}'
                   AND NOT EXISTS (SELECT 1 FROM Product p WHERE p.ProductCode = t.ProductCode AND p.IsDeleted = 0)";
            report.OrphanedCount = await db.Ado.GetIntAsync(orphanedCountSql);

            var missingCountSql =
                $@"SELECT COUNT(*) FROM Product p
                   WHERE p.IsDeleted = 0 AND p.ProductCode IS NOT NULL
                   AND NOT EXISTS (
                       SELECT 1 FROM StoreRetailPrice t
                       WHERE t.ProductCode = p.ProductCode AND t.IsDeleted = 0 AND t.StoreCode = '{escapedCode}'
                   )";
            report.MissingCount = await db.Ado.GetIntAsync(missingCountSql);

            if (report.OrphanedCount > 0)
            {
                var orphanedSampleSql = IsPostgreSQL
                    ? $@"SELECT DISTINCT t.ProductCode FROM StoreRetailPrice t
                        WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL AND t.StoreCode = '{escapedCode}'
                        AND NOT EXISTS (SELECT 1 FROM Product p WHERE p.ProductCode = t.ProductCode AND p.IsDeleted = 0)
                        LIMIT {MaxSampleCount}"
                    : $@"SELECT DISTINCT TOP {MaxSampleCount} t.ProductCode FROM StoreRetailPrice t
                        WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL AND t.StoreCode = '{escapedCode}'
                        AND NOT EXISTS (SELECT 1 FROM Product p WHERE p.ProductCode = t.ProductCode AND p.IsDeleted = 0)";
                report.OrphanedProductCodes = await db.Ado.SqlQueryAsync<string>(orphanedSampleSql);
            }

            if (report.MissingCount > 0)
            {
                var missingSampleSql = IsPostgreSQL
                    ? $@"SELECT p.ProductCode FROM Product p
                        WHERE p.IsDeleted = 0 AND p.ProductCode IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1 FROM StoreRetailPrice t
                            WHERE t.ProductCode = p.ProductCode AND t.IsDeleted = 0 AND t.StoreCode = '{escapedCode}'
                        )
                        LIMIT {MaxSampleCount}"
                    : $@"SELECT TOP {MaxSampleCount} p.ProductCode FROM Product p
                        WHERE p.IsDeleted = 0 AND p.ProductCode IS NOT NULL
                        AND NOT EXISTS (
                            SELECT 1 FROM StoreRetailPrice t
                            WHERE t.ProductCode = p.ProductCode AND t.IsDeleted = 0 AND t.StoreCode = '{escapedCode}'
                        )";
                report.MissingProductCodes = await db.Ado.SqlQueryAsync<string>(missingSampleSql);
            }

            return report;
        }

        private async Task<TableIntegrityReport> CheckStoreMultiCodeProductForStoreAsync(
            ISqlSugarClient db,
            string storeCode
        )
        {
            var report = new TableIntegrityReport { TableName = "StoreMultiCodeProduct" };
            var escapedCode = EscapeSql(storeCode);

            var totalSql =
                $@"SELECT COUNT(DISTINCT sm.ProductCode) FROM StoreMultiCodeProduct sm
                   WHERE sm.IsDeleted = 0 AND sm.ProductCode IS NOT NULL AND sm.StoreCode = '{escapedCode}'";
            report.TotalChecked = await db.Ado.GetIntAsync(totalSql);

            var orphanedCountSql =
                $@"SELECT COUNT(*) FROM StoreMultiCodeProduct sm
                   WHERE sm.IsDeleted = 0 AND sm.StoreCode = '{escapedCode}'
                   AND NOT EXISTS (
                       SELECT 1 FROM ProductSetCode psc
                       WHERE psc.ProductCode = sm.ProductCode
                         AND psc.SetProductCode = sm.MultiCodeProductCode
                         AND psc.IsDeleted = 0
                   )";
            report.OrphanedCount = await db.Ado.GetIntAsync(orphanedCountSql);

            var missingCountSql =
                $@"SELECT COUNT(*) FROM ProductSetCode psc
                   WHERE psc.IsDeleted = 0 AND psc.IsActive = 1
                   AND NOT EXISTS (
                       SELECT 1 FROM StoreMultiCodeProduct sm
                       WHERE sm.ProductCode = psc.ProductCode
                         AND sm.MultiCodeProductCode = psc.SetProductCode
                         AND sm.IsDeleted = 0
                         AND sm.StoreCode = '{escapedCode}'
                   )";
            report.MissingCount = await db.Ado.GetIntAsync(missingCountSql);

            if (report.OrphanedCount > 0)
            {
                var orphanedSampleSql = IsPostgreSQL
                    ? $@"SELECT DISTINCT sm.ProductCode FROM StoreMultiCodeProduct sm
                        WHERE sm.IsDeleted = 0 AND sm.StoreCode = '{escapedCode}'
                        AND NOT EXISTS (
                            SELECT 1 FROM ProductSetCode psc
                            WHERE psc.ProductCode = sm.ProductCode
                              AND psc.SetProductCode = sm.MultiCodeProductCode
                              AND psc.IsDeleted = 0
                        )
                        LIMIT {MaxSampleCount}"
                    : $@"SELECT DISTINCT TOP {MaxSampleCount} sm.ProductCode FROM StoreMultiCodeProduct sm
                        WHERE sm.IsDeleted = 0 AND sm.StoreCode = '{escapedCode}'
                        AND NOT EXISTS (
                            SELECT 1 FROM ProductSetCode psc
                            WHERE psc.ProductCode = sm.ProductCode
                              AND psc.SetProductCode = sm.MultiCodeProductCode
                              AND psc.IsDeleted = 0
                        )";
                report.OrphanedProductCodes = await db.Ado.SqlQueryAsync<string>(orphanedSampleSql);
            }

            if (report.MissingCount > 0)
            {
                var missingSampleSql = IsPostgreSQL
                    ? $@"SELECT psc.ProductCode FROM ProductSetCode psc
                        WHERE psc.IsDeleted = 0 AND psc.IsActive = 1
                        AND NOT EXISTS (
                            SELECT 1 FROM StoreMultiCodeProduct sm
                            WHERE sm.ProductCode = psc.ProductCode
                              AND sm.MultiCodeProductCode = psc.SetProductCode
                              AND sm.IsDeleted = 0
                              AND sm.StoreCode = '{escapedCode}'
                        )
                        LIMIT {MaxSampleCount}"
                    : $@"SELECT TOP {MaxSampleCount} psc.ProductCode FROM ProductSetCode psc
                        WHERE psc.IsDeleted = 0 AND psc.IsActive = 1
                        AND NOT EXISTS (
                            SELECT 1 FROM StoreMultiCodeProduct sm
                            WHERE sm.ProductCode = psc.ProductCode
                              AND sm.MultiCodeProductCode = psc.SetProductCode
                              AND sm.IsDeleted = 0
                              AND sm.StoreCode = '{escapedCode}'
                        )";
                report.MissingProductCodes = await db.Ado.SqlQueryAsync<string>(missingSampleSql);
            }

            return report;
        }

        private async Task<TableIntegrityReport> CheckProductSetCodeAsync()
        {
            var report = new TableIntegrityReport { TableName = "ProductSetCode" };
            var db = _db.Db;
            var originalTimeout = db.Ado.CommandTimeOut;
            db.Ado.CommandTimeOut = CommandTimeoutSeconds;
            try
            {
                var totalSql =
                    "SELECT COUNT(DISTINCT ProductCode) FROM ProductSetCode WHERE IsDeleted = 0 AND ProductCode IS NOT NULL";
                report.TotalChecked = await db.Ado.GetIntAsync(totalSql);
                Console.WriteLine($"  [ProductSetCode] 检测商品编码总数: {report.TotalChecked}");

                var orphanedCountSql =
                    @"SELECT COUNT(DISTINCT t.ProductCode) FROM ProductSetCode t
                      WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL
                      AND NOT EXISTS (SELECT 1 FROM Product p WHERE p.ProductCode = t.ProductCode AND p.IsDeleted = 0)";
                report.OrphanedCount = await db.Ado.GetIntAsync(orphanedCountSql);
                Console.WriteLine($"  [ProductSetCode] 孤立记录数（需删除）: {report.OrphanedCount}");

                if (report.OrphanedCount > 0)
                {
                    var orphanedSampleSql = IsPostgreSQL
                        ? $@"SELECT DISTINCT t.ProductCode FROM ProductSetCode t
                            WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL
                            AND NOT EXISTS (SELECT 1 FROM Product p WHERE p.ProductCode = t.ProductCode AND p.IsDeleted = 0)
                            LIMIT {MaxSampleCount}"
                        : $@"SELECT DISTINCT TOP {MaxSampleCount} t.ProductCode FROM ProductSetCode t
                            WHERE t.IsDeleted = 0 AND t.ProductCode IS NOT NULL
                            AND NOT EXISTS (SELECT 1 FROM Product p WHERE p.ProductCode = t.ProductCode AND p.IsDeleted = 0)";
                    report.OrphanedProductCodes = await db.Ado.SqlQueryAsync<string>(orphanedSampleSql);
                    Console.WriteLine(
                        $"  [ProductSetCode] 孤立编码样本: {string.Join(", ", report.OrphanedProductCodes.Take(10))}..."
                    );
                }

                report.MissingCount = 0;
            }
            finally
            {
                db.Ado.CommandTimeOut = originalTimeout;
            }

            return report;
        }

        #endregion

        #region Fix Methods

        /// <summary>
        /// 执行数据一致性修复
        /// 支持对三张表分别执行修复操作，支持 DryRun 模拟运行模式
        /// </summary>
        public async Task<ApiResponse<ProductIntegrityFixResultDto>> FixIntegrityAsync(
            ProductIntegrityFixRequestDto request
        )
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Console.WriteLine(
                    $"[数据一致性修复] ===== 开始修复（DryRun={request.DryRun}）====="
                );

                var stores = await GetActiveStoresAsync(request.SelectedStoreCodes);
                var activeStoreCodes = stores.Select(s => s.StoreCode).ToList();
                Console.WriteLine($"[数据一致性修复] 活跃分店数量: {activeStoreCodes.Count}");

                var result = new ProductIntegrityFixResultDto { IsDryRun = request.DryRun };

                if (request.FixStoreRetailPrice)
                {
                    result.Reports.Add(
                        await FixStoreRetailPriceAsync(activeStoreCodes, request.DryRun)
                    );
                }

                if (request.FixStoreMultiCodeProduct)
                {
                    result.Reports.Add(
                        await FixStoreMultiCodeProductAsync(activeStoreCodes, request.DryRun)
                    );
                }

                if (request.FixProductSetCode)
                {
                    result.Reports.Add(await FixProductSetCodeAsync(request.DryRun));
                }

                stopwatch.Stop();
                result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;

                _logger.LogInformation(
                    "数据一致性修复完成（DryRun={DryRun}），耗时 {Duration:F2}s",
                    request.DryRun,
                    result.DurationSeconds
                );
                Console.WriteLine(
                    $"[数据一致性修复] ===== 修复完成（DryRun={request.DryRun}），耗时 {result.DurationSeconds:F2}s ====="
                );

                return ApiResponse<ProductIntegrityFixResultDto>.OK(
                    result,
                    request.DryRun ? "模拟修复完成" : "修复完成"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "数据一致性修复失败");
                Console.WriteLine($"[数据一致性修复] ❌ 修复失败: {ex.Message}");
                return ApiResponse<ProductIntegrityFixResultDto>.Error(
                    "修复失败: " + ex.Message,
                    "DATABASE_ERROR"
                );
            }
        }

        /// <summary>
        /// 修复 StoreRetailPrice（分店零售价表）
        /// 1. 软删除孤立记录（商品编码不在基础表中）
        /// 2. 为每个分店补充缺失的商品零售价记录
        /// </summary>
        private async Task<TableFixReport> FixStoreRetailPriceAsync(
            List<string> activeStoreCodes,
            bool dryRun
        )
        {
            var report = new TableFixReport { TableName = "StoreRetailPrice" };

            try
            {
                var db = _db.Db;
                var originalTimeout = db.Ado.CommandTimeOut;
                db.Ado.CommandTimeOut = CommandTimeoutSeconds;
                try
                {
                    var productFields = await db.Queryable<Product>()
                        .Where(p => p.IsDeleted == false && p.ProductCode != null)
                        .Select(p => new
                        {
                            p.ProductCode,
                            p.PurchasePrice,
                            p.RetailPrice,
                            p.LocalSupplierCode,
                            p.IsActive,
                            p.IsAutoPricing,
                            p.IsSpecialProduct,
                        })
                        .ToListAsync();
                    var productDict = productFields
                        .Where(p => p.ProductCode != null)
                        .ToDictionary(p => p.ProductCode!);
                    Console.WriteLine($"  [StoreRetailPrice] 基础商品总数: {productDict.Count}");

                    var semaphore = new SemaphoreSlim(5);
                    var storeReports = activeStoreCodes.Select(async storeCode =>
                    {
                        await semaphore.WaitAsync();
                        ISqlSugarClient? storeDb = null;
                        try
                        {
                            storeDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                            storeDb.Ado.CommandTimeOut = CommandTimeoutSeconds;

                            var deleted = 0;
                            var added = 0;

                            var storeRecords = await storeDb
                                .Queryable<StoreRetailPrice>()
                                .Where(sr => sr.StoreCode == storeCode && sr.IsDeleted == false)
                                .Select(sr => sr.ProductCode)
                                .ToListAsync();

                            var storeProductSet = new HashSet<string>(storeRecords);

                            var orphanedProductCodes = storeRecords
                                .Where(pc => !productDict.ContainsKey(pc))
                                .ToList();

                            var missingProductCodes = productDict
                                .Keys.Where(pc => !storeProductSet.Contains(pc))
                                .ToList();

                            Console.WriteLine(
                                $"    [分店 {storeCode}] 现有: {storeRecords.Count}, 孤立: {orphanedProductCodes.Count}, 缺失: {missingProductCodes.Count}"
                            );

                            if (orphanedProductCodes.Count > 0)
                            {
                                if (dryRun)
                                {
                                    deleted = orphanedProductCodes.Count;
                                }
                                else
                                {
                                    deleted = await storeDb
                                        .Updateable<StoreRetailPrice>()
                                        .SetColumns(sr => sr.IsDeleted == true)
                                        .SetColumns(sr => sr.UpdatedAt == DateTime.UtcNow)
                                        .SetColumns(sr => sr.UpdatedBy == "IntegrityFix")
                                        .Where(sr =>
                                            sr.StoreCode == storeCode
                                            && sr.IsDeleted == false
                                            && orphanedProductCodes.Contains(sr.ProductCode)
                                        )
                                        .ExecuteCommandAsync();
                                }
                            }

                            if (missingProductCodes.Count > 0)
                            {
                                var newRecords = missingProductCodes
                                    .Select(pc =>
                                    {
                                        var p = productDict[pc];
                                        return new StoreRetailPrice
                                        {
                                            UUID = UuidHelper.GenerateUuid7(),
                                            StoreCode = storeCode,
                                            ProductCode = pc,
                                            StoreProductCode = pc,
                                            SupplierCode = p.LocalSupplierCode,
                                            PurchasePrice = p.PurchasePrice,
                                            StoreRetailPriceValue = p.RetailPrice,
                                            DiscountRate = 1.0000m,
                                            IsActive = p.IsActive,
                                            IsAutoPricing = p.IsAutoPricing,
                                            IsSpecialProduct = p.IsSpecialProduct,
                                            IsDeleted = false,
                                            CreatedAt = DateTime.UtcNow,
                                            CreatedBy = "IntegrityFix",
                                        };
                                    })
                                    .ToList();

                                if (dryRun)
                                {
                                    added = newRecords.Count;
                                }
                                else
                                {
                                    await BatchOperationHelper.BatchInsertAsync(
                                        storeDb,
                                        newRecords,
                                        BatchOperationHelper.LARGE_BATCH_SIZE
                                    );
                                    added = newRecords.Count;
                                }
                            }

                            Console.WriteLine(
                                dryRun
                                    ? $"    [分店 {storeCode}] DryRun 预计删除 {deleted}，新增 {added}"
                                    : $"    [分店 {storeCode}] 已删除 {deleted}，已新增 {added}"
                            );

                            return (Deleted: deleted, Added: added);
                        }
                        finally
                        {
                            semaphore.Release();
                            storeDb?.Dispose();
                        }
                    });

                    var results = await Task.WhenAll(storeReports);
                    report.DeletedCount = results.Sum(r => r.Deleted);
                    report.AddedCount = results.Sum(r => r.Added);
                    Console.WriteLine(
                        $"  [StoreRetailPrice] 总计删除: {report.DeletedCount}，新增: {report.AddedCount}"
                    );
                }
                finally
                {
                    db.Ado.CommandTimeOut = originalTimeout;
                }

                _logger.LogInformation(
                    "StoreRetailPrice 修复：删除 {Deleted}，新增 {Added}（DryRun={DryRun}）",
                    report.DeletedCount,
                    report.AddedCount,
                    dryRun
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修复 StoreRetailPrice 失败");
                Console.WriteLine($"  [StoreRetailPrice] ❌ 修复失败: {ex.Message}");
                report.Errors.Add(ex.Message);
                report.ErrorCount++;
            }

            return report;
        }

        /// <summary>
        /// 修复 StoreMultiCodeProduct（分店多码表）
        /// 和 ProductSetCode（套装编码表）对比：
        /// 1. 软删除孤立记录（分店多码表中存在但 ProductSetCode 中没有对应记录）
        /// 2. 为每个分店补充 ProductSetCode 中有但分店多码表中没有的记录
        /// </summary>
        private async Task<TableFixReport> FixStoreMultiCodeProductAsync(
            List<string> activeStoreCodes,
            bool dryRun
        )
        {
            var report = new TableFixReport { TableName = "StoreMultiCodeProduct" };

            try
            {
                var db = _db.Db;
                var originalTimeout = db.Ado.CommandTimeOut;
                db.Ado.CommandTimeOut = CommandTimeoutSeconds;
                try
                {
                    var productSetCodes = await db.Queryable<ProductSetCode>()
                        .Where(p => p.IsDeleted == false && p.IsActive == true)
                        .ToListAsync();
                    Console.WriteLine(
                        $"  [StoreMultiCodeProduct] 套装编码总数: {productSetCodes.Count}"
                    );

                    var setCodeDict = productSetCodes
                        .GroupBy(sc => sc.ProductCode)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    var setCodeKeySet = new HashSet<string>(
                        productSetCodes.Select(sc => $"{sc.ProductCode}\u0001{sc.SetProductCode}")
                    );

                    var semaphore = new SemaphoreSlim(5);
                    var storeReports = activeStoreCodes.Select(async storeCode =>
                    {
                        await semaphore.WaitAsync();
                        ISqlSugarClient? storeDb = null;
                        try
                        {
                            storeDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                            storeDb.Ado.CommandTimeOut = CommandTimeoutSeconds;

                            var deleted = 0;
                            var added = 0;

                            var storeRecords = await storeDb
                                .Queryable<StoreMultiCodeProduct>()
                                .Where(sm => sm.StoreCode == storeCode && sm.IsDeleted == false)
                                .Select(sm => new { sm.ProductCode, sm.MultiCodeProductCode })
                                .ToListAsync();

                            var storeKeySet = new HashSet<string>(
                                storeRecords.Select(r =>
                                    $"{r.ProductCode}\u0001{r.MultiCodeProductCode}"
                                )
                            );

                            var orphanedRecords = storeRecords
                                .Where(r =>
                                    !setCodeDict.ContainsKey(r.ProductCode)
                                    || !setCodeDict[r.ProductCode]
                                        .Any(sc => sc.SetProductCode == r.MultiCodeProductCode)
                                )
                                .ToList();

                            var missingEntries = productSetCodes
                                .Where(sc =>
                                    !storeKeySet.Contains(
                                        $"{sc.ProductCode}\u0001{sc.SetProductCode}"
                                    )
                                )
                                .ToList();

                            Console.WriteLine(
                                $"    [分店 {storeCode}] 现有: {storeRecords.Count}, 孤立: {orphanedRecords.Count}, 缺失: {missingEntries.Count}"
                            );

                            if (orphanedRecords.Count > 0)
                            {
                                if (dryRun)
                                {
                                    deleted = orphanedRecords.Count;
                                }
                                else
                                {
                                    var orphanedProductCodes = orphanedRecords
                                        .Select(r => r.ProductCode)
                                        .Distinct()
                                        .ToList();
                                    var orphanedMultiCodes = orphanedRecords
                                        .Select(r => r.MultiCodeProductCode)
                                        .Distinct()
                                        .ToList();

                                    deleted = await storeDb
                                        .Updateable<StoreMultiCodeProduct>()
                                        .SetColumns(sm => sm.IsDeleted == true)
                                        .SetColumns(sm => sm.UpdatedAt == DateTime.UtcNow)
                                        .SetColumns(sm => sm.UpdatedBy == "IntegrityFix")
                                        .Where(sm =>
                                            sm.StoreCode == storeCode
                                            && sm.IsDeleted == false
                                            && orphanedProductCodes.Contains(sm.ProductCode)
                                            && orphanedMultiCodes.Contains(sm.MultiCodeProductCode)
                                        )
                                        .ExecuteCommandAsync();
                                }
                            }

                            if (missingEntries.Count > 0)
                            {
                                var newRecords = missingEntries
                                    .Select(sc => new StoreMultiCodeProduct
                                    {
                                        UUID = UuidHelper.GenerateUuid7(),
                                        StoreCode = storeCode,
                                        ProductCode = sc.ProductCode,
                                        MultiCodeProductCode = sc.SetProductCode,
                                        StoreMultiCodeProductCode = sc.SetProductCode,
                                        MultiBarcode = sc.SetBarcode,
                                        PurchasePrice = sc.SetPurchasePrice,
                                        MultiCodeRetailPrice = sc.SetRetailPrice,
                                        DiscountRate = 0m,
                                        IsAutoPricing = false,
                                        IsSpecialProduct = false,
                                        IsActive = true,
                                        IsDeleted = false,
                                        CreatedAt = DateTime.UtcNow,
                                        CreatedBy = "IntegrityFix",
                                    })
                                    .ToList();

                                if (dryRun)
                                {
                                    added = newRecords.Count;
                                }
                                else
                                {
                                    await BatchOperationHelper.BatchInsertAsync(
                                        storeDb,
                                        newRecords,
                                        BatchOperationHelper.LARGE_BATCH_SIZE
                                    );
                                    added = newRecords.Count;
                                }
                            }

                            Console.WriteLine(
                                dryRun
                                    ? $"    [分店 {storeCode}] DryRun 预计删除 {deleted}，新增 {added}"
                                    : $"    [分店 {storeCode}] 已删除 {deleted}，已新增 {added}"
                            );

                            return (Deleted: deleted, Added: added);
                        }
                        finally
                        {
                            semaphore.Release();
                            storeDb?.Dispose();
                        }
                    });

                    var results = await Task.WhenAll(storeReports);
                    report.DeletedCount = results.Sum(r => r.Deleted);
                    report.AddedCount = results.Sum(r => r.Added);
                    Console.WriteLine(
                        $"  [StoreMultiCodeProduct] 总计删除: {report.DeletedCount}，新增: {report.AddedCount}"
                    );
                }
                finally
                {
                    db.Ado.CommandTimeOut = originalTimeout;
                }

                _logger.LogInformation(
                    "StoreMultiCodeProduct 修复：删除 {Deleted}，新增 {Added}（DryRun={DryRun}）",
                    report.DeletedCount,
                    report.AddedCount,
                    dryRun
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修复 StoreMultiCodeProduct 失败");
                Console.WriteLine($"  [StoreMultiCodeProduct] ❌ 修复失败: {ex.Message}");
                report.Errors.Add(ex.Message);
                report.ErrorCount++;
            }

            return report;
        }

        /// <summary>
        /// 修复 ProductSetCode（套装编码表）
        /// 只做软删除孤立记录（商品编码不在基础表中）
        /// 注意：ProductSetCode 是独立维护的数据，不存在"缺失需添加"的场景
        /// </summary>
        private async Task<TableFixReport> FixProductSetCodeAsync(bool dryRun)
        {
            var report = new TableFixReport { TableName = "ProductSetCode" };

            try
            {
                var db = _db.Db;
                var originalTimeout = db.Ado.CommandTimeOut;
                db.Ado.CommandTimeOut = CommandTimeoutSeconds;
                try
                {
                    Console.WriteLine($"  [ProductSetCode] 软删除孤立记录...");

                    var deleteSql =
                        @"UPDATE ProductSetCode
                          SET IsDeleted = 1, UpdatedAt = @now, UpdatedBy = 'IntegrityFix'
                          WHERE IsDeleted = 0
                          AND NOT EXISTS (
                              SELECT 1 FROM Product p WHERE p.ProductCode = ProductSetCode.ProductCode AND p.IsDeleted = 0
                          )";

                    if (dryRun)
                    {
                        var countSql =
                            @"SELECT COUNT(*) FROM ProductSetCode
                              WHERE IsDeleted = 0
                              AND NOT EXISTS (
                                  SELECT 1 FROM Product p WHERE p.ProductCode = ProductSetCode.ProductCode AND p.IsDeleted = 0
                              )";
                        report.DeletedCount = await db.Ado.GetIntAsync(countSql);
                        Console.WriteLine(
                            $"  [ProductSetCode] DryRun 预计删除: {report.DeletedCount} 条"
                        );
                    }
                    else
                    {
                        report.DeletedCount = await db.Ado.ExecuteCommandAsync(
                            deleteSql,
                            new { now = DateTime.UtcNow }
                        );
                        Console.WriteLine($"  [ProductSetCode] 实际删除: {report.DeletedCount} 条");
                    }

                    _logger.LogInformation(
                        "ProductSetCode 修复：删除 {Deleted}（DryRun={DryRun}）",
                        report.DeletedCount,
                        dryRun
                    );
                }
                finally
                {
                    db.Ado.CommandTimeOut = originalTimeout;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修复 ProductSetCode 失败");
                Console.WriteLine($"  [ProductSetCode] ❌ 修复失败: {ex.Message}");
                report.Errors.Add(ex.Message);
                report.ErrorCount++;
            }

            return report;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 加载基础商品表字典（商品编码 → 商品实体）
        /// </summary>
        private async Task<Dictionary<string, Product>> LoadProductDictAsync()
        {
            var products = await _db
                .ProductDb.AsQueryable()
                .Where(p => p.IsDeleted == false && p.ProductCode != null)
                .ToListAsync();
            return products.Where(p => p.ProductCode != null).ToDictionary(p => p.ProductCode!);
        }

        /// <summary>
        /// 获取活跃分店列表
        /// 如果指定了 storeCodes 则筛选对应分店，否则返回所有活跃分店
        /// </summary>
        private async Task<List<Store>> GetActiveStoresAsync(List<string>? storeCodes)
        {
            var query = _db.StoreDb.AsQueryable().Where(s => s.IsDeleted == false && s.IsActive == true);

            if (storeCodes != null && storeCodes.Count > 0)
            {
                query = query.Where(s => storeCodes.Contains(s.StoreCode));
            }

            return await query.ToListAsync();
        }

        #endregion
    }
}
