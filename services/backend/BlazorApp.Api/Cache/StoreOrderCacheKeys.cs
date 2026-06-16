using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Cache
{
    /// <summary>
    /// 订货商品列表缓存键管理器
    /// 为所有缓存数据生成唯一键，并跟踪所有活动缓存键以便清除
    /// </summary>
    public static class StoreOrderCacheKeys
    {
        private const string PREFIX = "StoreOrder";
        private static readonly HashSet<string> _activeKeys = new(StringComparer.Ordinal);
        private static readonly object _activeKeysLock = new();
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
                lock (_activeKeysLock)
                {
                    return _activeKeys.ToList();
                }
            }
        }

        /// <summary>
        /// 生成商品列表缓存键
        /// </summary>
        public static string Products(StoreOrderFilterDto filter)
        {
            var key = $"{PREFIX}:Products:{Hash(filter)}";
            lock (_activeKeysLock)
            {
                _activeKeys.Add(key);
            }
            LogKeyGenerated("Products", key, filter);
            return key;
        }

        /// <summary>
        /// 清除所有活动缓存键
        /// </summary>
        public static void ClearActiveKeys()
        {
            lock (_activeKeysLock)
            {
                _activeKeys.Clear();
            }
        }

        /// <summary>
        /// 获取缓存键前缀
        /// </summary>
        public static string Prefix => PREFIX;

        /// <summary>
        /// 获取首页缓存键（用于排除清除）
        /// </summary>
        public static string GetHomePageCacheKey(int pageSize = 50)
        {
            var filter = new StoreOrderFilterDto
            {
                StoreCode = null,
                PageNumber = 1,
                PageSize = pageSize,
                ItemNumber = null,
                ProductName = null,
                CategoryGUID = null,
                LocalSupplierCode = null,
                ExcludeExistingWarehouseProducts = false,
                IncludeInactiveWarehouseProducts = false,
                ExcludeOrderGUID = null,
                SortBy = "Default",
                Grade = null,
            };
            return Products(filter);
        }

        /// <summary>
        /// 获取首页预热专用缓存键
        /// </summary>
        public static string GetHomePageWarmUpCacheKey(int pageSize)
        {
            var key = $"{PREFIX}:WarmUp:HomePage:{pageSize}";
            lock (_activeKeysLock)
            {
                _activeKeys.Add(key);
            }
            LogKeyGenerated("HomePageWarmUp", key, pageSize);
            return key;
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
        /// 基于 StoreOrderFilterDto 的所有字段生成唯一键
        /// </summary>
        private static string Hash(StoreOrderFilterDto filter)
        {
            var parts = new[]
            {
                "store-scope-checked-before-cache",
                filter.ItemNumber ?? "null",
                filter.ProductName ?? "null",
                filter.CategoryGUID ?? "null",
                filter.LocalSupplierCode ?? "null",
                filter.ExcludeExistingWarehouseProducts ? "exclude-warehouse" : "include-warehouse",
                filter.IncludeInactiveWarehouseProducts ? "include-inactive" : "active-only",
                filter.ExcludeOrderGUID ?? "null",
                filter.Grade ?? "null",
                filter.PageNumber.ToString(),
                filter.PageSize.ToString(),
                filter.SortBy ?? "Default"
            };

            var combined = string.Join("|", parts);
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            var hash = Convert.ToHexString(bytes).Substring(0, 16);

            if (_logger != null)
            {
                _logger.LogDebug(
                    "生成缓存键哈希: 输入=[{InputParts}], 组合字符串=[{Combined}], 哈希值=[{Hash}]",
                    string.Join(", ", parts),
                    combined,
                    hash
                );
            }

            return hash;
        }
    }
}
