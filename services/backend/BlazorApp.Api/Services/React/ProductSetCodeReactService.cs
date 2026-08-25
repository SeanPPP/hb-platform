using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class ProductSetCodeReactService : IProductSetCodeReactService
    {
        private readonly SqlSugarContext _context;
        private readonly IStoreRetailPriceReactService _storeRetailPriceService;
        private readonly ILogger<ProductSetCodeReactService> _logger;

        public ProductSetCodeReactService(
            SqlSugarContext context,
            IStoreRetailPriceReactService storeRetailPriceService,
            ILogger<ProductSetCodeReactService> logger
        )
        {
            _context = context;
            _storeRetailPriceService = storeRetailPriceService;
            _logger = logger;
        }

        public async Task<GridResponseDto<ProductSetCodeGridDto>> GetGridDataAsync(
            ProductSetCodeGridRequestDto request
        )
        {
            try
            {
                var db = _context.Db;

                var query = db.Queryable<ProductSetCode>()
                    .InnerJoin<Product>((psc, p) => psc.ProductCode == p.ProductCode)
                    .LeftJoin<HBLocalSupplier>(
                        (psc, p, ls) => p.LocalSupplierCode == ls.LocalSupplierCode
                    )
                    .Where((psc, p, ls) => !psc.IsDeleted);

                var productCodeFilter = ResolveProductCodeFilter(request);
                if (!string.IsNullOrWhiteSpace(productCodeFilter))
                {
                    query = query.Where((psc, p, ls) => psc.ProductCode == productCodeFilter);
                }

                if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
                {
                    var keyword = request.GlobalSearch.Trim();
                    query = query.Where(
                        (psc, p, ls) =>
                            (ls.Name != null && ls.Name.Contains(keyword))
                            || (
                                p.LocalSupplierCode != null && p.LocalSupplierCode.Contains(keyword)
                            )
                            || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                            || (p.Barcode != null && p.Barcode.Contains(keyword))
                            || (psc.SetItemNumber != null && psc.SetItemNumber.Contains(keyword))
                            || (psc.SetBarcode != null && psc.SetBarcode.Contains(keyword))
                    );
                }

                if (request.FilterModel != null && request.FilterModel.Any())
                {
                    foreach (var kv in request.FilterModel)
                    {
                        var col = kv.Key;
                        var fm = kv.Value;
                        var type = (fm.Type ?? "contains").ToLower();
                        var value = fm.Filter;
                        var v = value ?? string.Empty;

                        if (
                            string.IsNullOrWhiteSpace(value)
                            && (fm.Values == null || fm.Values.Count == 0)
                        )
                            continue;

                        switch (col.ToLower())
                        {
                            case "productcode":
                                query = type switch
                                {
                                    "equals" => query.Where(
                                        (psc, p, ls) => psc.ProductCode == value
                                    ),
                                    _ => query.Where(
                                        (psc, p, ls) =>
                                            psc.ProductCode != null && psc.ProductCode.Contains(v)
                                    ),
                                };
                                break;
                            case "suppliername":
                                query = type switch
                                {
                                    "equals" => query.Where((psc, p, ls) => ls.Name == value),
                                    "startswith" => query.Where(
                                        (psc, p, ls) => ls.Name != null && ls.Name.StartsWith(v)
                                    ),
                                    "endswith" => query.Where(
                                        (psc, p, ls) => ls.Name != null && ls.Name.EndsWith(v)
                                    ),
                                    _ => query.Where(
                                        (psc, p, ls) => ls.Name != null && ls.Name.Contains(v)
                                    ),
                                };
                                break;
                            case "suppliercode":
                                query = type switch
                                {
                                    "equals" => query.Where(
                                        (psc, p, ls) => p.LocalSupplierCode == value
                                    ),
                                    "startswith" => query.Where(
                                        (psc, p, ls) =>
                                            p.LocalSupplierCode != null
                                            && p.LocalSupplierCode.StartsWith(v)
                                    ),
                                    "endswith" => query.Where(
                                        (psc, p, ls) =>
                                            p.LocalSupplierCode != null
                                            && p.LocalSupplierCode.EndsWith(v)
                                    ),
                                    _ => query.Where(
                                        (psc, p, ls) =>
                                            p.LocalSupplierCode != null
                                            && p.LocalSupplierCode.Contains(v)
                                    ),
                                };
                                break;
                            case "itemnumber":
                                query = type switch
                                {
                                    "equals" => query.Where((psc, p, ls) => p.ItemNumber == value),
                                    "startswith" => query.Where(
                                        (psc, p, ls) =>
                                            p.ItemNumber != null && p.ItemNumber.StartsWith(v)
                                    ),
                                    "endswith" => query.Where(
                                        (psc, p, ls) =>
                                            p.ItemNumber != null && p.ItemNumber.EndsWith(v)
                                    ),
                                    _ => query.Where(
                                        (psc, p, ls) =>
                                            p.ItemNumber != null && p.ItemNumber.Contains(v)
                                    ),
                                };
                                break;
                            case "barcode":
                                query = type switch
                                {
                                    "equals" => query.Where((psc, p, ls) => p.Barcode == value),
                                    _ => query.Where(
                                        (psc, p, ls) => p.Barcode != null && p.Barcode.Contains(v)
                                    ),
                                };
                                break;
                            case "setitemnumber":
                                query = type switch
                                {
                                    "equals" => query.Where(
                                        (psc, p, ls) => psc.SetItemNumber == value
                                    ),
                                    "startswith" => query.Where(
                                        (psc, p, ls) =>
                                            psc.SetItemNumber != null
                                            && psc.SetItemNumber.StartsWith(v)
                                    ),
                                    "endswith" => query.Where(
                                        (psc, p, ls) =>
                                            psc.SetItemNumber != null
                                            && psc.SetItemNumber.EndsWith(v)
                                    ),
                                    _ => query.Where(
                                        (psc, p, ls) =>
                                            psc.SetItemNumber != null
                                            && psc.SetItemNumber.Contains(v)
                                    ),
                                };
                                break;
                            case "setbarcode":
                                query = type switch
                                {
                                    "equals" => query.Where(
                                        (psc, p, ls) => psc.SetBarcode == value
                                    ),
                                    _ => query.Where(
                                        (psc, p, ls) =>
                                            psc.SetBarcode != null && psc.SetBarcode.Contains(v)
                                    ),
                                };
                                break;
                            case "isactive":
                                if (bool.TryParse(value, out var isActive))
                                {
                                    query = query.Where((psc, p, ls) => psc.IsActive == isActive);
                                }
                                break;
                        }
                    }
                }

                var total = await query.CountAsync();

                if (request.SortModel != null && request.SortModel.Any())
                {
                    var s = request.SortModel.First();
                    var asc = s.Sort.ToLower() == "asc";
                    query = s.ColId.ToLower() switch
                    {
                        "suppliername" => asc
                            ? query.OrderBy((psc, p, ls) => ls.Name)
                            : query.OrderBy((psc, p, ls) => ls.Name, OrderByType.Desc),
                        "suppliercode" => asc
                            ? query.OrderBy((psc, p, ls) => p.LocalSupplierCode)
                            : query.OrderBy((psc, p, ls) => p.LocalSupplierCode, OrderByType.Desc),
                        "itemnumber" => asc
                            ? query.OrderBy((psc, p, ls) => p.ItemNumber)
                            : query.OrderBy((psc, p, ls) => p.ItemNumber, OrderByType.Desc),
                        "barcode" => asc
                            ? query.OrderBy((psc, p, ls) => p.Barcode)
                            : query.OrderBy((psc, p, ls) => p.Barcode, OrderByType.Desc),
                        "setitemnumber" => asc
                            ? query.OrderBy((psc, p, ls) => psc.SetItemNumber)
                            : query.OrderBy((psc, p, ls) => psc.SetItemNumber, OrderByType.Desc),
                        "setbarcode" => asc
                            ? query.OrderBy((psc, p, ls) => psc.SetBarcode)
                            : query.OrderBy((psc, p, ls) => psc.SetBarcode, OrderByType.Desc),
                        "setpurchaseprice" => asc
                            ? query.OrderBy((psc, p, ls) => psc.SetPurchasePrice)
                            : query.OrderBy((psc, p, ls) => psc.SetPurchasePrice, OrderByType.Desc),
                        "setretailprice" => asc
                            ? query.OrderBy((psc, p, ls) => psc.SetRetailPrice)
                            : query.OrderBy((psc, p, ls) => psc.SetRetailPrice, OrderByType.Desc),
                        "updatedat" => asc
                            ? query.OrderBy((psc, p, ls) => psc.UpdatedAt)
                            : query.OrderBy((psc, p, ls) => psc.UpdatedAt, OrderByType.Desc),
                        "updatedby" => asc
                            ? query.OrderBy((psc, p, ls) => psc.UpdatedBy)
                            : query.OrderBy((psc, p, ls) => psc.UpdatedBy, OrderByType.Desc),
                        _ => query.OrderBy((psc, p, ls) => psc.UpdatedAt, OrderByType.Desc),
                    };
                }
                else
                {
                    query = query.OrderBy((psc, p, ls) => psc.UpdatedAt, OrderByType.Desc);
                }

                var start = Math.Max(0, request.StartRow);
                var pageSize = request.PageSize > 0 ? request.PageSize : 100;

                var items = await query
                    .Select(
                        (psc, p, ls) =>
                            new ProductSetCodeGridDto
                            {
                                SetCodeId = psc.SetCodeId,
                                ProductCode = psc.ProductCode,
                                SetProductCode = psc.SetProductCode,
                                SupplierCode = p.LocalSupplierCode,
                                SupplierName = ls.Name,
                                ItemNumber = p.ItemNumber,
                                Barcode = p.Barcode,
                                SetItemNumber = psc.SetItemNumber,
                                SetBarcode = psc.SetBarcode,
                                SetPurchasePrice = psc.SetPurchasePrice,
                                SetRetailPrice = psc.SetRetailPrice,
                                IsActive = psc.IsActive,
                                UpdatedAt = psc.UpdatedAt,
                                UpdatedBy = psc.UpdatedBy,
                            }
                    )
                    .Skip(start)
                    .Take(pageSize)
                    .ToListAsync();

                return GridResponseDto<ProductSetCodeGridDto>.OK(items, total);
            }
            catch (Exception ex)
            {
                return GridResponseDto<ProductSetCodeGridDto>.Error($"获取数据失败: {ex.Message}");
            }
        }

        private static string? ResolveProductCodeFilter(ProductSetCodeGridRequestDto request)
        {
            if (!string.IsNullOrWhiteSpace(request.ProductCode))
            {
                return request.ProductCode.Trim();
            }

            if (
                request.FilterModel != null
                && request.FilterModel.TryGetValue("productCode", out var productCodeFilter)
                && !string.IsNullOrWhiteSpace(productCodeFilter.Filter)
            )
            {
                return productCodeFilter.Filter.Trim();
            }

            return null;
        }

        public async Task<ApiResponse<bool>> BatchUpdateStatusAsync(
            List<string> ids,
            bool isActive,
            string updatedBy,
            List<string>? storeCodes = null
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var updatedCount = 0;
                var updatedMultiCodeCount = 0;
                var affectedStoreGroups = new HashSet<(string StoreCode, string ProductCode)>();

                _logger.LogInformation(
                    $"BatchUpdateStatusAsync 开始, 操作人: {updatedBy}, 套装条码ID数量: {ids.Count}, 分店数量: {storeCodes?.Count ?? 0}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var distinctIds = ids.Distinct(StringComparer.Ordinal).ToList();
                    var affectedRows = await db.Queryable<ProductSetCode>()
                        .Where(x => distinctIds.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    // 锁前保留完整业务身份，锁后必须逐项复核，不能把旧快照写回并发变更后的关系。
                    var snapshots = CaptureSetCodeLockSnapshots(affectedRows);
                    var lockProductCodes = GetSetCodeLockProductCodes(affectedRows);
                    var affectedSetProductCodes = GetAffectedSetProductCodes(affectedRows);
                    SetChildPurchasePriceLockScope? lockScope = null;
                    if (lockProductCodes.Count > 0)
                    {
                        lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            lockProductCodes
                        );
                    }
                    affectedRows = await db.Queryable<ProductSetCode>()
                        .Where(x => distinctIds.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    EnsureSetCodeLockSnapshotsUnchanged(snapshots, affectedRows);
                    lockProductCodes = GetSetCodeLockProductCodes(affectedRows);
                    affectedSetProductCodes = GetAffectedSetProductCodes(affectedRows);
                    if (lockProductCodes.Count > 0)
                    {
                        if (lockScope == null)
                        {
                            throw new InvalidOperationException("套装关系在更新期间发生变化，请重试");
                        }
                        lockScope.EnsureCovers(db, lockProductCodes);
                    }
                    var count = 0;
                    foreach (var row in affectedRows)
                    {
                        var updatedRows = await db.Updateable<ProductSetCode>()
                            .SetColumns(x => x.IsActive == isActive)
                            .SetColumns(x => x.UpdatedAt == now)
                            .SetColumns(x => x.UpdatedBy == updatedBy)
                            .Where(x =>
                                x.SetCodeId == row.SetCodeId
                                && x.ProductCode == row.ProductCode
                                && x.SetProductCode == row.SetProductCode
                                && x.SetType == row.SetType
                                && !x.IsDeleted
                            )
                            .ExecuteCommandAsync();
                        if (updatedRows != 1)
                        {
                            throw new InvalidOperationException(
                                $"套装条码 {row.SetCodeId} 在更新期间发生变化，请重试"
                            );
                        }
                        count += updatedRows;
                    }

                    _logger.LogInformation($"更新套装条码状态: {count} 条");
                    updatedCount = count;

                    // 如果提供了分店列表，同步更新 StoreMultiCodeProduct
                    if (storeCodes != null && storeCodes.Count > 0 && affectedRows.Any())
                    {
                        var normalizedStoreCodes = storeCodes
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x.Trim())
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                        foreach (var row in affectedRows.Where(x =>
                            !string.IsNullOrWhiteSpace(x.ProductCode)
                            && !string.IsNullOrWhiteSpace(x.SetProductCode)
                        ))
                        {
                            var multiCodeList = await db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.ProductCode == row.ProductCode
                                    && m.MultiCodeProductCode == row.SetProductCode
                                    && m.StoreCode != null
                                    && normalizedStoreCodes.Contains(m.StoreCode)
                                    && !m.IsDeleted
                                )
                                .ToListAsync();
                            foreach (var multiCode in multiCodeList)
                            {
                                var updatedRows = await db.Updateable<StoreMultiCodeProduct>()
                                    .SetColumns(x => x.IsActive == isActive)
                                    .SetColumns(x => x.UpdatedAt == now)
                                    .SetColumns(x => x.UpdatedBy == updatedBy)
                                    .Where(x =>
                                        x.UUID == multiCode.UUID
                                        && x.ProductCode == row.ProductCode
                                        && x.MultiCodeProductCode == row.SetProductCode
                                        && x.StoreCode == multiCode.StoreCode
                                        && !x.IsDeleted
                                    )
                                    .ExecuteCommandAsync();
                                if (updatedRows != 1)
                                {
                                    throw new InvalidOperationException(
                                        $"门店条码 {multiCode.UUID} 在更新期间发生变化，请重试"
                                    );
                                }
                                updatedMultiCodeCount += updatedRows;
                                if (
                                    !string.IsNullOrWhiteSpace(multiCode.StoreCode)
                                    && !string.IsNullOrWhiteSpace(row.ProductCode)
                                )
                                {
                                    affectedStoreGroups.Add((multiCode.StoreCode!, row.ProductCode!));
                                }
                            }
                        }
                        _logger.LogInformation($"同步更新分店一品多码状态: {updatedMultiCodeCount} 条");
                    }

                    if (affectedSetProductCodes.Count > 0)
                    {
                        var purchasePriceService = new SetChildPurchasePriceService(db);
                        // 总部状态落库后先校验全部活跃门店投影；这里只读校验，绝不改写非请求门店。
                        await purchasePriceService.ValidateStoreStructuresLockedAsync(
                            lockScope!,
                            affectedSetProductCodes
                        );
                        var globalRecalculateResult = await purchasePriceService
                            .RecalculateGlobalLockedAsync(
                                lockScope!,
                                affectedSetProductCodes,
                                updatedBy
                            );
                        EnsureNoSkippedSetGroups(globalRecalculateResult);

                        if (affectedStoreGroups.Count > 0)
                        {
                            // 每项请求只重算真正更新过的门店/商品三元组，禁止把独立集合扩成笛卡尔积。
                            var storeRecalculateResult = await purchasePriceService
                                .RecalculateStoreGroupsLockedAsync(
                                    lockScope!,
                                    affectedStoreGroups.Select(x =>
                                        ((string?)x.StoreCode, (string?)x.ProductCode)
                                    ),
                                    updatedBy
                                );
                            EnsureNoSkippedSetGroups(storeRecalculateResult);
                        }
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    var message = $"已更新 {updatedCount} 条状态";
                    if (updatedMultiCodeCount > 0)
                    {
                        message += $"，已同步到 {updatedMultiCodeCount} 条分店一品多码";
                    }

                    return ApiResponse<bool>.OK(true, message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量更新状态事务失败, 事务已回滚");
                    throw;
                }
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                _logger.LogWarning(ex, "批量更新状态遇到套装成本业务锁冲突");
                return ApiResponse<bool>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新状态失败");
                return ApiResponse<bool>.Error($"批量更新状态失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdatePricesAsync(
            List<BatchUpdatePricesItemDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var updatedCount = 0;
                var updatedMultiCodeCount = 0;
                var affectedStoreGroups = new HashSet<(string StoreCode, string ProductCode)>();

                if (!TryNormalizeBatchPriceUpdates(items, out var priceUpdates, out var validationError))
                {
                    return ApiResponse<bool>.Error(validationError);
                }

                var ids = priceUpdates.Keys.ToList();
                var list = await db.Queryable<ProductSetCode>()
                    .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                    .ToListAsync();

                await db.Ado.BeginTranAsync();
                try
                {
                    var snapshots = CaptureSetCodeLockSnapshots(list);
                    var lockProductCodes = GetSetCodeLockProductCodes(list);
                    SetChildPurchasePriceLockScope? lockScope = null;
                    if (lockProductCodes.Count > 0)
                    {
                        lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            lockProductCodes
                        );
                    }

                    // 业务锁获取后必须重读，避免把锁前快照覆盖到并发更新后的套装组。
                    list = await db.Queryable<ProductSetCode>()
                        .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    EnsureSetCodeLockSnapshotsUnchanged(snapshots, list);
                    lockProductCodes = GetSetCodeLockProductCodes(list);
                    var affectedSetProductCodes = GetAffectedSetProductCodes(list);
                    if (lockProductCodes.Count > 0)
                    {
                        if (lockScope == null)
                        {
                            throw new InvalidOperationException("套装关系在更新期间发生变化，请重试");
                        }
                        lockScope.EnsureCovers(db, lockProductCodes);
                    }
                    var changedActiveType1Rows = new List<ProductSetCode>();
                    foreach (var row in list)
                    {
                        var upd = priceUpdates[row.SetCodeId];
                        if (
                            row.IsActive
                            && row.SetType == 1
                            && upd.SetRetailPrice.HasValue
                            && upd.SetRetailPrice != row.SetRetailPrice
                            && !string.IsNullOrWhiteSpace(row.ProductCode)
                            && !string.IsNullOrWhiteSpace(row.SetProductCode)
                        )
                        {
                            changedActiveType1Rows.Add(row);
                        }
                        // 两类套装成本均为派生值，客户端只能提交零售价。
                        row.SetRetailPrice = upd.SetRetailPrice ?? row.SetRetailPrice;
                        row.UpdatedAt = now;
                        row.UpdatedBy = updatedBy;
                    }

                    var count = 0;
                    foreach (var row in list)
                    {
                        var updatedRows = await db.Updateable<ProductSetCode>()
                            .SetColumns(x => x.SetRetailPrice == row.SetRetailPrice)
                            .SetColumns(x => x.UpdatedAt == row.UpdatedAt)
                            .SetColumns(x => x.UpdatedBy == row.UpdatedBy)
                            .Where(x =>
                                x.SetCodeId == row.SetCodeId
                                && x.ProductCode == row.ProductCode
                                && x.SetProductCode == row.SetProductCode
                                && x.SetType == row.SetType
                                && !x.IsDeleted
                            )
                            .ExecuteCommandAsync();
                        if (updatedRows != 1)
                        {
                            throw new InvalidOperationException(
                                $"套装条码 {row.SetCodeId} 在更新期间发生变化，请重试"
                            );
                        }
                        count += updatedRows;
                    }
                    _logger.LogInformation($"更新套装条码价格: {count} 条");
                    updatedCount = count;

                    // 如果提供了分店列表，同步更新 StoreMultiCodeProduct 的价格
                    if (list.Any())
                    {
                        foreach (var row in list.Where(x =>
                            !string.IsNullOrWhiteSpace(x.ProductCode)
                            && !string.IsNullOrWhiteSpace(x.SetProductCode)
                            && priceUpdates[x.SetCodeId].StoreCodes.Count > 0
                        ))
                        {
                            var requestedStoreCodes = priceUpdates[row.SetCodeId].StoreCodes;
                            var multiCodeList = await db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.ProductCode == row.ProductCode
                                    && m.MultiCodeProductCode == row.SetProductCode
                                    && m.StoreCode != null
                                    && requestedStoreCodes.Contains(m.StoreCode)
                                    && !m.IsDeleted
                                )
                                .ToListAsync();
                            foreach (var multiCode in multiCodeList)
                            {
                                var updatedRows = await db.Updateable<StoreMultiCodeProduct>()
                                    .SetColumns(x => x.MultiCodeRetailPrice == row.SetRetailPrice)
                                    .SetColumns(x => x.UpdatedAt == now)
                                    .SetColumns(x => x.UpdatedBy == updatedBy)
                                    .Where(x =>
                                        x.UUID == multiCode.UUID
                                        && x.ProductCode == row.ProductCode
                                        && x.MultiCodeProductCode == row.SetProductCode
                                        && x.StoreCode == multiCode.StoreCode
                                        && !x.IsDeleted
                                    )
                                    .ExecuteCommandAsync();
                                if (updatedRows != 1)
                                {
                                    throw new InvalidOperationException(
                                        $"门店条码 {multiCode.UUID} 在更新期间发生变化，请重试"
                                    );
                                }
                                updatedMultiCodeCount += updatedRows;
                                affectedStoreGroups.Add((multiCode.StoreCode!, row.ProductCode!));
                            }
                        }
                        _logger.LogInformation($"同步更新分店一品多码价格: {updatedMultiCodeCount} 条");
                    }

                    if (changedActiveType1Rows.Count > 0)
                    {
                        var changedParentCodes = changedActiveType1Rows
                            .Select(x => x.ProductCode!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var changedChildCodes = changedActiveType1Rows
                            .Select(x => x.SetProductCode!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var changedProjectionKeys = GetStoreMultiCodeProjectionKeys(
                            changedActiveType1Rows
                        );
                        var fallbackRows = await db.Queryable<StoreMultiCodeProduct>()
                            .Where(x =>
                                x.StoreCode != null
                                && x.ProductCode != null
                                && x.MultiCodeProductCode != null
                                && changedParentCodes.Contains(x.ProductCode)
                                && changedChildCodes.Contains(x.MultiCodeProductCode)
                                && x.IsActive
                                && !x.IsDeleted
                                && (x.MultiCodeRetailPrice == null || x.MultiCodeRetailPrice <= 0)
                            )
                            .ToListAsync();

                        // 仅回退总部售价的门店组会受本次全局售价变化影响；正数自定义价保持门店自治。
                        foreach (var fallbackRow in fallbackRows)
                        {
                            var projectionKey = GetStoreMultiCodeProjectionKey(
                                fallbackRow.ProductCode,
                                fallbackRow.MultiCodeProductCode
                            );
                            if (
                                projectionKey != null
                                && changedProjectionKeys.Contains(projectionKey)
                            )
                            {
                                affectedStoreGroups.Add((
                                    fallbackRow.StoreCode!,
                                    fallbackRow.ProductCode!
                                ));
                            }
                        }
                    }

                    if (affectedSetProductCodes.Count > 0)
                    {
                        var purchasePriceService = new SetChildPurchasePriceService(db);
                        var globalRecalculateResult = await purchasePriceService
                            .RecalculateGlobalLockedAsync(
                                lockScope!,
                                affectedSetProductCodes,
                                updatedBy
                            );
                        EnsureNoSkippedSetGroups(globalRecalculateResult);

                        if (affectedStoreGroups.Count > 0)
                        {
                            var storeRecalculateResult = await purchasePriceService
                                .RecalculateStoreGroupsLockedAsync(
                                    lockScope!,
                                    affectedStoreGroups.Select(x =>
                                        ((string?)x.StoreCode, (string?)x.ProductCode)
                                    ),
                                    updatedBy
                                );
                            EnsureNoSkippedSetGroups(storeRecalculateResult);
                        }
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    var message = $"已更新 {updatedCount} 条价格";
                    if (updatedMultiCodeCount > 0)
                    {
                        message += $"，已同步到 {updatedMultiCodeCount} 条分店一品多码";
                    }

                    return ApiResponse<bool>.OK(true, message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量更新价格事务失败, 事务已回滚");
                    throw;
                }
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                _logger.LogWarning(ex, "批量更新价格遇到套装成本业务锁冲突");
                return ApiResponse<bool>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新价格失败");
                return ApiResponse<bool>.Error($"批量更新价格失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> BatchDeleteAsync(List<string> ids, string updatedBy)
        {
            try
            {
                var db = _context.Db;
                var deletedCount = 0;
                var deletedMultiCode = 0;

                _logger.LogInformation(
                    $"BatchDeleteAsync 开始, 操作人: {updatedBy}, 套装条码ID数量: {ids.Count}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var distinctIds = ids.Distinct(StringComparer.Ordinal).ToList();
                    var toDeleteSetCodes = await db.Queryable<ProductSetCode>()
                        .Where(x => distinctIds.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    var snapshots = CaptureSetCodeLockSnapshots(toDeleteSetCodes);
                    var lockProductCodes = GetSetCodeLockProductCodes(toDeleteSetCodes);
                    var affectedSetProductCodes = GetAffectedSetProductCodes(toDeleteSetCodes);
                    SetChildPurchasePriceLockScope? lockScope = null;
                    if (lockProductCodes.Count > 0)
                    {
                        lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            lockProductCodes
                        );
                    }
                    toDeleteSetCodes = await db.Queryable<ProductSetCode>()
                        .Where(x => distinctIds.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    EnsureSetCodeLockSnapshotsUnchanged(snapshots, toDeleteSetCodes);
                    lockProductCodes = GetSetCodeLockProductCodes(toDeleteSetCodes);
                    affectedSetProductCodes = GetAffectedSetProductCodes(toDeleteSetCodes);
                    if (lockProductCodes.Count > 0)
                    {
                        if (lockScope == null)
                        {
                            throw new InvalidOperationException("套装关系在删除期间发生变化，请重试");
                        }
                        lockScope.EnsureCovers(db, lockProductCodes);
                    }

                    _logger.LogInformation($"查询到待删除套装条码: {toDeleteSetCodes.Count} 条");

                    if (toDeleteSetCodes.Any())
                    {
                        foreach (var row in toDeleteSetCodes.Where(x =>
                            !string.IsNullOrWhiteSpace(x.ProductCode)
                            && !string.IsNullOrWhiteSpace(x.SetProductCode)
                        ))
                        {
                            var multiCodes = await db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.ProductCode == row.ProductCode
                                    && m.MultiCodeProductCode == row.SetProductCode
                                    && !m.IsDeleted
                                )
                                .ToListAsync();
                            foreach (var multiCode in multiCodes)
                            {
                                var deletedRows = await db.Deleteable<StoreMultiCodeProduct>()
                                    .Where(m =>
                                        m.UUID == multiCode.UUID
                                        && m.ProductCode == row.ProductCode
                                        && m.MultiCodeProductCode == row.SetProductCode
                                        && m.StoreCode == multiCode.StoreCode
                                        && !m.IsDeleted
                                    )
                                    .ExecuteCommandAsync();
                                if (deletedRows != 1)
                                {
                                    throw new InvalidOperationException(
                                        $"门店条码 {multiCode.UUID} 在删除期间发生变化，请重试"
                                    );
                                }
                                deletedMultiCode += deletedRows;
                            }
                        }
                        _logger.LogInformation($"物理删除分店一品多码: {deletedMultiCode} 条");

                        foreach (var row in toDeleteSetCodes)
                        {
                            var deletedRows = await db.Deleteable<ProductSetCode>()
                                .Where(x =>
                                    x.SetCodeId == row.SetCodeId
                                    && x.ProductCode == row.ProductCode
                                    && x.SetProductCode == row.SetProductCode
                                    && x.SetType == row.SetType
                                    && !x.IsDeleted
                                )
                                .ExecuteCommandAsync();
                            if (deletedRows != 1)
                            {
                                throw new InvalidOperationException(
                                    $"套装条码 {row.SetCodeId} 在删除期间发生变化，请重试"
                                );
                            }
                            deletedCount += deletedRows;
                        }

                        _logger.LogInformation($"物理删除套装条码: {deletedCount} 条");

                        if (affectedSetProductCodes.Count > 0)
                        {
                            var recalculateResult = await new SetChildPurchasePriceService(db)
                                .RecalculateLockedAsync(
                                    lockScope!,
                                    affectedSetProductCodes,
                                    storeCodes: null,
                                    updatedBy: updatedBy
                                );
                            EnsureNoSkippedSetGroups(recalculateResult);
                        }
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    return ApiResponse<bool>.OK(
                        true,
                        $"成功删除 {deletedCount} 条套装条码和 {deletedMultiCode} 条分店一品多码"
                    );
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量删除事务失败, 事务已回滚");
                    throw;
                }
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                _logger.LogWarning(ex, "批量删除套装关系遇到套装成本业务锁冲突");
                return ApiResponse<bool>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除失败");
                return ApiResponse<bool>.Error($"批量删除失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateBarcodesAsync(
            List<BatchUpdateBarcodesItemDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var ids = items.Select(x => x.Id).ToList();

                await db.Ado.BeginTranAsync();
                try
                {
                    var list = await db.Queryable<ProductSetCode>()
                        .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    var snapshots = CaptureSetCodeLockSnapshots(list);
                    var lockProductCodes = GetSetCodeLockProductCodes(list);
                    var affectedSetProductCodes = GetAffectedSetProductCodes(list);
                    SetChildPurchasePriceLockScope? lockScope = null;
                    if (lockProductCodes.Count > 0)
                    {
                        lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            lockProductCodes
                        );
                    }
                    list = await db.Queryable<ProductSetCode>()
                        .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    EnsureSetCodeLockSnapshotsUnchanged(snapshots, list);
                    lockProductCodes = GetSetCodeLockProductCodes(list);
                    affectedSetProductCodes = GetAffectedSetProductCodes(list);
                    if (lockProductCodes.Count > 0)
                    {
                        if (lockScope == null)
                        {
                            throw new InvalidOperationException("套装关系在更新期间发生变化，请重试");
                        }
                        lockScope.EnsureCovers(db, lockProductCodes);
                    }
                    var count = 0;
                    foreach (var row in list)
                    {
                        var upd = items.First(x => x.Id == row.SetCodeId);
                        var updatedRows = await db.Updateable<ProductSetCode>()
                            .SetColumns(x => x.SetBarcode == upd.SetBarcode)
                            .SetColumns(x => x.UpdatedAt == now)
                            .SetColumns(x => x.UpdatedBy == updatedBy)
                            .Where(x =>
                                x.SetCodeId == row.SetCodeId
                                && x.ProductCode == row.ProductCode
                                && x.SetProductCode == row.SetProductCode
                                && x.SetType == row.SetType
                                && !x.IsDeleted
                            )
                            .ExecuteCommandAsync();
                        if (updatedRows != 1)
                        {
                            throw new InvalidOperationException(
                                $"套装条码 {row.SetCodeId} 在更新期间发生变化，请重试"
                            );
                        }
                        row.SetBarcode = upd.SetBarcode;
                        count += updatedRows;
                    }

                    _logger.LogInformation($"更新套装条码: {count} 条");

                    // 同步更新所有分店的 StoreMultiCodeProduct
                    if (list.Any())
                    {
                        var projectionKeys = GetStoreMultiCodeProjectionKeys(list);
                        var setProductCodes = list
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .Select(x => x.SetProductCode!)
                            .Distinct()
                            .ToList();

                        var barcodeUpdates = list
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x.ProductCode)
                                && !string.IsNullOrWhiteSpace(x.SetProductCode)
                            )
                            .ToDictionary(
                                x => GetStoreMultiCodeProjectionKey(x.ProductCode, x.SetProductCode)!,
                                x => x.SetBarcode
                            );

                        if (projectionKeys.Count > 0 && setProductCodes.Any() && barcodeUpdates.Any())
                        {
                            var multiCodeList = await db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.MultiCodeProductCode != null
                                    && setProductCodes.Contains(m.MultiCodeProductCode)
                                    && !m.IsDeleted
                                )
                                .ToListAsync();
                            multiCodeList = multiCodeList
                                .Where(m =>
                                    GetStoreMultiCodeProjectionKey(
                                        m.ProductCode,
                                        m.MultiCodeProductCode
                                    ) is { } key && projectionKeys.Contains(key)
                                )
                                .ToList();

                            foreach (var multiCode in multiCodeList)
                            {
                                if (
                                    GetStoreMultiCodeProjectionKey(
                                        multiCode.ProductCode,
                                        multiCode.MultiCodeProductCode
                                    ) is { } key
                                    && barcodeUpdates.TryGetValue(key, out var barcode)
                                )
                                {
                                    var updatedRows = await db.Updateable<StoreMultiCodeProduct>()
                                        .SetColumns(x => x.MultiBarcode == barcode)
                                        .SetColumns(x => x.UpdatedAt == now)
                                        .SetColumns(x => x.UpdatedBy == updatedBy)
                                        .Where(x =>
                                            x.UUID == multiCode.UUID
                                            && x.ProductCode == multiCode.ProductCode
                                            && x.MultiCodeProductCode == multiCode.MultiCodeProductCode
                                            && x.StoreCode == multiCode.StoreCode
                                            && !x.IsDeleted
                                        )
                                        .ExecuteCommandAsync();
                                    if (updatedRows != 1)
                                    {
                                        throw new InvalidOperationException(
                                            $"门店条码 {multiCode.UUID} 在更新期间发生变化，请重试"
                                        );
                                    }
                                }
                            }
                            _logger.LogInformation($"同步更新分店一品多码条码: {multiCodeList.Count} 条");
                        }
                    }

                    if (affectedSetProductCodes.Count > 0)
                    {
                        var recalculateResult = await new SetChildPurchasePriceService(db)
                            .RecalculateLockedAsync(
                                lockScope!,
                                affectedSetProductCodes,
                                storeCodes: null,
                                updatedBy: updatedBy
                            );
                        EnsureNoSkippedSetGroups(recalculateResult);
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    return ApiResponse<bool>.OK(true, $"已更新 {count} 条条码");
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量更新条码事务失败, 事务已回滚");
                    throw;
                }
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                _logger.LogWarning(ex, "批量更新条码遇到套装成本业务锁冲突");
                return ApiResponse<bool>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新条码失败");
                return ApiResponse<bool>.Error($"批量更新条码失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<List<string>>> BatchCreateAsync(
            List<CreateSetCodeItemDto> items,
            string updatedBy
        )
        {
            var db = _context.Db;
            try
            {
                if (items.Count == 0)
                {
                    return ApiResponse<List<string>>.OK(new List<string>(), "无可创建的记录");
                }

                var now = DateTime.UtcNow;
                var productCodes = items
                    .Select(i => i.ProductCode?.Trim())
                    .Where(pc => !string.IsNullOrWhiteSpace(pc))
                    .Select(pc => pc!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (productCodes.Count == 0 || items.Any(i => string.IsNullOrWhiteSpace(i.ProductCode)))
                {
                    return ApiResponse<List<string>>.Error("主商品编码不能为空");
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                        db,
                        productCodes
                    );
                    // Product、父商品 ItemNumber 和兄弟编号必须全部在父商品锁内重读。
                    var products = await db.Queryable<Product>()
                        .Where(p =>
                            p.ProductCode != null
                            && productCodes.Contains(p.ProductCode)
                            && !p.IsDeleted
                        )
                        .ToListAsync();
                    var productMap = products.ToDictionary(
                        p => p.ProductCode!,
                        p => p,
                        StringComparer.OrdinalIgnoreCase
                    );
                    var missingProducts = productCodes
                        .Where(code => !productMap.ContainsKey(code))
                        .ToList();
                    if (missingProducts.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"商品不存在: {string.Join(", ", missingProducts)}"
                        );
                    }

                    var existingRelations = await db.Queryable<ProductSetCode>()
                        .Where(x => productCodes.Contains(x.ProductCode) && !x.IsDeleted)
                        .ToListAsync();
                    var usedSetNosByProduct = productCodes.ToDictionary(
                        code => code,
                        code => existingRelations
                            .Where(x => string.Equals(x.ProductCode, code, StringComparison.OrdinalIgnoreCase))
                            .Select(x => x.SetItemNumber)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase
                    );
                    var usedBarcodes = existingRelations
                        .Where(x => !string.IsNullOrWhiteSpace(x.SetBarcode))
                        .Select(x => x.SetBarcode!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var newRows = new List<ProductSetCode>();
                    foreach (var item in items)
                    {
                        var productCode = item.ProductCode.Trim();
                        var product = productMap[productCode];
                        var usedSetNos = usedSetNosByProduct[productCode];
                        var setItemNumber = string.IsNullOrWhiteSpace(item.SetItemNumber)
                            ? ItemNumberHelper.GenerateSetItemNumber(
                                product.ItemNumber ?? string.Empty,
                                usedSetNos
                            )
                            : item.SetItemNumber.Trim();
                        if (usedSetNos.Contains(setItemNumber, StringComparer.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"多码货号重复: {setItemNumber}");
                        }
                        usedSetNos.Add(setItemNumber);

                        var barcode = item.SetBarcode?.Trim();
                        if (!string.IsNullOrWhiteSpace(barcode) && !usedBarcodes.Add(barcode))
                        {
                            throw new InvalidOperationException($"多码条码重复: {barcode}");
                        }

                        newRows.Add(new ProductSetCode
                        {
                            SetCodeId = UuidHelper.GenerateUuid7(),
                            ProductCode = productCode,
                            SetProductCode = UuidHelper.GenerateUuid7(),
                            SetItemNumber = setItemNumber,
                            SetBarcode = barcode,
                            SetPurchasePrice = null,
                            SetRetailPrice = item.SetRetailPrice,
                            SetQuantity = 1,
                            SetType = 2,
                            IsActive = item.IsActive ?? true,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = updatedBy,
                            UpdatedBy = updatedBy,
                            IsDeleted = false,
                        });
                    }

                    var count = await db.Insertable(newRows).ExecuteCommandAsync();
                    var activeStoreCodes = await db.Queryable<Store>()
                        .Where(s => s.IsActive && !s.IsDeleted && s.StoreCode != null)
                        .Select(s => s.StoreCode)
                        .ToListAsync();
                    var multiCodeList = new List<StoreMultiCodeProduct>();
                    foreach (var row in newRows)
                    {
                        var mainProduct = productMap[row.ProductCode];
                        foreach (var storeCode in activeStoreCodes.Where(x => !string.IsNullOrWhiteSpace(x)))
                        {
                            multiCodeList.Add(new StoreMultiCodeProduct
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = storeCode!,
                                ProductCode = row.ProductCode,
                                MultiCodeProductCode = row.SetProductCode,
                                StoreMultiCodeProductCode = storeCode + row.SetProductCode,
                                MultiBarcode = row.SetBarcode,
                                PurchasePrice = null,
                                MultiCodeRetailPrice = row.SetRetailPrice,
                                DiscountRate = null,
                                IsAutoPricing = false,
                                IsSpecialProduct = mainProduct.IsSpecialProduct,
                                IsActive = row.IsActive,
                                CreatedAt = now,
                                UpdatedAt = now,
                                CreatedBy = updatedBy,
                                UpdatedBy = updatedBy,
                                IsDeleted = false,
                            });
                        }
                    }

                    if (multiCodeList.Count > 0)
                    {
                        await db.Insertable(multiCodeList).ExecuteCommandAsync();
                    }

                    var purchasePriceService = new SetChildPurchasePriceService(db);
                    var globalRecalculation = await purchasePriceService
                        .RecalculateGlobalLockedAsync(lockScope, productCodes, updatedBy);
                    EnsureNoSkippedSetGroups(globalRecalculation);
                    if (multiCodeList.Count > 0)
                    {
                        var storeRecalculation = await purchasePriceService
                            .RecalculateStoreGroupsLockedAsync(
                                lockScope,
                                multiCodeList.Select(row =>
                                    ((string?)row.StoreCode, (string?)row.ProductCode)
                                ),
                                updatedBy
                            );
                        EnsureNoSkippedSetGroups(storeRecalculation);
                    }
                    await db.Ado.CommitTranAsync();
                    return ApiResponse<List<string>>.OK(
                        newRows.Select(row => row.SetCodeId).ToList(),
                        $"已创建 {count} 条记录"
                        + (activeStoreCodes.Count > 0 ? $"，已同步至 {activeStoreCodes.Count} 个分店" : "")
                    );
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                return ApiResponse<List<string>>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                return ApiResponse<List<string>>.Error($"批量创建失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<BatchResultDto>> BatchCreateWithStoreSyncAsync(
            List<CreateSetCodeWithStoreSyncDto> items,
            string updatedBy
        )
        {
            var db = _context.Db;
            try
            {
                if (items.Count == 0)
                {
                    return ApiResponse<BatchResultDto>.OK(
                        new BatchResultDto(),
                        "无可创建的记录"
                    );
                }

                var now = DateTime.UtcNow;
                var productCodes = items
                    .Select(item => item.ProductCode?.Trim())
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (productCodes.Count == 0 || items.Any(item => string.IsNullOrWhiteSpace(item.ProductCode)))
                {
                    return ApiResponse<BatchResultDto>.Error("主商品编码不能为空");
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                        db,
                        productCodes
                    );
                    var products = await db.Queryable<Product>()
                        .Where(p =>
                            p.ProductCode != null
                            && productCodes.Contains(p.ProductCode)
                            && !p.IsDeleted
                        )
                        .ToListAsync();
                    var productMap = products.ToDictionary(
                        p => p.ProductCode!,
                        p => p,
                        StringComparer.OrdinalIgnoreCase
                    );
                    var missingProducts = productCodes
                        .Where(code => !productMap.ContainsKey(code))
                        .ToList();
                    if (missingProducts.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"商品不存在: {string.Join(", ", missingProducts)}"
                        );
                    }

                    var requestedStoreCodes = items
                        .SelectMany(item => item.StoreCodes ?? new List<string>())
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var activeStoreCodes = requestedStoreCodes.Count == 0
                        ? new List<string>()
                        : await db.Queryable<Store>()
                            .Where(store =>
                                store.StoreCode != null
                                && requestedStoreCodes.Contains(store.StoreCode)
                                && store.IsActive
                                && !store.IsDeleted
                            )
                            .Select(store => store.StoreCode!)
                            .ToListAsync();
                    var activeStoreSet = activeStoreCodes.ToHashSet(
                        StringComparer.OrdinalIgnoreCase
                    );
                    var missingStores = requestedStoreCodes
                        .Where(code => !activeStoreSet.Contains(code))
                        .ToList();
                    if (missingStores.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"分店不存在、停用或已删除: {string.Join(", ", missingStores)}"
                        );
                    }

                    var existingRelations = await db.Queryable<ProductSetCode>()
                        .Where(x => productCodes.Contains(x.ProductCode) && !x.IsDeleted)
                        .ToListAsync();
                    var usedSetNosByProduct = productCodes.ToDictionary(
                        code => code,
                        code => existingRelations
                            .Where(x => string.Equals(x.ProductCode, code, StringComparison.OrdinalIgnoreCase))
                            .Select(x => x.SetItemNumber)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!)
                            .ToList(),
                        StringComparer.OrdinalIgnoreCase
                    );
                    var usedBarcodes = existingRelations
                        .Where(x => !string.IsNullOrWhiteSpace(x.SetBarcode))
                        .Select(x => x.SetBarcode!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var newRows = new List<ProductSetCode>();
                    var targetStoresBySetCodeId = new Dictionary<string, List<string>>(
                        StringComparer.Ordinal
                    );
                    foreach (var item in items)
                    {
                        var productCode = item.ProductCode.Trim();
                        var product = productMap[productCode];
                        var usedSetNos = usedSetNosByProduct[productCode];
                        var setItemNumber = string.IsNullOrWhiteSpace(item.SetItemNumber)
                            ? ItemNumberHelper.GenerateSetItemNumber(
                                product.ItemNumber ?? string.Empty,
                                usedSetNos
                            )
                            : item.SetItemNumber.Trim();
                        if (usedSetNos.Contains(setItemNumber, StringComparer.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException($"多码货号重复: {setItemNumber}");
                        }
                        usedSetNos.Add(setItemNumber);

                        var barcode = item.SetBarcode?.Trim();
                        if (!string.IsNullOrWhiteSpace(barcode) && !usedBarcodes.Add(barcode))
                        {
                            throw new InvalidOperationException($"多码条码重复: {barcode}");
                        }

                        var row = new ProductSetCode
                        {
                            SetCodeId = UuidHelper.GenerateUuid7(),
                            ProductCode = productCode,
                            SetProductCode = UuidHelper.GenerateUuid7(),
                            SetItemNumber = setItemNumber,
                            SetBarcode = barcode,
                            SetPurchasePrice = null,
                            SetRetailPrice = item.SetRetailPrice,
                            SetQuantity = 1,
                            SetType = 2,
                            IsActive = item.IsActive ?? true,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = updatedBy,
                            UpdatedBy = updatedBy,
                            IsDeleted = false,
                        };
                        newRows.Add(row);
                        targetStoresBySetCodeId[row.SetCodeId] = (item.StoreCodes ?? new List<string>())
                            .Where(code => !string.IsNullOrWhiteSpace(code))
                            .Select(code => code.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    var insertedSetCodes = await db.Insertable(newRows).ExecuteCommandAsync();
                    var multiCodeList = new List<StoreMultiCodeProduct>();
                    foreach (var row in newRows)
                    {
                        var mainProduct = productMap[row.ProductCode];
                        foreach (var storeCode in targetStoresBySetCodeId[row.SetCodeId])
                        {
                            multiCodeList.Add(new StoreMultiCodeProduct
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = storeCode,
                                ProductCode = row.ProductCode,
                                MultiCodeProductCode = row.SetProductCode,
                                StoreMultiCodeProductCode = storeCode + row.SetProductCode,
                                MultiBarcode = row.SetBarcode,
                                PurchasePrice = null,
                                MultiCodeRetailPrice = row.SetRetailPrice,
                                DiscountRate = null,
                                IsAutoPricing = false,
                                IsSpecialProduct = mainProduct.IsSpecialProduct,
                                IsActive = row.IsActive,
                                CreatedAt = now,
                                UpdatedAt = now,
                                CreatedBy = updatedBy,
                                UpdatedBy = updatedBy,
                                IsDeleted = false,
                            });
                        }
                    }

                    if (multiCodeList.Count > 0)
                    {
                        await db.Insertable(multiCodeList).ExecuteCommandAsync();
                    }

                    var purchasePriceService = new SetChildPurchasePriceService(db);
                    var globalRecalculation = await purchasePriceService
                        .RecalculateGlobalLockedAsync(lockScope, productCodes, updatedBy);
                    EnsureNoSkippedSetGroups(globalRecalculation);
                    if (multiCodeList.Count > 0)
                    {
                        var storeRecalculation = await purchasePriceService
                            .RecalculateStoreGroupsLockedAsync(
                                lockScope,
                                multiCodeList.Select(row =>
                                    ((string?)row.StoreCode, (string?)row.ProductCode)
                                ),
                                updatedBy
                            );
                        EnsureNoSkippedSetGroups(storeRecalculation);
                    }
                    await db.Ado.CommitTranAsync();
                    var result = new BatchResultDto
                    {
                        Inserted = insertedSetCodes,
                        Updated = multiCodeList.Count,
                        Failed = 0,
                        Errors = new List<string>(),
                    };
                    return ApiResponse<BatchResultDto>.OK(
                        result,
                        $"成功创建 {insertedSetCodes} 条多码关系, 同步 {multiCodeList.Count} 条到分店"
                    );
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _)
            )
            {
                return ApiResponse<BatchResultDto>.Error(
                    "套装商品正在被其他操作修改，请稍后重试",
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建并同步失败");
                return ApiResponse<BatchResultDto>.Error(
                    $"批量创建并同步失败: {ex.Message}",
                    "BATCH_CREATE_SYNC_ERROR"
                );
            }
        }

        /// <summary>
        /// 删除条码并同步删除分店一品多码表（StoreMultiCodeProduct），全部物理删除。
        /// </summary>
        public async Task<ApiResponse<BatchResultDto>> BatchDeleteWithStoreSyncAsync(
            List<string> ids,
            List<string> storeCodes,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var errors = new List<string>();
                var deletedSetCodes = 0;
                var deletedMultiCode = 0;

                _logger.LogInformation(
                    $"BatchDeleteWithStoreSyncAsync 开始, 操作人: {updatedBy}, 套装条码ID数量: {ids.Count}, 分店数量: {storeCodes?.Count ?? 0}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var toDeleteSetCodes = await db.Queryable<ProductSetCode>()
                        .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();
                    var snapshots = CaptureSetCodeLockSnapshots(toDeleteSetCodes);
                    var affectedSetProductCodes = GetAffectedSetProductCodes(toDeleteSetCodes);
                    var lockProductCodes = GetSetCodeLockProductCodes(toDeleteSetCodes);
                    SetChildPurchasePriceLockScope? lockScope = null;
                    if (lockProductCodes.Count > 0)
                    {
                        lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                            db,
                            lockProductCodes
                        );
                        toDeleteSetCodes = await db.Queryable<ProductSetCode>()
                            .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                            .ToListAsync();
                        EnsureSetCodeLockSnapshotsUnchanged(snapshots, toDeleteSetCodes);
                        affectedSetProductCodes = GetAffectedSetProductCodes(toDeleteSetCodes);
                        lockProductCodes = GetSetCodeLockProductCodes(toDeleteSetCodes);
                        lockScope.EnsureCovers(db, lockProductCodes);
                    }

                    _logger.LogInformation($"查询到待删除套装条码: {toDeleteSetCodes.Count} 条");

                    if (toDeleteSetCodes.Any())
                    {
                        var projectionKeys = GetStoreMultiCodeProjectionKeys(toDeleteSetCodes);
                        var setProductCodes = toDeleteSetCodes
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .Select(x => x.SetProductCode!)
                            .Distinct()
                            .ToList();

                        if (projectionKeys.Count > 0 && setProductCodes.Any())
                        {
                            var multiCodeQuery = db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.MultiCodeProductCode != null
                                    && setProductCodes.Contains(m.MultiCodeProductCode)
                                    && !m.IsDeleted);
                            if (storeCodes != null && storeCodes.Count > 0)
                            {
                                multiCodeQuery = multiCodeQuery.Where(m =>
                                    m.StoreCode != null && storeCodes.Contains(m.StoreCode)
                                );
                            }

                            var multiCodeIds = (await multiCodeQuery.ToListAsync())
                                .Where(m =>
                                    GetStoreMultiCodeProjectionKey(
                                        m.ProductCode,
                                        m.MultiCodeProductCode
                                    ) is { } key && projectionKeys.Contains(key)
                                )
                                .Select(m => m.UUID)
                                .ToList();
                            if (multiCodeIds.Count > 0)
                            {
                                deletedMultiCode = await db.Deleteable<StoreMultiCodeProduct>()
                                    .Where(m => multiCodeIds.Contains(m.UUID))
                                    .ExecuteCommandAsync();
                            }

                            _logger.LogInformation($"物理删除分店一品多码: {deletedMultiCode} 条");
                        }

                        deletedSetCodes = await db.Deleteable<ProductSetCode>()
                            .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                            .ExecuteCommandAsync();
                        _logger.LogInformation($"物理删除套装条码: {deletedSetCodes} 条");

                        if (affectedSetProductCodes.Count > 0)
                        {
                            var recalculateResult = await new SetChildPurchasePriceService(db)
                                .RecalculateLockedAsync(
                                    lockScope!,
                                    affectedSetProductCodes,
                                    storeCodes: null,
                                    updatedBy: updatedBy
                                );
                            EnsureNoSkippedSetGroups(recalculateResult);
                        }
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    var result = new BatchResultDto
                    {
                        Inserted = 0,
                        Updated = 0,
                        Failed = errors.Count,
                        Errors = errors,
                    };

                    return ApiResponse<BatchResultDto>.OK(
                        result,
                        $"成功删除 {deletedSetCodes} 条套装条码和 {deletedMultiCode} 条分店一品多码"
                    );
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量删除并同步事务失败, 事务已回滚");
                    if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
                    {
                        return ApiResponse<BatchResultDto>.Error(
                            "套装商品正在被其他操作修改，请稍后重试",
                            SetChildPurchasePriceMutationLock.BusyErrorCode
                        );
                    }
                    return ApiResponse<BatchResultDto>.Error(
                        $"批量删除并同步失败: {ex.Message}",
                        "BATCH_DELETE_SYNC_ERROR"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除并同步失败");
                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
                {
                    return ApiResponse<BatchResultDto>.Error(
                        "套装商品正在被其他操作修改，请稍后重试",
                        SetChildPurchasePriceMutationLock.BusyErrorCode
                    );
                }
                return ApiResponse<BatchResultDto>.Error(
                    $"批量删除并同步失败: {ex.Message}",
                    "BATCH_DELETE_SYNC_ERROR"
                );
            }
        }

        private static List<string> GetAffectedSetProductCodes(
            IEnumerable<ProductSetCode> rows
        ) => rows
            .Where(x =>
                (x.SetType == 1 || x.SetType == 2)
                && !string.IsNullOrWhiteSpace(x.ProductCode)
            )
            .Select(x => x.ProductCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        /// <summary>
        /// 两类子码都可能并发修改父子关系，并且都必须在锁内校正成本。
        /// </summary>
        private static List<string> GetSetCodeLockProductCodes(
            IEnumerable<ProductSetCode> rows
        ) => rows
            .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
            .Select(x => x.ProductCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private sealed record SetCodeLockSnapshot(
            string SetCodeId,
            string? ProductCode,
            string? SetProductCode,
            int SetType
        );

        private sealed class NormalizedBatchPriceUpdate
        {
            public decimal? SetRetailPrice { get; init; }
            public HashSet<string> StoreCodes { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>
        /// 业务锁只按父商品获取，因此锁前快照必须包含套装关系全部身份，防止并发改父子关系后误写。
        /// </summary>
        private static List<SetCodeLockSnapshot> CaptureSetCodeLockSnapshots(
            IEnumerable<ProductSetCode> rows
        ) => rows
            .Select(x => new SetCodeLockSnapshot(
                x.SetCodeId,
                x.ProductCode,
                x.SetProductCode,
                x.SetType
            ))
            .ToList();

        private static void EnsureSetCodeLockSnapshotsUnchanged(
            IReadOnlyCollection<SetCodeLockSnapshot> snapshots,
            IEnumerable<ProductSetCode> lockedRows
        )
        {
            var lockedById = lockedRows.ToDictionary(x => x.SetCodeId, StringComparer.Ordinal);
            if (lockedById.Count != snapshots.Count)
            {
                throw new InvalidOperationException("套装关系在等待业务锁期间发生变化，请重试");
            }

            foreach (var snapshot in snapshots)
            {
                if (
                    !lockedById.TryGetValue(snapshot.SetCodeId, out var locked)
                    || !string.Equals(locked.ProductCode, snapshot.ProductCode, StringComparison.Ordinal)
                    || !string.Equals(
                        locked.SetProductCode,
                        snapshot.SetProductCode,
                        StringComparison.Ordinal
                    )
                    || locked.SetType != snapshot.SetType
                )
                {
                    throw new InvalidOperationException("套装关系在等待业务锁期间发生变化，请重试");
                }
            }
        }

        private static bool TryNormalizeBatchPriceUpdates(
            IEnumerable<BatchUpdatePricesItemDto> items,
            out Dictionary<string, NormalizedBatchPriceUpdate> normalized,
            out string error
        )
        {
            normalized = new Dictionary<string, NormalizedBatchPriceUpdate>(StringComparer.Ordinal);
            error = string.Empty;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    error = "套装条码ID不能为空";
                    return false;
                }
                if (item.SetRetailPrice.HasValue && item.SetRetailPrice.Value < 0)
                {
                    error = "零售价不能为负数";
                    return false;
                }

                var id = item.Id.Trim();
                if (!normalized.TryGetValue(id, out var current))
                {
                    current = new NormalizedBatchPriceUpdate
                    {
                        SetRetailPrice = item.SetRetailPrice,
                    };
                    normalized[id] = current;
                }
                else if (current.SetRetailPrice != item.SetRetailPrice)
                {
                    error = $"套装条码 {id} 存在冲突的零售价";
                    return false;
                }

                if (item.StoreCodes == null)
                {
                    continue;
                }
                foreach (var storeCode in item.StoreCodes.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    current.StoreCodes.Add(storeCode.Trim());
                }
            }
            return true;
        }

        /// <summary>
        /// 分店一品多码不是只由子项编码唯一确定，必须同时绑定父商品，避免同码子项串改。
        /// </summary>
        private static HashSet<string> GetStoreMultiCodeProjectionKeys(
            IEnumerable<ProductSetCode> rows
        ) => rows
            .Select(x => GetStoreMultiCodeProjectionKey(x.ProductCode, x.SetProductCode))
            .Where(x => x != null)
            .Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string? GetStoreMultiCodeProjectionKey(
            string? productCode,
            string? setProductCode
        )
        {
            if (
                string.IsNullOrWhiteSpace(productCode)
                || string.IsNullOrWhiteSpace(setProductCode)
            )
            {
                return null;
            }

            return $"{productCode.Trim()}\u0001{setProductCode.Trim()}";
        }

        private static void EnsureNoSkippedSetGroups(
            SetChildPurchasePriceWritebackResultDto result
        )
        {
            if (
                result.ProductSetCode.SkippedGroupCount == 0
                && result.StoreMultiCodeProduct.SkippedGroupCount == 0
            )
            {
                return;
            }

            throw new InvalidOperationException(
                result.Errors.FirstOrDefault()?.Reason ?? "目标套装组无法重算"
            );
        }
    }
}
