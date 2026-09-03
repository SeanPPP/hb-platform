using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BlazorApp.Api.Cache
{
    /// <summary>
    /// 销售仪表板缓存键管理器
    /// 为所有缓存数据生成唯一键，并跟踪所有活动缓存键以便清除
    /// </summary>
    public static class SalesDashboardCacheKeys
    {
        private const string PREFIX = "SalesDashboard";
        private static readonly HashSet<string> _activeKeys = new(StringComparer.Ordinal);
        private static readonly object _activeKeysLock = new();
        private static readonly Dictionary<string, int> _productSalesAnalysisActiveKeys = new(StringComparer.Ordinal);
        private static readonly object _productSalesAnalysisActiveKeysLock = new();
        private static readonly object _productSalesAnalysisCacheLifecycleLock = new();
        private static long _productSalesAnalysisGeneration;
        private static CancellationTokenSource _productSalesAnalysisGenerationCts = new();
        private static ILogger? _logger;

        /// <summary>
        /// 设置日志记录器
        /// </summary>
        public static void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 获取所有活动缓存键的只读集合
        /// </summary>
        public static IReadOnlyCollection<string> ActiveKeys
        {
            get
            {
                var keys = new List<string>();
                lock (_activeKeysLock)
                {
                    keys.AddRange(_activeKeys);
                }

                lock (_productSalesAnalysisActiveKeysLock)
                {
                    keys.AddRange(_productSalesAnalysisActiveKeys.Keys);
                }

                return keys;
            }
        }

        /// <summary>
        /// 生成仪表板汇总数据缓存键
        /// </summary>
        public static string Summary(DateRangeDto dateRange, List<string>? branchCodes)
        {
            var key = $"{PREFIX}:Summary:{Hash(dateRange, branchCodes)}";
            TrackKey(key);
            LogKeyGenerated("Summary", key, dateRange, branchCodes);
            return key;
        }

        /// <summary>
        /// 生成小时销售数据缓存键
        /// </summary>
        public static string Hourly(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            string? supplierCode
        )
        {
            var key = $"{PREFIX}:Hourly:{Hash(dateRange, branchCodes, supplierCode)}";
            TrackKey(key);
            LogKeyGenerated("Hourly", key, dateRange, branchCodes, supplierCode);
            return key;
        }

        /// <summary>
        /// 生成分店销售排名缓存键
        /// </summary>
        public static string StoreRank(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            int topN
        )
        {
            var key = $"{PREFIX}:StoreRank:{Hash(dateRange, branchCodes, topN)}";
            TrackKey(key);
            LogKeyGenerated("StoreRank", key, dateRange, branchCodes, topN);
            return key;
        }

        /// <summary>
        /// 生成供应商销售排名缓存键
        /// </summary>
        public static string SupplierRank(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            int topN,
            string? supplierCode = null,
            string? productStatisticCacheVersion = null
        )
        {
            var key = $"{PREFIX}:SupplierRank:{Hash(dateRange, branchCodes, topN, supplierCode, productStatisticCacheVersion)}";
            TrackKey(key);
            LogKeyGenerated("SupplierRank", key, dateRange, branchCodes, topN, supplierCode, productStatisticCacheVersion);
            return key;
        }

        /// <summary>
        /// 生成中国供应商销售排名缓存键
        /// </summary>
        public static string ChinaSupplierRank(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            int topN,
            string? supplierCode = null,
            string? productStatisticCacheVersion = null
        )
        {
            var key = $"{PREFIX}:ChinaSupplierRank:{Hash(dateRange, branchCodes, topN, supplierCode, productStatisticCacheVersion)}";
            TrackKey(key);
            LogKeyGenerated("ChinaSupplierRank", key, dateRange, branchCodes, topN, supplierCode, productStatisticCacheVersion);
            return key;
        }

        /// <summary>
        /// 生成供应商分店销售数据缓存键
        /// </summary>
        public static string SupplierStore(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes,
            string? productStatisticCacheVersion = null
        )
        {
            var key = $"{PREFIX}:SupplierStore:{Hash(dateRange, supplierCodes, branchCodes, productStatisticCacheVersion)}";
            TrackKey(key);
            LogKeyGenerated("SupplierStore", key, dateRange, supplierCodes, branchCodes, productStatisticCacheVersion);
            return key;
        }

        /// <summary>
        /// 生成中国供应商分店销售数据缓存键
        /// </summary>
        public static string ChinaSupplierStore(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes,
            string? productStatisticCacheVersion = null
        )
        {
            var key = $"{PREFIX}:ChinaSupplierStore:{Hash(dateRange, supplierCodes, branchCodes, productStatisticCacheVersion)}";
            TrackKey(key);
            LogKeyGenerated("ChinaSupplierStore", key, dateRange, supplierCodes, branchCodes, productStatisticCacheVersion);
            return key;
        }

        /// <summary>
        /// 生成分店供应商销售数据缓存键
        /// </summary>
        public static string StoreSupplier(
            DateRangeDto dateRange,
            List<string> branchCodes,
            int topN
        )
        {
            var key = $"{PREFIX}:StoreSupplier:{Hash(dateRange, branchCodes, topN)}";
            TrackKey(key);
            LogKeyGenerated("StoreSupplier", key, dateRange, branchCodes, topN);
            return key;
        }

        /// <summary>
        /// 生成产品销售明细缓存键
        /// </summary>
        public static string ProductDetail(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            List<string>? supplierCodes,
            int pageIndex,
            int pageSize
        )
        {
            var key = $"{PREFIX}:ProductDetail:{Hash(dateRange, branchCodes, supplierCodes, pageIndex, pageSize)}";
            TrackKey(key);
            LogKeyGenerated("ProductDetail", key, dateRange, branchCodes, supplierCodes, pageIndex, pageSize);
            return key;
        }

        /// <summary>
        /// 紧凑销售看板缓存键必须包含授权分店、所有筛选和统计水位，避免跨权限或旧统计结果串用。
        /// </summary>
        public static string CompactSalesBoard(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            List<string>? chinaSupplierCodes,
            string? productCode,
            int pageIndex,
            int pageSize,
            string? cacheVersion
        )
        {
            static List<string>? Canonicalize(IEnumerable<string>? codes)
            {
                return codes?
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var canonicalBranchCodes = Canonicalize(branchCodes);
            var canonicalChinaSupplierCodes = Canonicalize(chinaSupplierCodes);
            var key = $"{PREFIX}:CompactSalesBoard:{Hash(dateRange, canonicalBranchCodes, canonicalChinaSupplierCodes, productCode?.Trim(), pageIndex, pageSize, cacheVersion)}";
            // 紧凑看板使用商品销量分析的 generation 生命周期登记，不能作为普通键 TrackKey；
            // 否则清理期间按 key Remove 会误删同 key 的新代缓存。
            LogKeyGenerated("CompactSalesBoard", key, dateRange, canonicalBranchCodes, canonicalChinaSupplierCodes, pageIndex, pageSize, cacheVersion);
            return key;
        }

        /// <summary>
        /// 生成增强产品销售明细（含折扣信息）缓存键
        /// </summary>
        public static string EnhancedProductDetail(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            List<string>? localSupplierCodes,
            List<string>? chinaSupplierCodes,
            int pageIndex,
            int pageSize,
            string? productSearch = null,
            string? productStatisticCacheVersion = null,
            bool chinaSupplierScope = false
        )
        {
            var normalizedProductSearch = string.IsNullOrWhiteSpace(productSearch) ? null : productSearch.Trim();
            var key = $"{PREFIX}:EnhancedProductDetail:{Hash(dateRange, branchCodes, localSupplierCodes, chinaSupplierCodes, pageIndex, pageSize, normalizedProductSearch, productStatisticCacheVersion, chinaSupplierScope)}";
            TrackKey(key);
            // 搜索词可能包含货号/条码，缓存隔离要参与 hash，但日志只能记录是否有搜索；
            // 中国供应商全范围也必须隔离，避免复用未筛选商品缓存。
            LogKeyGenerated("EnhancedProductDetail", key, dateRange, branchCodes, localSupplierCodes, chinaSupplierCodes, pageIndex, pageSize, $"HasProductSearch={normalizedProductSearch is not null};ChinaSupplierScope={chinaSupplierScope}", productStatisticCacheVersion);
            return key;
        }

        /// <summary>
        /// 生成产品各分店销售数据缓存键
        /// </summary>
        public static string ProductBranch(
            DateRangeDto dateRange,
            string productCode,
            List<string>? branchCodes,
            string? productStatisticCacheVersion = null
        )
        {
            // 商品分店下钻必须把分店范围放进缓存键，避免不同权限/过滤条件串数据。
            var key = $"{PREFIX}:ProductBranch:{Hash(dateRange, productCode, branchCodes, productStatisticCacheVersion)}";
            TrackKey(key);
            LogKeyGenerated("ProductBranch", key, dateRange, productCode, branchCodes, productStatisticCacheVersion);
            return key;
        }

        /// <summary>
        /// 生成热销商品全平台排名缓存键
        /// 热销排名不按分店拆分，只按日期和分页缓存。
        /// </summary>
        public static string BestSellers(DateRangeDto dateRange, int pageIndex, int pageSize)
        {
            return BestSellers(dateRange, pageIndex, pageSize, null);
        }

        /// <summary>
        /// 生成包含商品统计水位的热销商品缓存键。
        /// </summary>
        public static string BestSellers(
            DateRangeDto dateRange,
            int pageIndex,
            int pageSize,
            string? cacheVersion
        )
        {
            var key = string.IsNullOrWhiteSpace(cacheVersion)
                ? $"{PREFIX}:BestSellers:{Hash(dateRange, pageIndex, pageSize)}"
                : $"{PREFIX}:BestSellers:{Hash(dateRange, pageIndex, pageSize, cacheVersion)}";
            TrackKey(key);
            LogKeyGenerated("BestSellers", key, dateRange, pageIndex, pageSize, cacheVersion);
            return key;
        }

        /// <summary>
        /// 生成商品销量分析候选/汇总列表缓存键。
        /// </summary>
        public static string ProductSalesAnalysisCandidates(
            ProductSalesAnalysisRequest request,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            return BuildProductSalesAnalysisKey("ProductSalesAnalysisCandidates", request, branchCodes, cacheVersion);
        }

        public static string ProductSalesAnalysisSummary(
            ProductSalesAnalysisRequest request,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            return BuildProductSalesAnalysisKey("ProductSalesAnalysisSummary", request, branchCodes, cacheVersion);
        }

        public static string ProductSalesAnalysisProductDaily(
            ProductSalesAnalysisRequest request,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            return BuildProductSalesAnalysisKey("ProductSalesAnalysisProductDaily", request, branchCodes, cacheVersion);
        }

        public static string ProductSalesAnalysisBranches(
            ProductSalesAnalysisRequest request,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            return BuildProductSalesAnalysisKey("ProductSalesAnalysisBranches", request, branchCodes, cacheVersion);
        }

        public static string ProductSalesAnalysisBranchDaily(
            ProductSalesAnalysisRequest request,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            return BuildProductSalesAnalysisKey("ProductSalesAnalysisBranchDaily", request, branchCodes, cacheVersion);
        }

        /// <summary>
        /// 生成商品销量分析可选供应商缓存键。
        /// 选项只受日期、授权分店与统计水位影响，不包含关键字或当前供应商过滤。
        /// </summary>
        public static string ProductSalesAnalysisOptions(
            DateTime startDate,
            DateTime endDate,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            var key = $"{PREFIX}:ProductSalesAnalysisOptions:{Hash(
                startDate.ToString("yyyy-MM-dd"),
                endDate.ToString("yyyy-MM-dd"),
                branchCodes,
                cacheVersion)}";
            LogKeyGenerated("ProductSalesAnalysisOptions", key, startDate, endDate, branchCodes?.Count, cacheVersion);
            return key;
        }

        private static string BuildProductSalesAnalysisKey(
            string segment,
            ProductSalesAnalysisRequest request,
            List<string>? branchCodes,
            string? cacheVersion
        )
        {
            var key = $"{PREFIX}:{segment}:{Hash(
                request.Filter.StartDate.ToString("yyyy-MM-dd"),
                request.Filter.EndDate.ToString("yyyy-MM-dd"),
                request.Filter.Keyword,
                request.Filter.AustralianSupplierCodes,
                request.Filter.ChinaSupplierCodes,
                request.Selection.Mode,
                request.Selection.IncludedProductCodes,
                request.Selection.ExcludedProductCodes,
                request.Scope?.Mode,
                request.Scope?.ProductCode,
                request.BranchCode,
                request.PageNumber,
                request.PageSize,
                request.SortBy,
                request.SortDirection,
                branchCodes,
                cacheVersion)}";
            LogKeyGenerated(
                segment,
                key,
                request.Filter.StartDate,
                request.Filter.EndDate,
                request.Filter.Keyword,
                branchCodes?.Count,
                cacheVersion
            );
            return key;
        }

        /// <summary>
        /// 清除所有活动缓存键
        /// </summary>
        public static void ClearActiveKeys()
        {
            ClearActiveKeysAndGetKeysToClear();
        }

        /// <summary>
        /// 原子地提取当前普通缓存键并切换商品销量分析的缓存代际。
        /// 普通键在独立锁内快照并清空；商品销量键在同一生命周期锁内切换 generation、
        /// 取消旧代 CancellationTokenSource 并清空登记，但不会进入返回的待 Remove 列表，
        /// 避免清理期间同 key 新代 entry 被调用方按 key 误删。商品销量旧代 entry
        /// （即使清理后才 Set）会因已取消的变更令牌立即过期。
        /// </summary>
        internal static IReadOnlyCollection<string> ClearActiveKeysAndGetKeysToClear()
        {
            var keysToClear = new List<string>();
            lock (_activeKeysLock)
            {
                keysToClear.AddRange(_activeKeys);
                _activeKeys.Clear();
            }

            // 写缓存只在内存 Set 的短临界区持有生命周期锁；clear 必须等已通过
            // generation 校验的写入完成后再切代，避免旧代迟到 Set 覆盖同 key 新代值。
            lock (_productSalesAnalysisCacheLifecycleLock)
            {
                CancellationTokenSource oldGenerationCts;
                lock (_productSalesAnalysisActiveKeysLock)
                {
                    _productSalesAnalysisGeneration++;
                    oldGenerationCts = _productSalesAnalysisGenerationCts;
                    _productSalesAnalysisGenerationCts = new CancellationTokenSource();
                    _productSalesAnalysisActiveKeys.Clear();
                }

                // 取消回调只会获取活动键锁，不会反向获取生命周期锁。
                oldGenerationCts.Cancel();
                oldGenerationCts.Dispose();
            }
            return keysToClear;
        }

        /// <summary>
        /// 捕获当前商品销量分析缓存代际，作为一次 cache miss 查询期间的预期代际 lease。
        /// 查询完成后写入缓存前，应使用 TryRegisterProductSalesAnalysisKey 校验该代际
        /// 是否仍为当前代；若期间发生过 ClearActiveKeys 切代，则放弃登记与写入。
        /// </summary>
        internal static long CaptureProductSalesAnalysisGeneration()
        {
            lock (_productSalesAnalysisActiveKeysLock)
            {
                return _productSalesAnalysisGeneration;
            }
        }

        /// <summary>
        /// 登记一个商品销量分析活动缓存键。
        /// 商品销量分析是高基数缓存，只有 Fresh 响应实际写入 MemoryCache 后才登记，
        /// 避免仅生成键就永久占用活动键集合。
        /// 返回值是当前登记 generation，供写入失败或 eviction 回调精确释放自己的登记。
        /// </summary>
        public static long RegisterProductSalesAnalysisKey(string key)
        {
            return RegisterProductSalesAnalysisKey(key, out _);
        }

        /// <summary>
        /// 登记一个商品销量分析活动缓存键，并原子返回当前代际的取消令牌。
        /// 登记与令牌读取在同一活动键锁内完成，避免读取到不匹配的 generation 与令牌。
        /// </summary>
        internal static long RegisterProductSalesAnalysisKey(string key, out IChangeToken expirationToken)
        {
            lock (_productSalesAnalysisActiveKeysLock)
            {
                RegisterProductSalesAnalysisKeyCore(key);
                expirationToken = new CancellationChangeToken(_productSalesAnalysisGenerationCts.Token);
                return _productSalesAnalysisGeneration;
            }
        }

        /// <summary>
        /// 在 cache miss 查询结束、写入缓存前调用：在同一活动键锁内校验 expectedGeneration
        /// 是否仍等于当前代际。若已切代则返回 false，调用方必须跳过缓存登记；此兼容入口
        /// 不包围后续 Set，生产写入应使用 TryExecuteProductSalesAnalysisCacheWrite。
        /// </summary>
        internal static bool TryRegisterProductSalesAnalysisKey(
            string key,
            long expectedGeneration,
            out long registrationToken,
            out IChangeToken expirationToken
        )
        {
            lock (_productSalesAnalysisActiveKeysLock)
            {
                if (expectedGeneration != _productSalesAnalysisGeneration)
                {
                    registrationToken = _productSalesAnalysisGeneration;
                    expirationToken = new CancellationChangeToken(_productSalesAnalysisGenerationCts.Token);
                    return false;
                }

                RegisterProductSalesAnalysisKeyCore(key);
                registrationToken = _productSalesAnalysisGeneration;
                expirationToken = new CancellationChangeToken(_productSalesAnalysisGenerationCts.Token);
                return true;
            }
        }

        /// <summary>
        /// 在同一生命周期临界区内完成代际校验、活动键登记和 MemoryCache 写入。
        /// 数据库查询不在锁内；这里只串行化极短的内存 Set，确保 clear 无法插入
        /// “已通过校验但尚未 Set”的窗口，也防止旧代迟到 Set 驱逐同 key 新代值。
        /// </summary>
        internal static bool TryExecuteProductSalesAnalysisCacheWrite(
            string key,
            long expectedGeneration,
            Action<long, IChangeToken> writeAction
        )
        {
            ArgumentNullException.ThrowIfNull(writeAction);

            lock (_productSalesAnalysisCacheLifecycleLock)
            {
                long registrationToken;
                IChangeToken expirationToken;
                lock (_productSalesAnalysisActiveKeysLock)
                {
                    if (expectedGeneration != _productSalesAnalysisGeneration)
                    {
                        return false;
                    }

                    RegisterProductSalesAnalysisKeyCore(key);
                    registrationToken = _productSalesAnalysisGeneration;
                    expirationToken = new CancellationChangeToken(
                        _productSalesAnalysisGenerationCts.Token
                    );
                }

                try
                {
                    writeAction(registrationToken, expirationToken);
                    return true;
                }
                catch
                {
                    UnregisterProductSalesAnalysisKey(key, registrationToken);
                    throw;
                }
            }
        }

        private static void RegisterProductSalesAnalysisKeyCore(string key)
        {
            _productSalesAnalysisActiveKeys.TryGetValue(key, out var count);
            _productSalesAnalysisActiveKeys[key] = count + 1;
        }

        /// <summary>
        /// 释放一个商品销量分析活动缓存键登记。
        /// 同一键可能被并发替换为多个 entry，因此采用引用计数；
        /// 旧 entry 的 eviction 回调不会误删仍存在的新 entry 登记。
        /// 这是无 generation 的兼容重载，仅在调用方自己持有当前 generation 时使用；
        /// 异步回调必须使用带 registrationToken 的重载。
        /// </summary>
        public static void UnregisterProductSalesAnalysisKey(string key)
        {
            lock (_productSalesAnalysisActiveKeysLock)
            {
                UnregisterProductSalesAnalysisKeyCore(key);
            }
        }

        /// <summary>
        /// 释放一个商品销量分析活动缓存键登记。
        /// 只有 registrationToken 仍等于当前 generation 时才允许释放，
        /// 因此 ClearActiveKeys 之后仍存活到此刻的旧 callback 无法影响新 generation 的登记。
        /// </summary>
        public static void UnregisterProductSalesAnalysisKey(string key, long registrationToken)
        {
            lock (_productSalesAnalysisActiveKeysLock)
            {
                if (registrationToken != _productSalesAnalysisGeneration)
                {
                    return;
                }

                UnregisterProductSalesAnalysisKeyCore(key);
            }
        }

        private static void UnregisterProductSalesAnalysisKeyCore(string key)
        {
            if (!_productSalesAnalysisActiveKeys.TryGetValue(key, out var count) || count <= 0)
            {
                return;
            }

            if (count == 1)
            {
                _productSalesAnalysisActiveKeys.Remove(key);
            }
            else
            {
                _productSalesAnalysisActiveKeys[key] = count - 1;
            }
        }

        /// <summary>
        /// 获取缓存键前缀
        /// </summary>
        public static string Prefix => PREFIX;

        private static void TrackKey(string key)
        {
            lock (_activeKeysLock)
            {
                _activeKeys.Add(key);
            }
        }

        /// <summary>
        /// 记录缓存键生成日志
        /// </summary>
        private static void LogKeyGenerated(string keyType, string key, params object?[] parameters)
        {
            if (_logger != null)
            {
                _logger.LogInformation(
                    "生成缓存键 [{KeyType}]: {CacheKey} | 参数: {Parameters}",
                    keyType,
                    key,
                    string.Join(", ", parameters.Select(p => p?.ToString() ?? "null"))
                );
            }
        }

        /// <summary>
        /// 使用 SHA256 生成缓存键哈希值
        /// 正确处理 List&lt;string&gt; 类型的参数
        /// </summary>
        private static string Hash(params object?[] values)
        {
            var parts = values.Select(v =>
            {
                if (v == null)
                    return "null";

                if (v is List<string> list)
                    return string.Join(",", list.OrderBy(x => x));

                return v.ToString() ?? "null";
            });

            var combined = string.Join("|", parts);
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            var hash = Convert.ToHexString(bytes).Substring(0, 16);

            if (_logger != null)
            {
                // 参数原文可能含搜索词、商品编码等业务标识，调试日志只输出 hash 结果。
                _logger.LogDebug(
                    "生成缓存键哈希: 参数数量=[{InputCount}], 哈希值=[{Hash}]",
                    values.Length,
                    hash
                );
            }

            return hash;
        }
    }
}
