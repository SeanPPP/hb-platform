using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Logging;

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
        public static IReadOnlyCollection<string> ActiveKeys => _activeKeys;

        /// <summary>
        /// 生成仪表板汇总数据缓存键
        /// </summary>
        public static string Summary(DateRangeDto dateRange, List<string>? branchCodes)
        {
            var key = $"{PREFIX}:Summary:{Hash(dateRange, branchCodes)}";
            _activeKeys.Add(key);
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
            _activeKeys.Add(key);
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
            _activeKeys.Add(key);
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
            string? supplierCode = null
        )
        {
            var key = $"{PREFIX}:SupplierRank:{Hash(dateRange, branchCodes, topN, supplierCode)}";
            _activeKeys.Add(key);
            LogKeyGenerated("SupplierRank", key, dateRange, branchCodes, topN, supplierCode);
            return key;
        }

        /// <summary>
        /// 生成中国供应商销售排名缓存键
        /// </summary>
        public static string ChinaSupplierRank(
            DateRangeDto dateRange,
            List<string>? branchCodes,
            int topN,
            string? supplierCode = null
        )
        {
            var key = $"{PREFIX}:ChinaSupplierRank:{Hash(dateRange, branchCodes, topN, supplierCode)}";
            _activeKeys.Add(key);
            LogKeyGenerated("ChinaSupplierRank", key, dateRange, branchCodes, topN, supplierCode);
            return key;
        }

        /// <summary>
        /// 生成供应商分店销售数据缓存键
        /// </summary>
        public static string SupplierStore(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes
        )
        {
            var key = $"{PREFIX}:SupplierStore:{Hash(dateRange, supplierCodes, branchCodes)}";
            _activeKeys.Add(key);
            LogKeyGenerated("SupplierStore", key, dateRange, supplierCodes, branchCodes);
            return key;
        }

        /// <summary>
        /// 生成中国供应商分店销售数据缓存键
        /// </summary>
        public static string ChinaSupplierStore(
            DateRangeDto dateRange,
            List<string> supplierCodes,
            List<string>? branchCodes
        )
        {
            var key = $"{PREFIX}:ChinaSupplierStore:{Hash(dateRange, supplierCodes, branchCodes)}";
            _activeKeys.Add(key);
            LogKeyGenerated("ChinaSupplierStore", key, dateRange, supplierCodes, branchCodes);
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
            _activeKeys.Add(key);
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
            _activeKeys.Add(key);
            LogKeyGenerated("ProductDetail", key, dateRange, branchCodes, supplierCodes, pageIndex, pageSize);
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
            string? productSearch = null
        )
        {
            var normalizedProductSearch = string.IsNullOrWhiteSpace(productSearch) ? null : productSearch.Trim();
            var key = $"{PREFIX}:EnhancedProductDetail:{Hash(dateRange, branchCodes, localSupplierCodes, chinaSupplierCodes, pageIndex, pageSize, normalizedProductSearch)}";
            _activeKeys.Add(key);
            // 搜索词可能包含货号/条码，缓存隔离要参与 hash，但日志只能记录是否有搜索。
            LogKeyGenerated("EnhancedProductDetail", key, dateRange, branchCodes, localSupplierCodes, chinaSupplierCodes, pageIndex, pageSize, $"HasProductSearch={normalizedProductSearch is not null}");
            return key;
        }

        /// <summary>
        /// 生成产品各分店销售数据缓存键
        /// </summary>
        public static string ProductBranch(
            DateRangeDto dateRange,
            string productCode,
            List<string>? branchCodes
        )
        {
            // 商品分店下钻必须把分店范围放进缓存键，避免不同权限/过滤条件串数据。
            var key = $"{PREFIX}:ProductBranch:{Hash(dateRange, productCode, branchCodes)}";
            _activeKeys.Add(key);
            LogKeyGenerated("ProductBranch", key, dateRange, productCode, branchCodes);
            return key;
        }

        /// <summary>
        /// 生成热销商品全平台排名缓存键
        /// 热销排名不按分店拆分，只按日期和分页缓存。
        /// </summary>
        public static string BestSellers(DateRangeDto dateRange, int pageIndex, int pageSize)
        {
            var key = $"{PREFIX}:BestSellers:{Hash(dateRange, pageIndex, pageSize)}";
            _activeKeys.Add(key);
            LogKeyGenerated("BestSellers", key, dateRange, pageIndex, pageSize);
            return key;
        }

        /// <summary>
        /// 清除所有活动缓存键
        /// </summary>
        public static void ClearActiveKeys()
        {
            _activeKeys.Clear();
        }

        /// <summary>
        /// 获取缓存键前缀
        /// </summary>
        public static string Prefix => PREFIX;

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
