using System.Diagnostics;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 统一计算并回写套装子项进货价。仅处理有效、未删除的 SetType=1 套装子项。
    /// </summary>
    internal sealed class SetChildPurchasePriceService
    {
        private const int MaxDetailCount = 100;
        private const int MaxCodesPerDifference = 20;
        private const int MaxStoreMultiCodeProductCodeLength = 50;
        private const string KeySeparator = "\u0001";
        private readonly ISqlSugarClient _db;

        public SetChildPurchasePriceService(ISqlSugarClient db)
        {
            _db = db;
        }

        public Task<SetChildPurchasePriceWritebackResultDto> PreviewAsync(
            SetChildPurchasePriceWritebackRequestDto request
        ) => ExecuteWithOwnedLockAsync(request, true, null, includeGlobal: true, includeStores: true);

        public async Task<SetChildPurchasePriceWritebackResultDto> WritebackAsync(
            SetChildPurchasePriceWritebackRequestDto request,
            string updatedBy
        )
        {
            return await ExecuteWithOwnedLockAsync(
                request,
                false,
                updatedBy,
                includeGlobal: true,
                includeStores: true
            );
        }

        /// <summary>
        /// 供已有业务事务调用。调用方负责事务边界；返回值可用于日志和验证。
        /// </summary>
        public Task<SetChildPurchasePriceWritebackResultDto> RecalculateAsync(
            IEnumerable<string?> productCodes,
            IEnumerable<string?>? storeCodes,
            string updatedBy
        )
        {
            return ExecuteRecalculationWithLockAsync(
                productCodes,
                storeCodes,
                updatedBy,
                includeGlobal: true
            );
        }

        internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes,
            IEnumerable<string?>? storeCodes,
            string updatedBy
        ) => ExecuteLockedAsync(lockScope, productCodes, storeCodes, updatedBy, includeGlobal: true);

        /// <summary>
        /// 复用正式重算的同一算法在既有 app lock/事务内做无写入核验。
        /// 用于货柜保存的丢响应重试：只有所有套装/多码成本均已是提交后应有值时才允许幂等成功。
        /// </summary>
        internal async Task<SetChildPurchasePriceWritebackResultDto> PreviewLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes
        )
        {
            var normalizedProducts = NormalizeCodes(productCodes);
            lockScope.EnsureCovers(_db, normalizedProducts);
            return await ExecuteCoreAsync(
                new SetChildPurchasePriceWritebackRequestDto { ProductCodes = normalizedProducts },
                dryRun: true,
                updatedBy: null,
                includeGlobal: true,
                includeStores: true
            );
        }

        internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateGlobalLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes,
            string updatedBy
        ) =>
            ExecuteLockedAsync(
                lockScope,
                productCodes,
                storeCodes: null,
                updatedBy,
                includeGlobal: true,
                includeStores: false
            );

        /// <summary>
        /// 仅重算指定门店投影，避免并发门店同步时重复写全局 ProductSetCode。
        /// </summary>
        public Task<SetChildPurchasePriceWritebackResultDto> RecalculateStoresAsync(
            IEnumerable<string?> productCodes,
            IEnumerable<string?> storeCodes,
            string updatedBy
        )
        {
            return ExecuteRecalculationWithLockAsync(
                productCodes,
                storeCodes,
                updatedBy,
                includeGlobal: false
            );
        }

        internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateStoresLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes,
            IEnumerable<string?> storeCodes,
            string updatedBy
        ) => ExecuteLockedAsync(lockScope, productCodes, storeCodes, updatedBy, includeGlobal: false);

        /// <summary>
        /// 仅重算明确受影响的门店商品组，避免把独立的商品和门店集合扩展成笛卡尔积。
        /// </summary>
        internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateStoreGroupsLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<(string? StoreCode, string? ProductCode)> groups,
            string updatedBy
        )
        {
            var normalizedGroups = groups
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.StoreCode)
                    && !string.IsNullOrWhiteSpace(x.ProductCode)
                )
                .Select(x => BuildKey(x.StoreCode!, x.ProductCode!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (normalizedGroups.Count == 0)
            {
                return Task.FromResult(new SetChildPurchasePriceWritebackResultDto());
            }

            var productCodes = normalizedGroups
                .Select(x => x.Split(KeySeparator, 2, StringSplitOptions.None)[1])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var storeCodes = normalizedGroups
                .Select(x => x.Split(KeySeparator, 2, StringSplitOptions.None)[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ExecuteLockedAsync(
                lockScope,
                productCodes,
                storeCodes,
                updatedBy,
                includeGlobal: false,
                exactStoreProductKeys: normalizedGroups
            );
        }

        /// <summary>
        /// 在调用方业务锁和事务内只校验全部活跃门店投影的结构，不写入非请求门店成本。
        /// </summary>
        internal async Task<SetChildPurchasePriceWritebackResultDto> ValidateStoreStructuresLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes
        )
        {
            var normalizedProducts = NormalizeCodes(productCodes);
            lockScope.EnsureCovers(_db, normalizedProducts);
            var result = await ExecuteCoreAsync(
                new SetChildPurchasePriceWritebackRequestDto
                {
                    ProductCodes = normalizedProducts,
                },
                dryRun: true,
                updatedBy: null,
                includeGlobal: false,
                includeStores: true,
                validateStoreStructuresOnly: true
            );
            EnsureBusinessRecalculationComplete(result, normalizedProducts, includeGlobal: false);
            return result;
        }

        /// <summary>
        /// 在调用方事务和商品锁内补齐明确缺失的门店套装/多码投影。
        /// 只新增有效总部关系对应的缺失行；额外行、重复键、停用/软删除墓碑都作为商品级失败返回。
        /// </summary>
        internal async Task<SetChildStoreRelationRepairResult> RepairMissingStoreRelationsLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IReadOnlyDictionary<string, decimal> requestedParentPurchasePrices,
            string updatedBy,
            IEnumerable<(string? StoreCode, string? ProductCode)>? exactStoreGroups = null,
            bool allowType2StoreParentPurchasePrice = false
        )
        {
            var normalizedPurchasePrices = requestedParentPurchasePrices
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.OrdinalIgnoreCase
                );
            var normalizedProducts = normalizedPurchasePrices.Keys.ToList();
            var repairResult = new SetChildStoreRelationRepairResult();
            HashSet<string>? exactStoreProductKeys = null;
            List<string>? exactStoreCodes = null;
            if (exactStoreGroups != null)
            {
                var normalizedExactGroups = exactStoreGroups
                    .Where(group =>
                        !string.IsNullOrWhiteSpace(group.StoreCode)
                        && !string.IsNullOrWhiteSpace(group.ProductCode)
                    )
                    .Select(group =>
                        (
                            StoreCode: group.StoreCode!.Trim(),
                            ProductCode: group.ProductCode!.Trim()
                        )
                    )
                    .Where(group => normalizedPurchasePrices.ContainsKey(group.ProductCode))
                    .GroupBy(
                        group => BuildKey(group.StoreCode, group.ProductCode),
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Select(group => group.First())
                    .ToList();
                exactStoreProductKeys = normalizedExactGroups
                    .Select(group => BuildKey(group.StoreCode, group.ProductCode))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                exactStoreCodes = normalizedExactGroups
                    .Select(group => group.StoreCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var exactProductCodes = normalizedExactGroups
                    .Select(group => group.ProductCode)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                normalizedProducts = normalizedProducts
                    .Where(exactProductCodes.Contains)
                    .ToList();
            }
            if (normalizedProducts.Count == 0)
            {
                return repairResult;
            }

            lockScope.EnsureCovers(_db, normalizedProducts);
            var allManagedSetRows = await _db.Queryable<ProductSetCode>()
                .Where(row =>
                    normalizedProducts.Contains(row.ProductCode)
                    && (row.SetType == 1 || row.SetType == 2)
                )
                .ToListAsync();
            var setRows = allManagedSetRows
                .Where(row => row.IsActive && !row.IsDeleted)
                .ToList();
            var historicalType1ParentCodes = allManagedSetRows
                .Where(row => row.SetType == 1)
                .Select(row => row.ProductCode.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allStoreRowsQuery = _db.Queryable<StoreMultiCodeProduct>()
                .Where(row =>
                    row.ProductCode != null && normalizedProducts.Contains(row.ProductCode)
                );
            if (exactStoreCodes != null)
            {
                allStoreRowsQuery = allStoreRowsQuery.Where(row =>
                    row.StoreCode != null && exactStoreCodes.Contains(row.StoreCode)
                );
            }
            var allStoreRows = await allStoreRowsQuery.ToListAsync();
            var storePriceRowsQuery = _db.Queryable<StoreRetailPrice>()
                .Where(row =>
                    row.ProductCode != null
                    && normalizedProducts.Contains(row.ProductCode)
                    && row.StoreCode != null
                    && row.IsActive
                    && !row.IsDeleted
                );
            if (exactStoreCodes != null)
            {
                storePriceRowsQuery = storePriceRowsQuery.Where(row =>
                    row.StoreCode != null && exactStoreCodes.Contains(row.StoreCode)
                );
            }
            var storePriceRows = await storePriceRowsQuery.ToListAsync();
            if (exactStoreProductKeys != null)
            {
                allStoreRows = allStoreRows
                    .Where(row =>
                        !string.IsNullOrWhiteSpace(row.StoreCode)
                        && !string.IsNullOrWhiteSpace(row.ProductCode)
                        && exactStoreProductKeys.Contains(BuildKey(row.StoreCode!, row.ProductCode!))
                    )
                    .ToList();
                storePriceRows = storePriceRows
                    .Where(row =>
                        !string.IsNullOrWhiteSpace(row.StoreCode)
                        && !string.IsNullOrWhiteSpace(row.ProductCode)
                        && exactStoreProductKeys.Contains(BuildKey(row.StoreCode!, row.ProductCode!))
                    )
                    .ToList();
            }
            var products = await _db.Queryable<Product>()
                .Where(row =>
                    row.ProductCode != null
                    && normalizedProducts.Contains(row.ProductCode)
                    && !row.IsDeleted
                )
                .ToListAsync();

            var setGroups = setRows
                .GroupBy(row => row.ProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var activeStoreRows = allStoreRows
                .Where(row => row.IsActive && !row.IsDeleted)
                .ToList();
            var activeStoreGroups = activeStoreRows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.StoreCode)
                    && !string.IsNullOrWhiteSpace(row.ProductCode)
                )
                .GroupBy(
                    row => BuildKey(row.StoreCode!, row.ProductCode!),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var allStoreGroups = allStoreRows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.StoreCode)
                    && !string.IsNullOrWhiteSpace(row.ProductCode)
                )
                .GroupBy(
                    row => BuildKey(row.StoreCode!, row.ProductCode!),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var productMap = products
                .GroupBy(row => row.ProductCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var storePriceGroups = storePriceRows
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.StoreCode)
                    && !string.IsNullOrWhiteSpace(row.ProductCode)
                )
                .GroupBy(
                    row => BuildKey(row.StoreCode!, row.ProductCode!),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var candidateKeys = exactStoreProductKeys == null
                ? activeStoreGroups.Keys
                    .Concat(storePriceGroups.Keys)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(exactStoreProductKeys, StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var actor = string.IsNullOrWhiteSpace(updatedBy) ? "System" : updatedBy;

            var plannedRowsByProduct = new Dictionary<
                string,
                (List<StoreMultiCodeProduct> Rows, int GroupCount)
            >(StringComparer.OrdinalIgnoreCase);
            foreach (var productCode in normalizedProducts.OrderBy(code => code, StringComparer.OrdinalIgnoreCase))
            {
                var activeRowsForProduct = activeStoreRows
                    .Where(row =>
                        string.Equals(
                            row.ProductCode?.Trim(),
                            productCode,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
                if (activeRowsForProduct.Any(row => string.IsNullOrWhiteSpace(row.StoreCode)))
                {
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_STORE_RELATION_INVALID",
                        "门店子项的分店编码为空"
                    );
                    continue;
                }
                if (!setGroups.TryGetValue(productCode, out var productSetRows))
                {
                    if (
                        historicalType1ParentCodes.Contains(productCode)
                        && activeRowsForProduct.Count > 0
                    )
                    {
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_STORE_RELATION_INVALID",
                            "总部无有效 Type1 关系但门店仍有活跃子项"
                        );
                    }
                    continue;
                }

                var structuralError = ValidateSetGroupStructure(productCode, productSetRows);
                if (structuralError != null)
                {
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_STORE_RELATION_INVALID",
                        structuralError
                    );
                    continue;
                }
                if (!productMap.TryGetValue(productCode, out var product))
                {
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_STORE_RELATION_INVALID",
                        "本地主商品不存在"
                    );
                    continue;
                }
                if (
                    allowType2StoreParentPurchasePrice
                    && productSetRows.Any(row => row.SetType != 2)
                )
                {
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_STORE_RELATION_INVALID",
                        "只有纯 Type2 多码商品允许按门店主商品成本补齐投影"
                    );
                    continue;
                }
                if (
                    !allowType2StoreParentPurchasePrice
                    && normalizedPurchasePrices[productCode] <= 0m
                )
                {
                    // 货柜和进货单等既有入口仍要求请求中的主商品进口价有效，不改变其 Type1/混合关系门禁。
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_COST_RECALCULATION_INCOMPLETE",
                        "套装或多码商品的进口价格必须大于0"
                    );
                    continue;
                }
                if (
                    productSetRows.Any(row =>
                        row.SetType == 1 && row.SetRetailPrice.GetValueOrDefault() <= 0m
                    )
                )
                {
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_COST_RECALCULATION_INCOMPLETE",
                        "Type1 套装子项零售价为空或0"
                    );
                    continue;
                }

                var requiredRows = productSetRows.ToDictionary(
                    row => row.SetProductCode.Trim(),
                    row => row,
                    StringComparer.OrdinalIgnoreCase
                );
                var plannedRows = new List<StoreMultiCodeProduct>();
                var plannedGroupCount = 0;
                var productFailed = false;
                foreach (var groupKey in candidateKeys
                    .Where(key =>
                    {
                        var parts = key.Split(KeySeparator, 2, StringSplitOptions.None);
                        return parts.Length == 2
                            && string.Equals(
                                parts[1],
                                productCode,
                                StringComparison.OrdinalIgnoreCase
                            );
                    })
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
                {
                    var keyParts = groupKey.Split(KeySeparator, 2, StringSplitOptions.None);
                    var storeCode = keyParts[0];
                    if (
                        storePriceGroups.TryGetValue(groupKey, out var matchingStorePrices)
                        && matchingStorePrices.Count > 1
                    )
                    {
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_COST_RECALCULATION_INCOMPLETE",
                            $"分店 {storeCode} 存在重复主商品价格记录",
                            storeCode
                        );
                        productFailed = true;
                        break;
                    }
                    activeStoreGroups.TryGetValue(groupKey, out var activeRows);
                    allStoreGroups.TryGetValue(groupKey, out var everyRow);
                    activeRows ??= new List<StoreMultiCodeProduct>();
                    everyRow ??= new List<StoreMultiCodeProduct>();

                    if (activeRows.Any(row => string.IsNullOrWhiteSpace(row.MultiCodeProductCode)))
                    {
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_STORE_RELATION_INVALID",
                            $"分店 {storeCode} 子项业务键为空",
                            storeCode
                        );
                        productFailed = true;
                        break;
                    }

                    var duplicateChild = activeRows
                        .GroupBy(
                            row => row.MultiCodeProductCode!.Trim(),
                            StringComparer.OrdinalIgnoreCase
                        )
                        .FirstOrDefault(group => group.Count() > 1);
                    if (duplicateChild != null)
                    {
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_STORE_RELATION_INVALID",
                            $"分店 {storeCode} 子项业务键重复: {duplicateChild.Key}",
                            storeCode
                        );
                        productFailed = true;
                        break;
                    }

                    var activeCodes = activeRows
                        .Select(row => row.MultiCodeProductCode!.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var extraCodes = activeCodes
                        .Except(requiredRows.Keys, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (extraCodes.Count > 0)
                    {
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_STORE_RELATION_INVALID",
                            $"分店 {storeCode} 存在额外子项: {FormatCodeList(extraCodes)}",
                            storeCode
                        );
                        productFailed = true;
                        break;
                    }

                    var missingCodes = requiredRows.Keys
                        .Except(activeCodes, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (missingCodes.Count == 0)
                    {
                        continue;
                    }

                    var storeParentPurchasePrice = matchingStorePrices?
                        .SingleOrDefault()
                        ?.PurchasePrice;
                    if (
                        allowType2StoreParentPurchasePrice
                        && storeParentPurchasePrice.GetValueOrDefault() <= 0m
                        && normalizedPurchasePrices[productCode] <= 0m
                    )
                    {
                        // 精确门店补齐必须采用与正式重算相同的成本优先级：门店主成本优先，再回退全局主档或仓库成本。
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_COST_RECALCULATION_INCOMPLETE",
                            $"分店 {storeCode} 主商品成本为空或0",
                            storeCode
                        );
                        productFailed = true;
                        break;
                    }

                    // 两个来源字段各自允许 50 字符，但目标组合业务键只有 50；先拒绝，避免 SQL Server 截断异常回滚整批保存。
                    var oversizedChildCode = missingCodes.FirstOrDefault(childCode =>
                        storeCode.Length + childCode.Length > MaxStoreMultiCodeProductCodeLength
                    );
                    if (oversizedChildCode != null)
                    {
                        repairResult.AddFailure(
                            productCode,
                            "SET_CHILD_STORE_RELATION_INVALID",
                            $"分店 {storeCode} 子项 {oversizedChildCode} 的组合业务键超过 {MaxStoreMultiCodeProductCodeLength} 字符",
                            storeCode
                        );
                        productFailed = true;
                        break;
                    }

                    foreach (var childCode in missingCodes)
                    {
                        var historicalRow = everyRow.FirstOrDefault(row =>
                            !string.IsNullOrWhiteSpace(row.MultiCodeProductCode)
                            && string.Equals(
                                row.MultiCodeProductCode.Trim(),
                                childCode,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );
                        if (historicalRow != null)
                        {
                            repairResult.AddFailure(
                                productCode,
                                "SET_CHILD_STORE_RELATION_TOMBSTONED",
                                $"分店 {storeCode} 子项 {childCode} 已停用或软删除，未自动复活",
                                storeCode
                            );
                            productFailed = true;
                            break;
                        }

                        var setRow = requiredRows[childCode];
                        plannedRows.Add(
                            new StoreMultiCodeProduct
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = storeCode,
                                ProductCode = productCode,
                                MultiCodeProductCode = childCode,
                                StoreMultiCodeProductCode = storeCode + childCode,
                                MultiBarcode = setRow.SetBarcode,
                                PurchasePrice = null,
                                MultiCodeRetailPrice = setRow.SetRetailPrice,
                                DiscountRate = 0m,
                                IsAutoPricing = false,
                                IsSpecialProduct = product.IsSpecialProduct,
                                IsActive = true,
                                IsDeleted = false,
                                CreatedAt = now,
                                UpdatedAt = now,
                                CreatedBy = actor,
                                UpdatedBy = actor,
                            }
                        );
                    }
                    if (productFailed)
                    {
                        break;
                    }
                    plannedGroupCount++;
                }

                if (productFailed || plannedRows.Count == 0)
                {
                    continue;
                }

                plannedRowsByProduct[productCode] = (plannedRows, plannedGroupCount);
            }

            // 全部通过预检查的关系一次插入、一次结构校验，避免事务和商品锁内按商品
            // 重复 Inspect 查询；若某商品校验失败，仅删除本次为该商品新增的投影。
            var allPlannedRows = plannedRowsByProduct.Values
                .SelectMany(plan => plan.Rows)
                .ToList();
            if (allPlannedRows.Count == 0)
            {
                return repairResult;
            }

            var inserted = 0;
            // StoreMultiCodeProduct 单行字段较多；80 行足以避开 SQL Server 参数上限，
            // 同时仍为固定分块批处理，不会按关系行退化成 N+1。
            foreach (var insertBatch in allPlannedRows.Chunk(80))
            {
                inserted += await _db.Insertable(insertBatch.ToList()).ExecuteCommandAsync();
            }
            if (inserted != allPlannedRows.Count)
            {
                throw new InvalidOperationException(
                    $"批量补齐门店套装关系数量不一致，期望 {allPlannedRows.Count}，实际 {inserted}"
                );
            }

            var validation = await InspectStoreStructuresLockedAsync(
                lockScope,
                plannedRowsByProduct.Keys,
                exactStoreProductKeys
            );
            var validationErrorsByProduct = validation.Errors
                .Where(error => !string.IsNullOrWhiteSpace(error.ProductCode))
                .GroupBy(error => error.ProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var failedProductCodes = ResolveFailedRepairProductCodes(
                validation,
                plannedRowsByProduct.Keys
            );

            var failedInsertedIds = failedProductCodes
                .SelectMany(productCode => plannedRowsByProduct[productCode].Rows)
                .Select(row => row.UUID)
                .ToList();
            if (failedInsertedIds.Count > 0)
            {
                // UUID 条件同样受 SQL Server 参数上限约束；按固定批次删除本次插入的失败投影。
                foreach (var deleteBatch in failedInsertedIds.Chunk(500))
                {
                    await _db.Deleteable<StoreMultiCodeProduct>()
                        .Where(row => deleteBatch.Contains(row.UUID))
                        .ExecuteCommandAsync();
                }
            }

            foreach (var (productCode, plan) in plannedRowsByProduct)
            {
                if (failedProductCodes.Contains(productCode))
                {
                    validationErrorsByProduct.TryGetValue(productCode, out var errors);
                    var firstError = errors?.FirstOrDefault();
                    var reason = firstError?.Reason ?? "补齐后门店子项结构仍不完整";
                    repairResult.AddFailure(
                        productCode,
                        "SET_CHILD_COST_RECALCULATION_INCOMPLETE",
                        reason,
                        firstError?.StoreCode
                    );
                    continue;
                }

                repairResult.AutoRepairedStoreGroupCount += plan.GroupCount;
                repairResult.AutoRepairedRelationCount += plan.Rows.Count;
            }

            return repairResult;
        }

        internal static HashSet<string> ResolveFailedRepairProductCodes(
            SetChildPurchasePriceWritebackResultDto validation,
            IEnumerable<string?> plannedProductCodes
        )
        {
            var plannedCodes = NormalizeCodes(plannedProductCodes)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var storeErrors = validation.Errors
                .Where(error => string.Equals(
                    error.TableName,
                    "StoreMultiCodeProduct",
                    StringComparison.OrdinalIgnoreCase
                ))
                .ToList();
            var failedProductCodes = storeErrors
                .Where(error =>
                    !string.IsNullOrWhiteSpace(error.ProductCode)
                    && plannedCodes.Contains(error.ProductCode.Trim())
                )
                .Select(error => error.ProductCode!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hasUnattributedError = storeErrors.Any(error =>
                string.IsNullOrWhiteSpace(error.ProductCode)
                || !plannedCodes.Contains(error.ProductCode.Trim())
            );
            var hasTruncatedErrors =
                validation.StoreMultiCodeProduct.SkippedGroupCount > storeErrors.Count;

            if (
                validation.StoreMultiCodeProduct.SkippedGroupCount > 0
                && (hasTruncatedErrors || hasUnattributedError || failedProductCodes.Count == 0)
            )
            {
                // 展示错误最多保留 100 条；一旦明细被截断或无法归属，必须保守撤销整批新增关系。
                failedProductCodes.UnionWith(plannedCodes);
            }

            return failedProductCodes;
        }

        /// <summary>
        /// 返回锁内结构检查结果而不抛业务完整性异常，供字段级部分成功入口使用。
        /// </summary>
        private async Task<SetChildPurchasePriceWritebackResultDto> InspectStoreStructuresLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes,
            HashSet<string>? exactStoreProductKeys = null
        )
        {
            var normalizedProducts = NormalizeCodes(productCodes);
            var exactStoreCodes = exactStoreProductKeys?
                .Select(key => key.Split(KeySeparator, 2, StringSplitOptions.None))
                .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                .Select(parts => parts[0].Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            lockScope.EnsureCovers(_db, normalizedProducts);
            return await ExecuteCoreAsync(
                new SetChildPurchasePriceWritebackRequestDto
                {
                    ProductCodes = normalizedProducts,
                    StoreCodes = exactStoreCodes,
                },
                dryRun: true,
                updatedBy: null,
                includeGlobal: false,
                includeStores: true,
                exactStoreProductKeys: exactStoreProductKeys,
                validateStoreStructuresOnly: true
            );
        }

        private async Task<SetChildPurchasePriceWritebackResultDto> ExecuteWithOwnedLockAsync(
            SetChildPurchasePriceWritebackRequestDto? request,
            bool dryRun,
            string? updatedBy,
            bool includeGlobal,
            bool includeStores
        )
        {
            request ??= new SetChildPurchasePriceWritebackRequestDto();
            var productCodes = NormalizeCodes(request.ProductCodes);
            await _db.Ado.BeginTranAsync();
            try
            {
                _ = productCodes.Count == 0
                    ? await SetChildPurchasePriceMutationLock.AcquireAllAsync(_db)
                    : await SetChildPurchasePriceMutationLock.AcquireProductsAsync(_db, productCodes);
                var result = await ExecuteCoreAsync(
                    request,
                    dryRun,
                    updatedBy,
                    includeGlobal,
                    includeStores
                );
                await _db.Ado.CommitTranAsync();
                return result;
            }
            catch
            {
                await _db.Ado.RollbackTranAsync();
                throw;
            }
        }

        private async Task<SetChildPurchasePriceWritebackResultDto> ExecuteRecalculationWithLockAsync(
            IEnumerable<string?> productCodes,
            IEnumerable<string?>? storeCodes,
            string updatedBy,
            bool includeGlobal
        )
        {
            var normalizedProducts = NormalizeCodes(productCodes);
            var ownsTransaction = _db.Ado.Transaction == null;
            if (ownsTransaction)
            {
                await _db.Ado.BeginTranAsync();
            }

            try
            {
                var lockScope = normalizedProducts.Count == 0
                    ? await SetChildPurchasePriceMutationLock.AcquireAllAsync(_db)
                    : await SetChildPurchasePriceMutationLock.AcquireProductsAsync(_db, normalizedProducts);
                var result = await ExecuteLockedAsync(
                    lockScope,
                    normalizedProducts,
                    storeCodes,
                    updatedBy,
                    includeGlobal
                );
                if (ownsTransaction)
                {
                    await _db.Ado.CommitTranAsync();
                }
                return result;
            }
            catch
            {
                if (ownsTransaction)
                {
                    await _db.Ado.RollbackTranAsync();
                }
                throw;
            }
        }

        private async Task<SetChildPurchasePriceWritebackResultDto> ExecuteLockedAsync(
            SetChildPurchasePriceLockScope lockScope,
            IEnumerable<string?> productCodes,
            IEnumerable<string?>? storeCodes,
            string updatedBy,
            bool includeGlobal,
            bool includeStores = true,
            HashSet<string>? exactStoreProductKeys = null
        )
        {
            var normalizedProducts = NormalizeCodes(productCodes);
            lockScope.EnsureCovers(_db, normalizedProducts);
            var result = await ExecuteCoreAsync(
                new SetChildPurchasePriceWritebackRequestDto
                {
                    ProductCodes = normalizedProducts,
                    StoreCodes = storeCodes == null ? null : NormalizeCodes(storeCodes),
                },
                false,
                updatedBy,
                includeGlobal,
                includeStores,
                exactStoreProductKeys
            );
            EnsureBusinessRecalculationComplete(result, normalizedProducts, includeGlobal);
            return result;
        }

        private static void EnsureBusinessRecalculationComplete(
            SetChildPurchasePriceWritebackResultDto result,
            IReadOnlyCollection<string> productCodes,
            bool includeGlobal
        )
        {
            var hasSkippedGlobal =
                includeGlobal && result.ProductSetCode.SkippedGroupCount > 0;
            var hasSkippedStore = result.StoreMultiCodeProduct.SkippedGroupCount > 0;
            if (!hasSkippedGlobal && !hasSkippedStore)
            {
                return;
            }

            // 管理员预览/回写仍返回分组错误；业务写入入口必须抛错，让其现有事务整体回滚。
            var failedProductCodes = result.Errors
                .Select(error => error.ProductCode?.Trim())
                .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                .Select(productCode => productCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(productCode => productCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var affectedCodes = failedProductCodes.Count > 0
                ? string.Join(", ", failedProductCodes)
                : productCodes.Count == 0
                    ? "全部有效套装"
                    : string.Join(", ", productCodes);
            var reasons = string.Join(
                "；",
                result.Errors.Select(error =>
                    $"{error.TableName}/{error.StoreCode ?? "总部"}/{error.ProductCode}: {error.Reason}"
                )
            );
            throw new InvalidOperationException(
                $"套装子项成本无法完整重算，主商品: {affectedCodes}。{reasons}"
            );
        }

        private async Task<SetChildPurchasePriceWritebackResultDto> ExecuteCoreAsync(
            SetChildPurchasePriceWritebackRequestDto? request,
            bool dryRun,
            string? updatedBy,
            bool includeGlobal,
            bool includeStores,
            HashSet<string>? exactStoreProductKeys = null,
            bool validateStoreStructuresOnly = false
        )
        {
            var stopwatch = Stopwatch.StartNew();
            request ??= new SetChildPurchasePriceWritebackRequestDto();
            var productFilter = NormalizeCodes(request.ProductCodes);
            var storeFilter = NormalizeCodes(request.StoreCodes);
            var result = new SetChildPurchasePriceWritebackResultDto { IsDryRun = dryRun };

            var setQuery = _db.Queryable<ProductSetCode>()
                .Where(x => (x.SetType == 1 || x.SetType == 2) && x.IsActive && !x.IsDeleted);
            if (productFilter.Count > 0)
            {
                setQuery = setQuery.Where(x => productFilter.Contains(x.ProductCode));
            }

            var setRows = await setQuery.ToListAsync();
            var productCodes = setRows
                .Select(x => x.ProductCode?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (productCodes.Count == 0 && !includeStores)
            {
                stopwatch.Stop();
                result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
                return result;
            }

            var products = productCodes.Count == 0
                ? new List<Product>()
                : await _db.Queryable<Product>()
                    .Where(x =>
                        x.ProductCode != null
                        && productCodes.Contains(x.ProductCode)
                        && !x.IsDeleted
                    )
                    .ToListAsync();
            var warehouses = productCodes.Count == 0
                ? new List<WarehouseProduct>()
                : await _db.Queryable<WarehouseProduct>()
                    .Where(x => productCodes.Contains(x.ProductCode) && !x.IsDeleted)
                    .ToListAsync();
            var productMap = products
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            var warehouseMap = warehouses
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var globalUpdates = new List<ProductSetCode>();
            var setGroups = new Dictionary<string, SetGroupContext>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;

            foreach (var group in setRows
                .GroupBy(x => x.ProductCode?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (includeGlobal)
                {
                    result.ProductSetCode.ScannedGroupCount++;
                }
                var rows = group.ToList();
                var structuralError = ValidateSetGroupStructure(group.Key, rows);
                var context = new SetGroupContext
                {
                    Rows = rows,
                    StructuralError = structuralError,
                };
                setGroups[group.Key] = context;

                if (structuralError != null)
                {
                    if (includeGlobal)
                    {
                        SkipGroup(
                            result,
                            result.ProductSetCode,
                            "ProductSetCode",
                            null,
                            group.Key,
                            structuralError
                        );
                    }
                    continue;
                }

                if (!includeGlobal)
                {
                    continue;
                }

                var parentPurchasePrice = ResolveGlobalParentPurchasePrice(
                    group.Key,
                    productMap,
                    warehouseMap
                );
                if (parentPurchasePrice <= 0)
                {
                    SkipGroup(
                        result,
                        result.ProductSetCode,
                        "ProductSetCode",
                        null,
                        group.Key,
                        "套装总进货价为空或0"
                    );
                    continue;
                }

                var type1Rows = context.Rows.Where(x => x.SetType == 1).ToList();
                if (type1Rows.Any(x => x.SetRetailPrice.GetValueOrDefault() <= 0))
                {
                    SkipGroup(
                        result,
                        result.ProductSetCode,
                        "ProductSetCode",
                        null,
                        group.Key,
                        "子项零售价为空或0"
                    );
                    continue;
                }

                result.ProductSetCode.EligibleGroupCount++;
                if (type1Rows.Count > 0)
                {
                    var allocations = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                        type1Rows,
                        parentPurchasePrice,
                        x => x.SetProductCode,
                        x => x.SetRetailPrice
                    );
                    var totalRetailPrice = type1Rows.Sum(x => x.SetRetailPrice!.Value);

                    foreach (var row in type1Rows.OrderBy(x => x.SetProductCode, StringComparer.OrdinalIgnoreCase))
                    {
                        var childCode = row.SetProductCode.Trim();
                        var expected = allocations[childCode];
                        if (row.SetPurchasePrice == expected)
                        {
                            result.ProductSetCode.UnchangedCount++;
                            continue;
                        }

                        result.ProductSetCode.PendingUpdateCount++;
                        AddSample(
                            result,
                            "ProductSetCode",
                            null,
                            group.Key,
                            childCode,
                            row.SetPurchasePrice,
                            expected,
                            parentPurchasePrice,
                            row.SetRetailPrice!.Value,
                            totalRetailPrice
                        );
                        if (!dryRun)
                        {
                            row.SetPurchasePrice = expected;
                            row.UpdatedAt = now;
                            row.UpdatedBy = updatedBy;
                            globalUpdates.Add(row);
                        }
                    }
                }

                // Type2 是普通多码商品：每个有效编码直接继承主商品成本，不依赖零售价。
                foreach (var row in context.Rows
                    .Where(x => x.SetType == 2)
                    .OrderBy(x => x.SetProductCode, StringComparer.OrdinalIgnoreCase))
                {
                    var childCode = row.SetProductCode.Trim();
                    if (row.SetPurchasePrice == parentPurchasePrice)
                    {
                        result.ProductSetCode.UnchangedCount++;
                        continue;
                    }

                    result.ProductSetCode.PendingUpdateCount++;
                    AddSample(
                        result,
                        "ProductSetCode",
                        null,
                        group.Key,
                        childCode,
                        row.SetPurchasePrice,
                        parentPurchasePrice,
                        parentPurchasePrice,
                        0m,
                        0m
                    );
                    if (!dryRun)
                    {
                        row.SetPurchasePrice = parentPurchasePrice;
                        row.UpdatedAt = now;
                        row.UpdatedBy = updatedBy;
                        globalUpdates.Add(row);
                    }
                }
            }

            var storeUpdates = includeStores
                ? await BuildStoreUpdatesAsync(
                    result,
                    setGroups,
                    productMap,
                    warehouseMap,
                    productFilter,
                    storeFilter,
                    exactStoreProductKeys,
                    dryRun,
                    updatedBy,
                    now,
                    validateStoreStructuresOnly
                )
                : new List<StoreMultiCodeProduct>();

            if (!dryRun)
            {
                if (globalUpdates.Count > 0)
                {
                    foreach (var batch in globalUpdates.Chunk(500))
                    {
                        result.ProductSetCode.UpdatedCount += await _db.Updateable(batch.ToList())
                            .UpdateColumns(x => new
                            {
                                x.SetPurchasePrice,
                                x.UpdatedAt,
                                x.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }
                }

                if (storeUpdates.Count > 0)
                {
                    foreach (var batch in storeUpdates.Chunk(500))
                    {
                        result.StoreMultiCodeProduct.UpdatedCount += await _db.Updateable(batch.ToList())
                            .UpdateColumns(x => new
                            {
                                x.PurchasePrice,
                                x.UpdatedAt,
                                x.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }
                }
            }

            stopwatch.Stop();
            result.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
            return result;
        }

        private async Task<List<StoreMultiCodeProduct>> BuildStoreUpdatesAsync(
            SetChildPurchasePriceWritebackResultDto result,
            Dictionary<string, SetGroupContext> setGroups,
            Dictionary<string, Product> productMap,
            Dictionary<string, WarehouseProduct> warehouseMap,
            List<string> productFilter,
            List<string> storeFilter,
            HashSet<string>? exactStoreProductKeys,
            bool dryRun,
            string? updatedBy,
            DateTime now,
            bool validateStoreStructuresOnly
        )
        {
            var multiQuery = _db.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.ProductCode != null
                    && x.IsActive
                    && !x.IsDeleted
                );
            if (productFilter.Count > 0)
            {
                multiQuery = multiQuery.Where(x => productFilter.Contains(x.ProductCode!));
            }
            if (storeFilter.Count > 0)
            {
                multiQuery = multiQuery.Where(x => x.StoreCode != null && storeFilter.Contains(x.StoreCode));
            }

            var storeRows = await multiQuery.ToListAsync();
            // Type2 普通多码允许总部关系停用而个别门店投影继续有效；严格的“孤儿投影”
            // 校验只属于 Type1 套装父项。历史 Type1 关系也必须保留判别能力，避免全部
            // 停用或软删除后反而绕过门店结构校验。
            var type1ParentQuery = _db.Queryable<ProductSetCode>()
                .Where(x => x.SetType == 1 && x.ProductCode != null);
            if (productFilter.Count > 0)
            {
                type1ParentQuery = type1ParentQuery.Where(x => productFilter.Contains(x.ProductCode));
            }
            var strictStructureParentCodes = (
                await type1ParentQuery.Select(x => x.ProductCode).Distinct().ToListAsync()
            )
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var productCodes = setGroups.Keys.ToList();
            var storePriceRows = new List<StoreRetailPrice>();
            if (productCodes.Count > 0)
            {
                var storePriceQuery = _db.Queryable<StoreRetailPrice>()
                    .Where(x =>
                        x.StoreCode != null
                        && x.ProductCode != null
                        && productCodes.Contains(x.ProductCode)
                        && x.IsActive
                        && !x.IsDeleted
                    );
                if (storeFilter.Count > 0)
                {
                    storePriceQuery = storePriceQuery.Where(x => storeFilter.Contains(x.StoreCode!));
                }
                storePriceRows = await storePriceQuery.ToListAsync();
            }
            var storePriceGroups = storePriceRows
                .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode) && !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => BuildKey(x.StoreCode!, x.ProductCode!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            var storeRowsByGroup = storeRows
                .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode) && !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => BuildKey(x.StoreCode!, x.ProductCode!), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> candidateKeys;
            if (exactStoreProductKeys != null)
            {
                candidateKeys = exactStoreProductKeys
                    .Where(key =>
                    {
                        var parts = key.Split(KeySeparator, 2, StringSplitOptions.None);
                        return parts.Length == 2
                            && !string.IsNullOrWhiteSpace(parts[0])
                            && !string.IsNullOrWhiteSpace(parts[1]);
                    })
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                candidateKeys = new HashSet<string>(
                    storeRowsByGroup.Keys.Concat(storePriceGroups.Keys),
                    StringComparer.OrdinalIgnoreCase
                );
                if (storeFilter.Count > 0)
                {
                    foreach (var storeCode in storeFilter)
                    {
                        foreach (var productCode in productCodes)
                        {
                            candidateKeys.Add(BuildKey(storeCode, productCode));
                        }
                    }
                }
            }

            var updates = new List<StoreMultiCodeProduct>();
            foreach (var groupKey in candidateKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                result.StoreMultiCodeProduct.ScannedGroupCount++;
                var keyParts = groupKey.Split(KeySeparator, 2, StringSplitOptions.None);
                var storeCode = keyParts[0];
                var productCode = keyParts[1];
                storeRowsByGroup.TryGetValue(groupKey, out var groupRows);
                if (!setGroups.TryGetValue(productCode, out var setContext))
                {
                    if (
                        groupRows is { Count: > 0 }
                        && strictStructureParentCodes.Contains(productCode)
                    )
                    {
                        SkipGroup(
                            result,
                            result.StoreMultiCodeProduct,
                            "StoreMultiCodeProduct",
                            storeCode,
                            productCode,
                            "总部无有效关系但门店仍有活跃子项"
                        );
                    }
                    continue;
                }
                if (setContext.StructuralError != null)
                {
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        setContext.StructuralError
                    );
                    continue;
                }

                // 必须先核对门店组的全部活跃行；预先按总部子项过滤会漏掉额外、空值和孤儿数据。
                var relatedRows = groupRows ?? new List<StoreMultiCodeProduct>();
                if (relatedRows.Any(x => string.IsNullOrWhiteSpace(x.MultiCodeProductCode)))
                {
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        "门店子项业务键为空"
                    );
                    continue;
                }

                var requiredCodes = new HashSet<string>(
                    setContext.Rows.Select(x => x.SetProductCode.Trim()),
                    StringComparer.OrdinalIgnoreCase
                );
                var duplicateChild = relatedRows
                    .GroupBy(x => x.MultiCodeProductCode!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(x => x.Count() > 1);
                if (duplicateChild != null)
                {
                    var canonicalCode = requiredCodes.FirstOrDefault(x =>
                        string.Equals(x, duplicateChild.Key, StringComparison.OrdinalIgnoreCase)
                    ) ?? duplicateChild.Key;
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        $"规范化后重复子项业务键: {canonicalCode}"
                    );
                    continue;
                }

                var actualCodes = relatedRows
                    .Select(x => x.MultiCodeProductCode!.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingCodes = requiredCodes
                    .Except(actualCodes, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var extraCodes = actualCodes
                    .Except(requiredCodes, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (missingCodes.Count > 0 || extraCodes.Count > 0)
                {
                    var differences = new List<string>();
                    if (missingCodes.Count > 0)
                    {
                        differences.Add($"缺少子项: {FormatCodeList(missingCodes)}");
                    }
                    if (extraCodes.Count > 0)
                    {
                        differences.Add($"额外子项: {FormatCodeList(extraCodes)}");
                    }
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        $"门店子项不完整: 期望 {requiredCodes.Count} 条，实际 {actualCodes.Count} 条；{string.Join("；", differences)}"
                    );
                    continue;
                }

                if (validateStoreStructuresOnly)
                {
                    result.StoreMultiCodeProduct.EligibleGroupCount++;
                    continue;
                }

                var storePriceKey = BuildKey(storeCode, productCode);
                storePriceGroups.TryGetValue(storePriceKey, out var matchingStorePrices);
                if (matchingStorePrices is { Count: > 1 })
                {
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        "重复门店主商品价格记录"
                    );
                    continue;
                }

                var parentPurchase = ResolveStoreParentPurchasePrice(
                    matchingStorePrices?.FirstOrDefault(),
                    productCode,
                    productMap,
                    warehouseMap
                );
                if (parentPurchase <= 0)
                {
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        "套装总进货价为空或0"
                    );
                    continue;
                }

                var type1SetRows = setContext.Rows.Where(x => x.SetType == 1).ToList();
                var setRetailMap = type1SetRows.ToDictionary(
                    x => x.SetProductCode.Trim(),
                    x => x.SetRetailPrice.GetValueOrDefault(),
                    StringComparer.OrdinalIgnoreCase
                );
                var allocationItems = relatedRows
                    .Where(x => setContext.Rows.Single(y =>
                        string.Equals(y.SetProductCode.Trim(), x.MultiCodeProductCode!.Trim(), StringComparison.OrdinalIgnoreCase)
                    ).SetType == 1)
                    .Select(x => new StoreAllocationItem
                    {
                        Row = x,
                        ChildCode = x.MultiCodeProductCode!.Trim(),
                        RetailPrice = x.MultiCodeRetailPrice.GetValueOrDefault() > 0
                            ? x.MultiCodeRetailPrice!.Value
                            : setRetailMap[x.MultiCodeProductCode.Trim()],
                    })
                    .ToList();
                if (allocationItems.Any(x => x.RetailPrice <= 0))
                {
                    SkipGroup(
                        result,
                        result.StoreMultiCodeProduct,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        "子项零售价为空或0"
                    );
                    continue;
                }

                result.StoreMultiCodeProduct.EligibleGroupCount++;
                if (allocationItems.Count > 0)
                {
                    var totalRetail = allocationItems.Sum(x => x.RetailPrice);
                    var allocations = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                        allocationItems,
                        parentPurchase,
                        x => x.ChildCode,
                        x => x.RetailPrice
                    );

                    foreach (var item in allocationItems.OrderBy(x => x.ChildCode, StringComparer.OrdinalIgnoreCase))
                    {
                        var expected = allocations[item.ChildCode];
                        if (item.Row.PurchasePrice == expected)
                        {
                            result.StoreMultiCodeProduct.UnchangedCount++;
                            continue;
                        }

                        result.StoreMultiCodeProduct.PendingUpdateCount++;
                        AddSample(
                            result,
                            "StoreMultiCodeProduct",
                            storeCode,
                            productCode,
                            item.ChildCode,
                            item.Row.PurchasePrice,
                            expected,
                            parentPurchase,
                            item.RetailPrice,
                            totalRetail
                        );
                        if (!dryRun)
                        {
                            item.Row.PurchasePrice = expected;
                            item.Row.UpdatedAt = now;
                            item.Row.UpdatedBy = updatedBy;
                            updates.Add(item.Row);
                        }
                    }
                }

                // 门店 Type2 先取门店主成本，再回退总部和仓库；零售价不参与成本计算。
                var type2ChildCodes = new HashSet<string>(
                    setContext.Rows.Where(x => x.SetType == 2).Select(x => x.SetProductCode.Trim()),
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (var row in relatedRows
                    .Where(x => type2ChildCodes.Contains(x.MultiCodeProductCode!.Trim()))
                    .OrderBy(x => x.MultiCodeProductCode, StringComparer.OrdinalIgnoreCase))
                {
                    if (row.PurchasePrice == parentPurchase)
                    {
                        result.StoreMultiCodeProduct.UnchangedCount++;
                        continue;
                    }

                    result.StoreMultiCodeProduct.PendingUpdateCount++;
                    AddSample(
                        result,
                        "StoreMultiCodeProduct",
                        storeCode,
                        productCode,
                        row.MultiCodeProductCode!.Trim(),
                        row.PurchasePrice,
                        parentPurchase,
                        parentPurchase,
                        0m,
                        0m
                    );
                    if (!dryRun)
                    {
                        row.PurchasePrice = parentPurchase;
                        row.UpdatedAt = now;
                        row.UpdatedBy = updatedBy;
                        updates.Add(row);
                    }
                }
            }

            return updates;
        }

        private static string FormatCodeList(IReadOnlyCollection<string> codes)
        {
            var displayedCodes = codes.Take(MaxCodesPerDifference).ToList();
            var omittedCount = codes.Count - displayedCodes.Count;
            return string.Join(", ", displayedCodes)
                + (omittedCount > 0 ? $"（另有 {omittedCount} 项未展开）" : string.Empty);
        }

        private static string? ValidateSetGroupStructure(
            string productCode,
            List<ProductSetCode> rows
        )
        {
            if (string.IsNullOrWhiteSpace(productCode))
            {
                return "主商品编码为空";
            }

            if (rows.Any(x => string.IsNullOrWhiteSpace(x.SetProductCode)))
            {
                return "子项业务键为空";
            }

            var typeConflict = rows
                .GroupBy(x => x.SetProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Select(item => item.SetType).Distinct().Count() > 1);
            if (typeConflict != null)
            {
                // 同一主商品子项只能选择一种计算规则，避免同时分摊和固定成本覆盖彼此。
                return $"同一子项存在活跃Type1/Type2冲突: {typeConflict.Key}";
            }

            var duplicate = rows
                .GroupBy(x => x.SetProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicate != null)
            {
                return $"重复子项业务键: {duplicate.Key}";
            }

            return null;
        }

        private static decimal ResolveGlobalParentPurchasePrice(
            string productCode,
            Dictionary<string, Product> productMap,
            Dictionary<string, WarehouseProduct> warehouseMap
        )
        {
            if (
                productMap.TryGetValue(productCode, out var product)
                && product.PurchasePrice.GetValueOrDefault() > 0
            )
            {
                return product.PurchasePrice!.Value;
            }

            return warehouseMap.TryGetValue(productCode, out var warehouse)
                && warehouse.ImportPrice.GetValueOrDefault() > 0
                ? warehouse.ImportPrice!.Value
                : 0m;
        }

        private static decimal ResolveStoreParentPurchasePrice(
            StoreRetailPrice? storePrice,
            string productCode,
            Dictionary<string, Product> productMap,
            Dictionary<string, WarehouseProduct> warehouseMap
        )
        {
            return storePrice?.PurchasePrice.GetValueOrDefault() > 0
                ? storePrice.PurchasePrice!.Value
                : ResolveGlobalParentPurchasePrice(productCode, productMap, warehouseMap);
        }

        private static void SkipGroup(
            SetChildPurchasePriceWritebackResultDto result,
            SetChildPurchasePriceTableReport report,
            string tableName,
            string? storeCode,
            string? productCode,
            string reason
        )
        {
            report.SkippedGroupCount++;
            if (result.Errors.Count >= MaxDetailCount)
            {
                return;
            }

            result.Errors.Add(new SetChildPurchasePriceWritebackError
            {
                TableName = tableName,
                StoreCode = storeCode,
                ProductCode = productCode,
                Reason = reason,
            });
        }

        private static void AddSample(
            SetChildPurchasePriceWritebackResultDto result,
            string tableName,
            string? storeCode,
            string productCode,
            string childCode,
            decimal? current,
            decimal expected,
            decimal parentPurchase,
            decimal childRetail,
            decimal totalRetail
        )
        {
            if (result.Samples.Count >= MaxDetailCount)
            {
                return;
            }

            result.Samples.Add(new SetChildPurchasePriceChangeSample
            {
                TableName = tableName,
                StoreCode = storeCode,
                ProductCode = productCode,
                ChildProductCode = childCode,
                CurrentPurchasePrice = current,
                ExpectedPurchasePrice = expected,
                ParentPurchasePrice = parentPurchase,
                ChildRetailPrice = childRetail,
                TotalChildRetailPrice = totalRetail,
            });
        }

        private static List<string> NormalizeCodes(IEnumerable<string?>? codes)
        {
            return codes?
                .Select(x => x?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        private static string BuildKey(string storeCode, string productCode) =>
            $"{storeCode.Trim()}{KeySeparator}{productCode.Trim()}";

        private sealed class SetGroupContext
        {
            public List<ProductSetCode> Rows { get; init; } = new();
            public string? StructuralError { get; init; }
        }

        private sealed class StoreAllocationItem
        {
            public StoreMultiCodeProduct Row { get; init; } = null!;
            public string ChildCode { get; init; } = string.Empty;
            public decimal RetailPrice { get; init; }
        }
    }

    internal sealed class SetChildStoreRelationRepairResult
    {
        public int AutoRepairedStoreGroupCount { get; set; }
        public int AutoRepairedRelationCount { get; set; }
        public Dictionary<string, SetChildStoreRelationRepairFailure> Failures { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public void AddFailure(
            string productCode,
            string code,
            string message,
            string? storeCode = null
        )
        {
            Failures.TryAdd(
                productCode,
                new SetChildStoreRelationRepairFailure
                {
                    ProductCode = productCode,
                    StoreCode = storeCode,
                    Code = code,
                    Message = message,
                }
            );
        }
    }

    internal sealed class SetChildStoreRelationRepairFailure
    {
        public string ProductCode { get; init; } = string.Empty;
        public string? StoreCode { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
