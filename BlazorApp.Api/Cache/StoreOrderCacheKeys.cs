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
        /// 生成商品列表缓存键
        /// </summary>
        public static string Products(StoreOrderFilterDto filter)
        {
            var key = $"{PREFIX}:Products:{Hash(filter)}";
            _activeKeys.Add(key);
            LogKeyGenerated("Products", key, filter);
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
        /// 获取首页缓存键（用于排除清除）
        /// </summary>
        public static string GetHomePageCacheKey()
        {
            var filter = new StoreOrderFilterDto
            {
                PageNumber = 1,
                PageSize = 50,
                ItemNumber = null,
                ProductName = null,
                CategoryGUID = null,
                SortBy = "Default"
            };
            return Products(filter);
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
                filter.ItemNumber ?? "null",
                filter.ProductName ?? "null",
                filter.CategoryGUID ?? "null",
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

