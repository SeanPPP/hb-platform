using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React.OfflineCatalog
{
    /// <summary>
    /// 某分店某一版本的不可变离线目录索引（参考 Hbpos.Api 的 CatalogSellableIndex）。
    /// 行按 LookupKey 的 Ordinal 顺序排序；分页用二分定位游标后切窗；
    /// delta 由两个版本按 key 有序归并生成，每个 key 最多一个操作。
    /// </summary>
    public sealed class OfflineCatalogIndex
    {
        public const int MaxPageSize = 5000;
        private const string VersionPrefix = "offline-catalog-v1:";

        public OfflineCatalogIndex(
            string storeCode,
            DateTimeOffset generatedAt,
            IEnumerable<OfflineCatalogItemDto> items,
            string? catalogVersion = null)
        {
            StoreCode = storeCode.Trim();
            GeneratedAt = generatedAt;
            CatalogVersion = string.IsNullOrWhiteSpace(catalogVersion)
                ? string.Concat(VersionPrefix, Guid.NewGuid().ToString("N"))
                : catalogVersion.Trim();
            Items = items
                .Where(item => !string.IsNullOrEmpty(item.LookupKey))
                .GroupBy(item => item.LookupKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => item.LookupKey, StringComparer.Ordinal)
                .ToArray();
        }

        public string StoreCode { get; }

        public DateTimeOffset GeneratedAt { get; }

        public string CatalogVersion { get; }

        public IReadOnlyList<OfflineCatalogItemDto> Items { get; }

        public OfflineCatalogPageDto GetPage(string? cursor, int pageSize)
        {
            var normalizedCursor = cursor ?? string.Empty;
            var take = Math.Clamp(pageSize, 1, MaxPageSize);
            var start = FindFirstAfter(normalizedCursor);
            var remaining = Items.Count - start;
            var pageLength = Math.Min(take, Math.Max(remaining, 0));
            var pageItems = new OfflineCatalogItemDto[pageLength];
            for (var index = 0; index < pageLength; index++)
            {
                pageItems[index] = Items[start + index];
            }

            var hasMore = remaining > take;
            return new OfflineCatalogPageDto
            {
                StoreCode = StoreCode,
                GeneratedAt = OfflineCatalogChecksum.FormatTimestamp(GeneratedAt),
                Cursor = string.IsNullOrEmpty(normalizedCursor) ? null : normalizedCursor,
                Items = pageItems.ToList(),
                NextCursor = hasMore && pageItems.Length > 0 ? pageItems[^1].LookupKey : null,
                HasMore = hasMore,
                TotalCount = Items.Count,
                CatalogVersion = CatalogVersion,
                PageChecksum = OfflineCatalogChecksum.CreatePageChecksum(pageItems),
            };
        }

        /// <summary>两个版本均按 key 排序，做有序归并；rowVersion 相同的 key 不产生操作。</summary>
        public IReadOnlyList<OfflineCatalogDeltaOperation> GetDeltaOperations(OfflineCatalogIndex baseline)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            if (!string.Equals(StoreCode, baseline.StoreCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Offline catalog versions must belong to the same store.", nameof(baseline));
            }

            var operations = new List<OfflineCatalogDeltaOperation>();
            var baselinePosition = 0;
            var targetPosition = 0;
            var deletedAt = OfflineCatalogChecksum.FormatTimestamp(GeneratedAt);
            while (baselinePosition < baseline.Items.Count || targetPosition < Items.Count)
            {
                if (baselinePosition >= baseline.Items.Count)
                {
                    operations.Add(OfflineCatalogDeltaOperation.Upsert(Items[targetPosition++]));
                    continue;
                }

                if (targetPosition >= Items.Count)
                {
                    operations.Add(DeleteOperation(baseline.Items[baselinePosition++], deletedAt));
                    continue;
                }

                var left = baseline.Items[baselinePosition];
                var right = Items[targetPosition];
                var comparison = string.CompareOrdinal(left.LookupKey, right.LookupKey);
                if (comparison < 0)
                {
                    baselinePosition++;
                    operations.Add(DeleteOperation(left, deletedAt));
                }
                else if (comparison > 0)
                {
                    targetPosition++;
                    operations.Add(OfflineCatalogDeltaOperation.Upsert(right));
                }
                else
                {
                    baselinePosition++;
                    targetPosition++;
                    if (!string.Equals(left.RowVersion, right.RowVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        operations.Add(OfflineCatalogDeltaOperation.Upsert(right));
                    }
                }
            }

            return operations;
        }

        /// <summary>租约已固定完整操作数组；续页只在该数组上二分切片。</summary>
        public OfflineCatalogDeltaPageDto GetDeltaPage(
            OfflineCatalogIndex baseline,
            IReadOnlyList<OfflineCatalogDeltaOperation> operations,
            string? cursor,
            int pageSize)
        {
            var normalizedCursor = cursor ?? string.Empty;
            var take = Math.Clamp(pageSize, 1, MaxPageSize);
            var start = string.IsNullOrEmpty(normalizedCursor) ? 0 : FindFirstOperationAfter(operations, normalizedCursor);
            var remaining = operations.Count - start;
            var pageLength = Math.Min(take, Math.Max(remaining, 0));
            var pageOperations = new OfflineCatalogDeltaOperation[pageLength];
            for (var index = 0; index < pageLength; index++)
            {
                pageOperations[index] = operations[start + index];
            }

            var hasMore = remaining > take;
            return new OfflineCatalogDeltaPageDto
            {
                StoreCode = StoreCode,
                GeneratedAt = OfflineCatalogChecksum.FormatTimestamp(GeneratedAt),
                BaseCatalogVersion = baseline.CatalogVersion,
                TargetCatalogVersion = CatalogVersion,
                Cursor = string.IsNullOrEmpty(normalizedCursor) ? null : normalizedCursor,
                Items = pageOperations.Where(o => o.Item is not null).Select(o => o.Item!).ToList(),
                DeletedItems = pageOperations.Where(o => o.Deleted is not null).Select(o => o.Deleted!).ToList(),
                NextCursor = hasMore && pageOperations.Length > 0 ? pageOperations[^1].LookupKey : null,
                HasMore = hasMore,
                TargetTotal = Items.Count,
                PageChecksum = OfflineCatalogChecksum.CreateDeltaPageChecksum(baseline.CatalogVersion, CatalogVersion, pageOperations),
            };
        }

        private OfflineCatalogDeltaOperation DeleteOperation(OfflineCatalogItemDto item, string deletedAt)
        {
            return OfflineCatalogDeltaOperation.Delete(new OfflineCatalogDeletedItemDto
            {
                StoreCode = StoreCode,
                LookupKey = item.LookupKey,
                DeletedAt = deletedAt,
            });
        }

        /// <summary>二分定位第一个 LookupKey 大于游标的位置；空游标从 0 开始。</summary>
        private int FindFirstAfter(string cursor)
        {
            if (string.IsNullOrEmpty(cursor))
            {
                return 0;
            }

            var low = 0;
            var high = Items.Count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (string.CompareOrdinal(Items[middle].LookupKey, cursor) <= 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static int FindFirstOperationAfter(IReadOnlyList<OfflineCatalogDeltaOperation> operations, string cursor)
        {
            var low = 0;
            var high = operations.Count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (string.CompareOrdinal(operations[middle].LookupKey, cursor) <= 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }
    }

    /// <summary>补全派生字段（归一化售卖码、LookupKey、RowVersion），构建索引前必须调用。</summary>
    public static class OfflineCatalogItemFinalizer
    {
        public static OfflineCatalogItemDto Finalize(OfflineCatalogItemDto item)
        {
            item.StoreCode = item.StoreCode.Trim();
            item.LookupCodeNormalized = OfflineCatalogChecksum.NormalizeLookupCode(item.LookupCode);
            item.LookupKey = OfflineCatalogChecksum.BuildLookupKey(
                item.LookupCodeNormalized,
                item.MatchSource,
                item.ProductCode,
                item.CodeId);
            item.RowVersion = OfflineCatalogChecksum.CreateRowVersion(item);
            return item;
        }
    }
}
