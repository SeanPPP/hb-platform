using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// HQ 套装子项与本地记录的身份命中类型。
    /// </summary>
    internal enum ProductSetCodeIdentityMatchKind
    {
        None,
        GuidOnly,
        KeyOnly,
        SameRecord,
        Conflict,
    }

    internal readonly record struct ProductSetCodeIdentity(string? Guid, string? BusinessKey);

    internal sealed record ProductSetCodeIdentityResolution(
        ProductSetCodeIdentityMatchKind Kind,
        ProductSetCode? MatchedRow,
        string? Guid,
        string? BusinessKey,
        IReadOnlyList<ProductSetCode> GuidMatches,
        IReadOnlyList<ProductSetCode> KeyMatches
    )
    {
        public IEnumerable<ProductSetCode> AllMatches => GuidMatches
            .Concat(KeyMatches)
            .Distinct();
    }

    internal enum ProductSetCodeSourceConflictKind
    {
        GuidMapsToMultipleKeys,
        KeyMapsToMultipleGuids,
        KeyHasMixedGuidPresence,
    }

    internal sealed record ProductSetCodeSourceConflict(
        ProductSetCodeSourceConflictKind Kind,
        IReadOnlyList<string> Guids,
        IReadOnlyList<string> BusinessKeys,
        IReadOnlyList<long> SourceIds
    )
    {
        public string ToErrorMessage()
        {
            var guidText = Guids.Count == 0 ? "(空)" : string.Join(",", Guids);
            var keyText = BusinessKeys.Count == 0
                ? "(空)"
                : string.Join(",", BusinessKeys.Select(ProductSetCodeIdentityResolver.FormatBusinessKey));
            return Kind switch
            {
                ProductSetCodeSourceConflictKind.GuidMapsToMultipleKeys =>
                    $"HQ ProductSetCode 身份冲突：同一 GUID 对应多个父子业务键，已整组跳过。GUID={guidText}，业务键={keyText}，HQ ID={string.Join(",", SourceIds)}",
                ProductSetCodeSourceConflictKind.KeyMapsToMultipleGuids =>
                    $"HQ ProductSetCode 身份冲突：同一父子业务键对应多个非空 GUID，已整组跳过。GUID={guidText}，业务键={keyText}，HQ ID={string.Join(",", SourceIds)}",
                _ =>
                    $"HQ ProductSetCode 身份冲突：同一父子业务键同时存在空 GUID 与非空 GUID，无法安全确定唯一身份，已整组跳过。GUID={guidText}，业务键={keyText}，HQ ID={string.Join(",", SourceIds)}",
            };
        }
    }

    internal sealed class ProductSetCodeSourcePreflightResult<T>
    {
        public ProductSetCodeSourcePreflightResult(
            IReadOnlyList<T> rows,
            IReadOnlyList<ProductSetCodeSourceConflict> conflicts,
            IReadOnlySet<string> conflictingGuids,
            IReadOnlySet<string> conflictingBusinessKeys
        )
        {
            Rows = rows;
            Conflicts = conflicts;
            ConflictingGuids = conflictingGuids;
            ConflictingBusinessKeys = conflictingBusinessKeys;
        }

        public IReadOnlyList<T> Rows { get; }

        public IReadOnlyList<ProductSetCodeSourceConflict> Conflicts { get; }

        public IReadOnlySet<string> ConflictingGuids { get; }

        public IReadOnlySet<string> ConflictingBusinessKeys { get; }
    }

    /// <summary>
    /// ProductSetCode 的共享身份解析入口。
    /// DataSync 增量/全量路径可复用同一预检与本地双索引规则，避免各自实现 GUID 优先的 Last-wins。
    /// </summary>
    internal static class ProductSetCodeIdentityResolver
    {
        private const char KeySeparator = '\u001F';
        private const char IdentitySeparator = '\u001E';

        public static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string? BuildBusinessKey(string? productCode, string? childCode)
        {
            var normalizedProductCode = Normalize(productCode);
            var normalizedChildCode = Normalize(childCode);
            return normalizedProductCode == null || normalizedChildCode == null
                ? null
                : $"{normalizedProductCode}{KeySeparator}{normalizedChildCode}";
        }

        public static ProductSetCodeIdentity CreateIdentity(
            string? guid,
            string? productCode,
            string? childCode
        )
        {
            return new ProductSetCodeIdentity(
                Normalize(guid),
                BuildBusinessKey(productCode, childCode)
            );
        }

        public static ProductSetCodeIdentity CreateIdentity(ProductSetCode row)
        {
            return CreateIdentity(row.SetCodeId, row.ProductCode, row.SetProductCode);
        }

        public static ProductSetCodeIdentityIndex CreateIndex(IEnumerable<ProductSetCode> rows)
        {
            return new ProductSetCodeIdentityIndex(rows);
        }

        public static ProductSetCodeSourcePreflightResult<T> PreflightSource<T>(
            IEnumerable<T> rows,
            Func<T, string?> guidSelector,
            Func<T, string?> productCodeSelector,
            Func<T, string?> childCodeSelector,
            Func<T, DateTime?> lastModifyDateSelector,
            Func<T, long> sourceIdSelector
        )
        {
            var candidates = rows
                .Select((row, index) =>
                {
                    var identity = CreateIdentity(
                        guidSelector(row),
                        productCodeSelector(row),
                        childCodeSelector(row)
                    );
                    return new SourceCandidate<T>(
                        row,
                        identity.Guid,
                        identity.BusinessKey,
                        lastModifyDateSelector(row),
                        sourceIdSelector(row),
                        index
                    );
                })
                .ToList();
            var conflictingIndexes = new HashSet<int>();
            var conflicts = new List<ProductSetCodeSourceConflict>();

            // 空 GUID 没有全局身份含义，不能把所有空 GUID 行误判为同一组。
            foreach (
                var group in candidates
                    .Where(candidate => candidate.Guid != null)
                    .GroupBy(candidate => candidate.Guid!, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            )
            {
                var businessKeys = group
                    .Where(candidate => candidate.BusinessKey != null)
                    .Select(candidate => candidate.BusinessKey!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (businessKeys.Count <= 1)
                {
                    continue;
                }

                var groupRows = group.ToList();
                foreach (var candidate in groupRows)
                {
                    conflictingIndexes.Add(candidate.OriginalIndex);
                }
                conflicts.Add(
                    CreateConflict(
                        ProductSetCodeSourceConflictKind.GuidMapsToMultipleKeys,
                        groupRows
                    )
                );
            }

            foreach (
                var group in candidates
                    .Where(candidate => candidate.BusinessKey != null)
                    .GroupBy(candidate => candidate.BusinessKey!, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            )
            {
                var nonEmptyGuids = group
                    .Where(candidate => candidate.Guid != null)
                    .Select(candidate => candidate.Guid!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var groupRows = group.ToList();
                var conflictKind = nonEmptyGuids.Count > 1
                    ? ProductSetCodeSourceConflictKind.KeyMapsToMultipleGuids
                    : nonEmptyGuids.Count == 1 && groupRows.Any(candidate => candidate.Guid == null)
                        ? ProductSetCodeSourceConflictKind.KeyHasMixedGuidPresence
                        : (ProductSetCodeSourceConflictKind?)null;
                if (conflictKind == null)
                {
                    continue;
                }

                foreach (var candidate in groupRows)
                {
                    conflictingIndexes.Add(candidate.OriginalIndex);
                }
                conflicts.Add(
                    CreateConflict(
                        conflictKind.Value,
                        groupRows
                    )
                );
            }

            var conflictingCandidates = candidates
                .Where(candidate => conflictingIndexes.Contains(candidate.OriginalIndex))
                .ToList();
            var conflictingGuids = conflictingCandidates
                .Where(candidate => candidate.Guid != null)
                .Select(candidate => candidate.Guid!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var conflictingBusinessKeys = conflictingCandidates
                .Where(candidate => candidate.BusinessKey != null)
                .Select(candidate => candidate.BusinessKey!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var acceptedRows = candidates
                .Where(candidate => !conflictingIndexes.Contains(candidate.OriginalIndex))
                .GroupBy(BuildSourceIdentityToken, StringComparer.OrdinalIgnoreCase)
                // 完全相同身份只应用一个 HQ 快照：先取最新修改时间，再由 HQ ID 打破平局。
                .Select(group => group
                    .OrderByDescending(candidate => candidate.LastModifyDate ?? DateTime.MinValue)
                    .ThenByDescending(candidate => candidate.SourceId)
                    .First())
                .OrderBy(candidate => candidate.SourceId)
                .ThenBy(candidate => candidate.OriginalIndex)
                .Select(candidate => candidate.Row)
                .ToList();

            return new ProductSetCodeSourcePreflightResult<T>(
                acceptedRows,
                conflicts,
                conflictingGuids,
                conflictingBusinessKeys
            );
        }

        internal static string FormatBusinessKey(string businessKey)
        {
            return businessKey.Replace(KeySeparator, '/');
        }

        internal static string FormatLocalRecords(
            IEnumerable<ProductSetCode> rows,
            int maxCount = 10
        )
        {
            var localRows = rows.Distinct().ToList();
            var displayed = localRows
                .Take(Math.Max(1, maxCount))
                .Select(row =>
                {
                    var identity = CreateIdentity(row);
                    var businessKey = identity.BusinessKey == null
                        ? "(空)"
                        : FormatBusinessKey(identity.BusinessKey);
                    return $"[GUID={identity.Guid ?? "(空)"},业务键={businessKey},Type={row.SetType},Active={row.IsActive},Deleted={row.IsDeleted}]";
                })
                .ToList();
            var omittedCount = localRows.Count - displayed.Count;
            return string.Join(",", displayed)
                + (omittedCount > 0 ? $"，另有 {omittedCount} 条本地记录未展开" : string.Empty);
        }

        private static string BuildSourceIdentityToken<T>(SourceCandidate<T> candidate)
        {
            if (candidate.Guid == null && candidate.BusinessKey == null)
            {
                // 无可解析身份的异常源行不能互相吞并；后续业务校验会逐行忽略或处理。
                return $"NONE{IdentitySeparator}{candidate.SourceId}{IdentitySeparator}{candidate.OriginalIndex}";
            }

            return $"G:{candidate.Guid ?? "<NULL>"}{IdentitySeparator}K:{candidate.BusinessKey ?? "<NULL>"}";
        }

        private static ProductSetCodeSourceConflict CreateConflict<T>(
            ProductSetCodeSourceConflictKind kind,
            IReadOnlyCollection<SourceCandidate<T>> candidates
        )
        {
            return new ProductSetCodeSourceConflict(
                kind,
                candidates
                    .Where(candidate => candidate.Guid != null)
                    .Select(candidate => candidate.Guid!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                candidates
                    .Where(candidate => candidate.BusinessKey != null)
                    .Select(candidate => candidate.BusinessKey!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                candidates
                    .Select(candidate => candidate.SourceId)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList()
            );
        }

        private sealed record SourceCandidate<T>(
            T Row,
            string? Guid,
            string? BusinessKey,
            DateTime? LastModifyDate,
            long SourceId,
            int OriginalIndex
        );
    }

    internal sealed class ProductSetCodeIdentityIndex
    {
        private readonly Dictionary<string, List<ProductSetCode>> _byGuid =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ProductSetCode>> _byBusinessKey =
            new(StringComparer.OrdinalIgnoreCase);

        public ProductSetCodeIdentityIndex(IEnumerable<ProductSetCode> rows)
        {
            foreach (var row in rows)
            {
                Add(row);
            }
        }

        public ProductSetCodeIdentityResolution Resolve(
            string? guid,
            string? productCode,
            string? childCode
        )
        {
            var identity = ProductSetCodeIdentityResolver.CreateIdentity(
                guid,
                productCode,
                childCode
            );
            var guidMatches = GetMatches(_byGuid, identity.Guid);
            var keyMatches = GetMatches(_byBusinessKey, identity.BusinessKey);

            if (guidMatches.Count > 1 || keyMatches.Count > 1)
            {
                return CreateResolution(
                    ProductSetCodeIdentityMatchKind.Conflict,
                    null,
                    identity,
                    guidMatches,
                    keyMatches
                );
            }

            var guidMatch = guidMatches.SingleOrDefault();
            var keyMatch = keyMatches.SingleOrDefault();
            if (guidMatch != null && keyMatch != null)
            {
                return ReferenceEquals(guidMatch, keyMatch)
                    ? CreateResolution(
                        ProductSetCodeIdentityMatchKind.SameRecord,
                        guidMatch,
                        identity,
                        guidMatches,
                        keyMatches
                    )
                    : CreateResolution(
                        ProductSetCodeIdentityMatchKind.Conflict,
                        null,
                        identity,
                        guidMatches,
                        keyMatches
                    );
            }

            if (guidMatch != null)
            {
                return CreateResolution(
                    ProductSetCodeIdentityMatchKind.GuidOnly,
                    guidMatch,
                    identity,
                    guidMatches,
                    keyMatches
                );
            }

            if (keyMatch != null)
            {
                return CreateResolution(
                    ProductSetCodeIdentityMatchKind.KeyOnly,
                    keyMatch,
                    identity,
                    guidMatches,
                    keyMatches
                );
            }

            return CreateResolution(
                ProductSetCodeIdentityMatchKind.None,
                null,
                identity,
                guidMatches,
                keyMatches
            );
        }

        public void Add(ProductSetCode row)
        {
            var identity = ProductSetCodeIdentityResolver.CreateIdentity(row);
            AddMatch(_byGuid, identity.Guid, row);
            AddMatch(_byBusinessKey, identity.BusinessKey, row);
        }

        public void Reindex(ProductSetCode row, ProductSetCodeIdentity previousIdentity)
        {
            RemoveMatch(_byGuid, previousIdentity.Guid, row);
            RemoveMatch(_byBusinessKey, previousIdentity.BusinessKey, row);
            Add(row);
        }

        private static ProductSetCodeIdentityResolution CreateResolution(
            ProductSetCodeIdentityMatchKind kind,
            ProductSetCode? matchedRow,
            ProductSetCodeIdentity identity,
            IReadOnlyList<ProductSetCode> guidMatches,
            IReadOnlyList<ProductSetCode> keyMatches
        )
        {
            return new ProductSetCodeIdentityResolution(
                kind,
                matchedRow,
                identity.Guid,
                identity.BusinessKey,
                guidMatches,
                keyMatches
            );
        }

        private static IReadOnlyList<ProductSetCode> GetMatches(
            IReadOnlyDictionary<string, List<ProductSetCode>> index,
            string? key
        )
        {
            return key != null && index.TryGetValue(key, out var rows)
                ? rows
                : Array.Empty<ProductSetCode>();
        }

        private static void AddMatch(
            IDictionary<string, List<ProductSetCode>> index,
            string? key,
            ProductSetCode row
        )
        {
            if (key == null)
            {
                return;
            }

            if (!index.TryGetValue(key, out var rows))
            {
                rows = new List<ProductSetCode>();
                index[key] = rows;
            }
            if (!rows.Any(existing => ReferenceEquals(existing, row)))
            {
                rows.Add(row);
            }
        }

        private static void RemoveMatch(
            IDictionary<string, List<ProductSetCode>> index,
            string? key,
            ProductSetCode row
        )
        {
            if (key == null || !index.TryGetValue(key, out var rows))
            {
                return;
            }

            rows.RemoveAll(existing => ReferenceEquals(existing, row));
            if (rows.Count == 0)
            {
                index.Remove(key);
            }
        }
    }
}
