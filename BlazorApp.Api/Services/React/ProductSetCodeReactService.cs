using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class ProductSetCodeReactService : IProductSetCodeReactService
    {
        private readonly SqlSugarContext _context;

        public ProductSetCodeReactService(SqlSugarContext context)
        {
            _context = context;
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
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var count = await db.Updateable<ProductSetCode>()
                    .SetColumns(psc => new ProductSetCode
                    {
                        IsActive = isActive,
                        UpdatedAt = now,
                        UpdatedBy = updatedBy,
                    })
                    .Where(psc => ids.Contains(psc.SetCodeId) && !psc.IsDeleted)
                    .ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, $"已更新 {count} 条状态");
            }
            catch (Exception ex)
            {
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

                foreach (var row in list)
                {
                    var upd = items.First(x => x.Id == row.SetCodeId);
                    row.SetPurchasePrice = upd.SetPurchasePrice ?? row.SetPurchasePrice;
                    row.SetRetailPrice = upd.SetRetailPrice ?? row.SetRetailPrice;
                    row.UpdatedAt = now;
                    row.UpdatedBy = updatedBy;
                }

                var count = await db.Updateable(list).ExecuteCommandAsync();
                return ApiResponse<bool>.OK(true, $"已更新 {count} 条价格");
            }
            catch (Exception ex)
            {
                return ApiResponse<bool>.Error($"批量更新价格失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<bool>> BatchDeleteAsync(List<string> ids, string updatedBy)
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;
                var count = await db.Updateable<ProductSetCode>()
                    .SetColumns(psc => new ProductSetCode
                    {
                        IsDeleted = true,
                        UpdatedAt = now,
                        UpdatedBy = updatedBy,
                    })
                    .Where(psc => ids.Contains(psc.SetCodeId) && !psc.IsDeleted)
                    .ExecuteCommandAsync();
                return ApiResponse<bool>.OK(true, $"已删除 {count} 条记录");
            }
            catch (Exception ex)
            {
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

                foreach (var row in list)
                {
                    var upd = items.First(x => x.Id == row.SetCodeId);
                    row.SetBarcode = upd.SetBarcode; // 允许为空或重复
                    row.UpdatedAt = now;
                    row.UpdatedBy = updatedBy;
                }

                var count = await db.Updateable(list).ExecuteCommandAsync();
                return ApiResponse<bool>.OK(true, $"已更新 {count} 条条码");
            }
            catch (Exception ex)
            {
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

                var newRows = new List<ProductSetCode>();
                foreach (var it in items)
                {
                    if (
                        string.IsNullOrEmpty(it.ProductCode)
                        || !productMap.TryGetValue(it.ProductCode!, out var prod)
                    )
                        continue;

                    var existingSetNos = await db.Queryable<ProductSetCode>()
                        .Where(x => x.ProductCode == it.ProductCode && !x.IsDeleted)
                        .Select(x => x.SetItemNumber)
                        .ToListAsync();

                    var baseItemNumber = prod.ItemNumber ?? string.Empty;
                    var setItemNumber = string.IsNullOrWhiteSpace(it.SetItemNumber)
                        ? ItemNumberHelper.GenerateSetItemNumber(baseItemNumber, existingSetNos)
                        : it.SetItemNumber!;

                    var row = new ProductSetCode
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
                    newRows.Add(row);
                }

                if (newRows.Count == 0)
                    return ApiResponse<List<string>>.OK(new List<string>(), "无可创建的记录");

                var count = await db.Insertable(newRows).ExecuteCommandAsync();
                var ids = newRows.Select(r => r.SetCodeId).ToList();
                return ApiResponse<List<string>>.OK(ids, $"已创建 {count} 条记录");
            }
            catch (Exception ex)
            {
                return ApiResponse<List<string>>.Error($"批量创建失败: {ex.Message}");
            }
        }
    }
}
