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
            GridRequestDto request
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

                var countQuery = db.Queryable<ProductSetCode>()
                    .InnerJoin<Product>((psc, p) => psc.ProductCode == p.ProductCode)
                    .LeftJoin<HBLocalSupplier>(
                        (psc, p, ls) => p.LocalSupplierCode == ls.LocalSupplierCode
                    )
                    .Where((psc, p, ls) => !psc.IsDeleted);

                if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
                {
                    var keyword = request.GlobalSearch.Trim();
                    countQuery = countQuery.Where(
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

                var total = await countQuery.CountAsync();

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

                _logger.LogInformation(
                    $"BatchUpdateStatusAsync 开始, 操作人: {updatedBy}, 套装条码ID数量: {ids.Count}, 分店数量: {storeCodes?.Count ?? 0}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var count = await db.Updateable<ProductSetCode>()
                        .SetColumns(psc => new ProductSetCode
                        {
                            IsActive = isActive,
                            UpdatedAt = now,
                            UpdatedBy = updatedBy,
                        })
                        .Where(psc => ids.Contains(psc.SetCodeId) && !psc.IsDeleted)
                        .ExecuteCommandAsync();

                    _logger.LogInformation($"更新套装条码状态: {count} 条");
                    updatedCount = count;

                    // 如果提供了分店列表，同步更新 StoreMultiCodeProduct
                    if (storeCodes != null && storeCodes.Count > 0 && ids.Any())
                    {
                        var setProductCodes = await db.Queryable<ProductSetCode>()
                            .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                            .Select(x => x.SetProductCode)
                            .ToListAsync();

                        if (setProductCodes.Any())
                        {
                            var distinctSetProductCodes = setProductCodes
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct()
                                .ToList();

                            if (distinctSetProductCodes.Any())
                            {
                                updatedMultiCodeCount = await db.Updateable<StoreMultiCodeProduct>()
                                    .SetColumns(m => new StoreMultiCodeProduct
                                    {
                                        IsActive = isActive,
                                        UpdatedAt = now,
                                        UpdatedBy = updatedBy,
                                    })
                                    .Where(m =>
                                        m.MultiCodeProductCode != null
                                        && distinctSetProductCodes.Contains(m.MultiCodeProductCode)
                                        && m.StoreCode != null
                                        && storeCodes.Contains(m.StoreCode))
                                    .ExecuteCommandAsync();

                                _logger.LogInformation($"同步更新分店一品多码状态: {updatedMultiCodeCount} 条");
                            }
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

                foreach (var it in items)
                {
                    if (it.SetPurchasePrice.HasValue && it.SetPurchasePrice.Value < 0)
                        return ApiResponse<bool>.Error("进货价不能为负数");
                    if (it.SetRetailPrice.HasValue && it.SetRetailPrice.Value < 0)
                        return ApiResponse<bool>.Error("零售价不能为负数");
                }

                var ids = items.Select(x => x.Id).ToList();
                var list = await db.Queryable<ProductSetCode>()
                    .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                    .ToListAsync();

                await db.Ado.BeginTranAsync();
                try
                {
                    foreach (var row in list)
                    {
                        var upd = items.First(x => x.Id == row.SetCodeId);
                        row.SetPurchasePrice = upd.SetPurchasePrice ?? row.SetPurchasePrice;
                        row.SetRetailPrice = upd.SetRetailPrice ?? row.SetRetailPrice;
                        row.UpdatedAt = now;
                        row.UpdatedBy = updatedBy;
                    }

                    var count = await db.Updateable(list).ExecuteCommandAsync();
                    _logger.LogInformation($"更新套装条码价格: {count} 条");
                    updatedCount = count;

                    // 如果提供了分店列表，同步更新 StoreMultiCodeProduct 的价格
                    var storeCodes = items
                        .Where(x => x.StoreCodes != null && x.StoreCodes.Count > 0)
                        .SelectMany(x => x.StoreCodes!)
                        .Distinct()
                        .ToList();

                    if (storeCodes.Count > 0 && list.Any())
                    {
                        var setProductCodes = list
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .Select(x => x.SetProductCode!)
                            .Distinct()
                            .ToList();

                        var priceUpdates = list
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .ToDictionary(
                                x => x.SetProductCode!,
                                x => new { PurchasePrice = x.SetPurchasePrice, RetailPrice = x.SetRetailPrice }
                            );

                        if (setProductCodes.Any() && priceUpdates.Any())
                        {
                            var multiCodeList = await db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.MultiCodeProductCode != null
                                    && setProductCodes.Contains(m.MultiCodeProductCode)
                                    && m.StoreCode != null
                                    && storeCodes.Contains(m.StoreCode))
                                .ToListAsync();

                            foreach (var multiCode in multiCodeList)
                            {
                                if (priceUpdates.TryGetValue(multiCode.MultiCodeProductCode!, out var prices))
                                {
                                    if (prices.PurchasePrice.HasValue)
                                    {
                                        multiCode.PurchasePrice = prices.PurchasePrice;
                                    }
                                    if (prices.RetailPrice.HasValue)
                                    {
                                        multiCode.MultiCodeRetailPrice = prices.RetailPrice;
                                    }
                                    multiCode.UpdatedAt = now;
                                    multiCode.UpdatedBy = updatedBy;
                                }
                            }

                            if (multiCodeList.Count > 0)
                            {
                                await db.Updateable(multiCodeList).ExecuteCommandAsync();
                                _logger.LogInformation($"同步更新分店一品多码价格: {multiCodeList.Count} 条");
                                updatedMultiCodeCount = multiCodeList.Count;
                            }
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
                    var toDeleteSetCodes = await db.Queryable<ProductSetCode>()
                        .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                        .ToListAsync();

                    _logger.LogInformation($"查询到待删除套装条码: {toDeleteSetCodes.Count} 条");

                    if (toDeleteSetCodes.Any())
                    {
                        var setProductCodes = toDeleteSetCodes
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .Select(x => x.SetProductCode!)
                            .Distinct()
                            .ToList();

                        if (setProductCodes.Any())
                        {
                            deletedMultiCode = await db.Deleteable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.MultiCodeProductCode != null
                                    && setProductCodes.Contains(m.MultiCodeProductCode))
                                .ExecuteCommandAsync();

                            _logger.LogInformation($"物理删除分店一品多码: {deletedMultiCode} 条");
                        }

                        deletedCount = await db.Deleteable<ProductSetCode>()
                            .Where(x => ids.Contains(x.SetCodeId))
                            .ExecuteCommandAsync();

                        _logger.LogInformation($"物理删除套装条码: {deletedCount} 条");
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
                var list = await db.Queryable<ProductSetCode>()
                    .Where(x => ids.Contains(x.SetCodeId) && !x.IsDeleted)
                    .ToListAsync();

                await db.Ado.BeginTranAsync();
                try
                {
                    foreach (var row in list)
                    {
                        var upd = items.First(x => x.Id == row.SetCodeId);
                        row.SetBarcode = upd.SetBarcode; // 允许为空或重复
                        row.UpdatedAt = now;
                        row.UpdatedBy = updatedBy;
                    }

                    var count = await db.Updateable(list).ExecuteCommandAsync();
                    _logger.LogInformation($"更新套装条码: {count} 条");

                    // 同步更新所有分店的 StoreMultiCodeProduct
                    if (list.Any())
                    {
                        var setProductCodes = list
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .Select(x => x.SetProductCode!)
                            .Distinct()
                            .ToList();

                        var barcodeUpdates = list
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .ToDictionary(x => x.SetProductCode!, x => x.SetBarcode);

                        if (setProductCodes.Any() && barcodeUpdates.Any())
                        {
                            var multiCodeList = await db.Queryable<StoreMultiCodeProduct>()
                                .Where(m =>
                                    m.MultiCodeProductCode != null
                                    && setProductCodes.Contains(m.MultiCodeProductCode))
                                .ToListAsync();

                            foreach (var multiCode in multiCodeList)
                            {
                                if (barcodeUpdates.TryGetValue(multiCode.MultiCodeProductCode!, out var barcode))
                                {
                                    multiCode.MultiBarcode = barcode;
                                    multiCode.UpdatedAt = now;
                                    multiCode.UpdatedBy = updatedBy;
                                }
                            }

                            if (multiCodeList.Count > 0)
                            {
                                await db.Updateable(multiCodeList).ExecuteCommandAsync();
                                _logger.LogInformation($"同步更新分店一品多码条码: {multiCodeList.Count} 条");
                            }
                        }
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
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;

                var productCodes = items
                    .Select(i => i.ProductCode)
                    .Where(pc => !string.IsNullOrEmpty(pc))
                    .Distinct()
                    .ToList();
                var products = await db.Queryable<Product>()
                    .Where(p =>
                        p.ProductCode != null
                        && productCodes.Contains(p.ProductCode)
                        && !p.IsDeleted
                    )
                    .ToListAsync();
                var productMap = products.ToDictionary(p => p.ProductCode!, p => p);

                // 按商品提前获取库中已有 SetItemNumber，避免同批多条生成重复货号
                var existingSetNosByProduct = new Dictionary<string, List<string>>();
                foreach (var pc in productCodes)
                {
                    var existing = await db.Queryable<ProductSetCode>()
                        .Where(x => x.ProductCode == pc && !x.IsDeleted)
                        .Select(x => x.SetItemNumber)
                        .ToListAsync();
                    existingSetNosByProduct[pc] = existing ?? new List<string>();
                }

                var assignedInBatchByProduct = new Dictionary<string, List<string>>();

                var newRows = new List<ProductSetCode>();
                foreach (var it in items)
                {
                    if (
                        string.IsNullOrEmpty(it.ProductCode)
                        || !productMap.TryGetValue(it.ProductCode!, out var prod)
                    )
                        continue;

                    var productCode = it.ProductCode!;
                    if (!assignedInBatchByProduct.ContainsKey(productCode))
                        assignedInBatchByProduct[productCode] = new List<string>();

                    var existingSetNos = existingSetNosByProduct.TryGetValue(productCode, out var existing)
                        ? existing
                        : new List<string>();
                    var usedSetNos = existingSetNos
                        .Concat(assignedInBatchByProduct[productCode])
                        .ToList();

                    var baseItemNumber = prod.ItemNumber ?? string.Empty;
                    var setItemNumber = string.IsNullOrWhiteSpace(it.SetItemNumber)
                        ? ItemNumberHelper.GenerateSetItemNumber(baseItemNumber, usedSetNos)
                        : it.SetItemNumber!;

                    if (string.IsNullOrWhiteSpace(it.SetItemNumber))
                        assignedInBatchByProduct[productCode].Add(setItemNumber);

                    var row = new ProductSetCode
                    {
                        SetCodeId = UuidHelper.GenerateUuid7(),
                        ProductCode = it.ProductCode,
                        SetProductCode = UuidHelper.GenerateUuid7(),
                        SetItemNumber = setItemNumber,
                        SetBarcode = it.SetBarcode,
                        SetPurchasePrice = it.SetPurchasePrice,
                        SetRetailPrice = it.SetRetailPrice,
                        SetQuantity = 1,
                        SetType = 2,
                        IsActive = it.IsActive ?? true,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy,
                        IsDeleted = false,
                    };
                    newRows.Add(row);
                }

                if (newRows.Count == 0)
                    return ApiResponse<List<string>>.OK(new List<string>(), "无可创建的记录");

                await db.Ado.BeginTranAsync();
                try
                {
                    var count = await db.Insertable(newRows).ExecuteCommandAsync();
                    var ids = newRows.Select(r => r.SetCodeId).ToList();

                    // 自动为有效分店写入分店一品多码表（StoreMultiCodeProduct）
                    var activeStoreCodes = await db.Queryable<Store>()
                        .Where(s => s.IsActive == true && s.IsDeleted == false)
                        .Select(s => s.StoreCode)
                        .ToListAsync();

                    if (activeStoreCodes != null && activeStoreCodes.Count > 0)
                    {
                        var multiCodeList = new List<StoreMultiCodeProduct>();
                        foreach (var row in newRows)
                        {
                            var mainProduct = productMap.TryGetValue(row.ProductCode, out var p) ? p : null;
                            foreach (var storeCode in activeStoreCodes)
                            {
                                if (string.IsNullOrWhiteSpace(storeCode))
                                    continue;

                                multiCodeList.Add(new StoreMultiCodeProduct
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = storeCode,
                                    ProductCode = row.ProductCode,
                                    MultiCodeProductCode = row.SetProductCode,
                                    StoreMultiCodeProductCode = storeCode + (row.SetProductCode ?? string.Empty),
                                    MultiBarcode = row.SetBarcode,
                                    PurchasePrice = row.SetPurchasePrice,
                                    MultiCodeRetailPrice = row.SetRetailPrice,
                                    DiscountRate = null,
                                    IsAutoPricing = false,
                                    IsSpecialProduct = mainProduct?.IsSpecialProduct ?? false,
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
                            _logger.LogInformation(
                                "添加条码后已为 {StoreCount} 个分店写入 {Count} 条一品多码",
                                activeStoreCodes.Count,
                                multiCodeList.Count
                            );
                        }
                    }

                    await db.Ado.CommitTranAsync();
                    return ApiResponse<List<string>>.OK(
                        ids,
                        $"已创建 {count} 条记录"
                        + (
                            activeStoreCodes != null && activeStoreCodes.Count > 0
                                ? $"，已同步至 {activeStoreCodes.Count} 个分店"
                                : ""
                        )
                    );
                }
                catch (Exception)
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
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
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var errors = new List<string>();
                var insertedSetCodes = 0;
                var syncedCount = 0;

                _logger.LogInformation(
                    $"BatchCreateWithStoreSyncAsync 开始, 操作人: {updatedBy}, 数据条数: {items.Count}"
                );

                await db.Ado.BeginTranAsync();
                try
                {
                    var productCodes = items
                        .Select(i => i.ProductCode)
                        .Where(pc => !string.IsNullOrEmpty(pc))
                        .Distinct()
                        .ToList();
                    var products = await db.Queryable<Product>()
                        .Where(p =>
                            p.ProductCode != null
                            && productCodes.Contains(p.ProductCode)
                            && !p.IsDeleted
                        )
                        .ToListAsync();
                    var productMap = products.ToDictionary(p => p.ProductCode!, p => p);

                    var newSetCodeRows = new List<ProductSetCode>();
                    var storePriceUpsertItems = new List<StoreRetailPriceUpsertItemDto>();

                    foreach (var it in items)
                    {
                        try
                        {
                            if (
                                string.IsNullOrEmpty(it.ProductCode)
                                || !productMap.TryGetValue(it.ProductCode!, out var prod)
                            )
                            {
                                errors.Add($"商品不存在: {it.ProductCode}");
                                continue;
                            }

                            var existingSetNos = await db.Queryable<ProductSetCode>()
                                .Where(x => x.ProductCode == it.ProductCode && !x.IsDeleted)
                                .Select(x => x.SetItemNumber)
                                .ToListAsync();

                            var baseItemNumber = prod.ItemNumber ?? string.Empty;
                            var setItemNumber = string.IsNullOrWhiteSpace(it.SetItemNumber)
                                ? ItemNumberHelper.GenerateSetItemNumber(
                                    baseItemNumber,
                                    existingSetNos
                                )
                                : it.SetItemNumber!;

                            var setCodeRow = new ProductSetCode
                            {
                                SetCodeId = UuidHelper.GenerateUuid7(),
                                ProductCode = it.ProductCode ?? string.Empty,
                                SetItemNumber = setItemNumber,
                                SetBarcode = it.SetBarcode,
                                SetPurchasePrice = it.SetPurchasePrice,
                                SetRetailPrice = it.SetRetailPrice,
                                SetQuantity = 1,
                                SetType = 2,
                                IsActive = it.IsActive ?? true,
                                CreatedAt = now,
                                UpdatedAt = now,
                                CreatedBy = updatedBy,
                                UpdatedBy = updatedBy,
                                IsDeleted = false,
                            };
                            newSetCodeRows.Add(setCodeRow);

                            if (!string.IsNullOrWhiteSpace(it.SetBarcode) && it.StoreCodes.Any())
                            {
                                foreach (var storeCode in it.StoreCodes)
                                {
                                    storePriceUpsertItems.Add(
                                        new StoreRetailPriceUpsertItemDto
                                        {
                                            ProductCode = it.SetBarcode,
                                            StoreCode = storeCode,
                                            SupplierCode = it.SupplierCode,
                                            PurchasePrice = it.SetPurchasePrice,
                                            StoreRetailPriceValue = it.SetRetailPrice,
                                            IsActive = it.IsActive ?? true,
                                            IsAutoPricing = false,
                                        }
                                    );
                                }
                            }
                        }
                        catch (Exception exItem)
                        {
                            var errorMsg =
                                $"处理数据失败: {exItem.Message}, 商品: {it.ProductCode}";
                            errors.Add(errorMsg);
                            _logger.LogError(exItem, errorMsg);
                        }
                    }

                    if (newSetCodeRows.Count == 0)
                    {
                        await db.Ado.RollbackTranAsync();
                        return ApiResponse<BatchResultDto>.OK(
                            new BatchResultDto { Inserted = 0, Updated = 0, Failed = errors.Count, Errors = errors },
                            "无可创建的记录"
                        );
                    }

                    insertedSetCodes = await db.Insertable(newSetCodeRows).ExecuteCommandAsync();
                    _logger.LogInformation($"插入套装条码: {insertedSetCodes} 条");

                    // 自动为有效分店写入分店一品多码表（StoreMultiCodeProduct）
                    var activeStoreCodes = await db.Queryable<Store>()
                        .Where(s => s.IsActive == true && s.IsDeleted == false)
                        .Select(s => s.StoreCode)
                        .ToListAsync();

                    if (activeStoreCodes != null && activeStoreCodes.Count > 0)
                    {
                        var multiCodeList = new List<StoreMultiCodeProduct>();
                        foreach (var row in newSetCodeRows)
                        {
                            var mainProduct = productMap.TryGetValue(row.ProductCode, out var p) ? p : null;
                            foreach (var storeCode in activeStoreCodes)
                            {
                                if (string.IsNullOrWhiteSpace(storeCode))
                                    continue;

                                multiCodeList.Add(new StoreMultiCodeProduct
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = storeCode,
                                    ProductCode = row.ProductCode,
                                    MultiCodeProductCode = row.SetProductCode,
                                    StoreMultiCodeProductCode = storeCode + (row.SetProductCode ?? string.Empty),
                                    MultiBarcode = row.SetBarcode,
                                    PurchasePrice = row.SetPurchasePrice,
                                    MultiCodeRetailPrice = row.SetRetailPrice,
                                    DiscountRate = null,
                                    IsAutoPricing = false,
                                    IsSpecialProduct = mainProduct?.IsSpecialProduct ?? false,
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
                            _logger.LogInformation(
                                "添加条码后已为 {StoreCount} 个分店写入 {Count} 条一品多码",
                                activeStoreCodes.Count,
                                multiCodeList.Count
                            );
                        }
                    }

                    if (storePriceUpsertItems.Any())
                    {
                        var syncResult = await _storeRetailPriceService.BatchUpsertAsync(
                            storePriceUpsertItems,
                            updatedBy
                        );
                        if (!syncResult.Success)
                        {
                            await db.Ado.RollbackTranAsync();
                            _logger.LogError("同步到分店失败: {Message}", syncResult.Message);
                            return ApiResponse<BatchResultDto>.Error(
                                $"同步到分店失败: {syncResult.Message}",
                                "SYNC_TO_STORES_ERROR"
                            );
                        }
                        syncedCount = syncResult.Data.Inserted + syncResult.Data.Updated;
                        _logger.LogInformation($"同步到分店: {syncedCount} 条");

                        if (syncResult.Data.Errors.Any())
                        {
                            errors.AddRange(syncResult.Data.Errors);
                        }
                    }

                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("事务提交成功");

                    var result = new BatchResultDto
                    {
                        Inserted = insertedSetCodes,
                        Updated = syncedCount,
                        Failed = errors.Count,
                        Errors = errors,
                    };

                    return ApiResponse<BatchResultDto>.OK(
                        result,
                        $"成功创建 {insertedSetCodes} 条套装条码, 同步 {syncedCount} 条到分店"
                    );
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "批量创建并同步事务失败, 事务已回滚");
                    return ApiResponse<BatchResultDto>.Error(
                        $"批量创建并同步失败: {ex.Message}",
                        "BATCH_CREATE_SYNC_ERROR"
                    );
                }
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

                    _logger.LogInformation($"查询到待删除套装条码: {toDeleteSetCodes.Count} 条");

                    if (toDeleteSetCodes.Any())
                    {
                        var setProductCodes = toDeleteSetCodes
                            .Where(x => !string.IsNullOrWhiteSpace(x.SetProductCode))
                            .Select(x => x.SetProductCode!)
                            .Distinct()
                            .ToList();

                        if (setProductCodes.Any())
                        {
                            if (storeCodes != null && storeCodes.Count > 0)
                            {
                                deletedMultiCode = await db.Deleteable<StoreMultiCodeProduct>()
                                    .Where(m =>
                                        m.MultiCodeProductCode != null
                                        && setProductCodes.Contains(m.MultiCodeProductCode)
                                        && m.StoreCode != null
                                        && storeCodes.Contains(m.StoreCode))
                                    .ExecuteCommandAsync();
                            }
                            else
                            {
                                deletedMultiCode = await db.Deleteable<StoreMultiCodeProduct>()
                                    .Where(m =>
                                        m.MultiCodeProductCode != null
                                        && setProductCodes.Contains(m.MultiCodeProductCode))
                                    .ExecuteCommandAsync();
                            }

                            _logger.LogInformation($"物理删除分店一品多码: {deletedMultiCode} 条");
                        }

                        deletedSetCodes = await db.Deleteable<ProductSetCode>()
                            .Where(x => ids.Contains(x.SetCodeId))
                            .ExecuteCommandAsync();
                        _logger.LogInformation($"物理删除套装条码: {deletedSetCodes} 条");
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
                    return ApiResponse<BatchResultDto>.Error(
                        $"批量删除并同步失败: {ex.Message}",
                        "BATCH_DELETE_SYNC_ERROR"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除并同步失败");
                return ApiResponse<BatchResultDto>.Error(
                    $"批量删除并同步失败: {ex.Message}",
                    "BATCH_DELETE_SYNC_ERROR"
                );
            }
        }
    }
}
