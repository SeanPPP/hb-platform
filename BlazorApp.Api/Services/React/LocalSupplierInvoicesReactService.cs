using System.Linq;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocalSupplierInvoicesReactService : ILocalSupplierInvoicesReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<LocalSupplierInvoicesReactService> _logger;

        public LocalSupplierInvoicesReactService(
            SqlSugarContext context,
            ILogger<LocalSupplierInvoicesReactService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        public async Task<GridResponseDto<LocalSupplierInvoiceListDto>> GetGridDataAsync(
            GridRequestDto request
        )
        {
            try
            {
                var db = _context.Db;
                var query = db.Queryable<StoreLocalSupplierInvoice>()
                    .LeftJoin<Store>((h, st) => h.StoreCode == st.StoreCode)
                    .LeftJoin<HBLocalSupplier>(
                        (h, st, sup) => h.SupplierCode == sup.LocalSupplierCode
                    )
                    .Where((h, st, sup) => h.IsDeleted == false);

                if (request.FilterModel != null && request.FilterModel.Any())
                {
                    foreach (var kv in request.FilterModel)
                    {
                        var col = kv.Key;
                        var f = kv.Value;
                        if (f == null || f.FilterType == null)
                            continue;
                        var type = f.FilterType.ToLower();
                        if (type == "text" && f.Filter != null)
                        {
                            var v = f.Filter?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(v))
                                continue;
                            var op = (f.Type ?? "contains").ToLower();
                            switch (col)
                            {
                                case "storeCode":
                                    query = ApplyText(query, op, v, x => x.StoreCode);
                                    break;
                                case "supplierCode":
                                    query = ApplyText(query, op, v, x => x.SupplierCode);
                                    break;
                                case "invoiceNo":
                                    query = ApplyText(query, op, v, x => x.InvoiceNo);
                                    break;
                                case "storeName":
                                    query = query.Where((h, st, sup) => st.StoreName.Contains(v));
                                    break;
                                case "supplierName":
                                    query = query.Where((h, st, sup) => sup.Name.Contains(v));
                                    break;
                                case "remarks":
                                    query = ApplyText(query, op, v, x => x.Remarks);
                                    break;
                            }
                        }
                        else if (type == "number" && f.Filter != null)
                        {
                            if (decimal.TryParse(f.Filter.ToString(), out var numValue))
                            {
                                var op = (f.Type ?? "equals").ToLower();
                                switch (col)
                                {
                                    case "totalAmount":
                                        query = ApplyNumber(
                                            query,
                                            op,
                                            x => x.TotalAmount,
                                            numValue,
                                            f.FilterTo
                                        );
                                        break;
                                    case "receivedTotalAmount":
                                        query = ApplyNumber(
                                            query,
                                            op,
                                            x => x.ReceivedTotalAmount,
                                            numValue,
                                            f.FilterTo
                                        );
                                        break;
                                }
                            }
                        }
                    }
                }

                if (request.SortModel != null && request.SortModel.Any())
                {
                    var s = request.SortModel.First();
                    var asc = s.Sort.ToLower() == "asc";
                    query = s.ColId switch
                    {
                        "StoreName" => query.OrderBy(
                            (h, st, sup) => st.StoreName,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "SupplierName" => query.OrderBy(
                            (h, st, sup) => sup.Name,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "InvoiceNo" => query.OrderBy(
                            (h, st, sup) => h.InvoiceNo,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "OrderDate" => query.OrderBy(
                            (h, st, sup) => h.OrderDate,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "InboundDate" => query.OrderBy(
                            (h, st, sup) => h.InboundDate,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "TotalAmount" => query.OrderBy(
                            (h, st, sup) => h.TotalAmount,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "ReceivedTotalAmount" => query.OrderBy(
                            (h, st, sup) => h.ReceivedTotalAmount,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "CreatedAt" => query.OrderBy(
                            (h, st, sup) => h.CreatedAt,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        _ => query.OrderBy((h, st, sup) => h.CreatedAt, OrderByType.Desc),
                    };
                }
                else
                {
                    query = query.OrderBy((h, st, sup) => h.CreatedAt, OrderByType.Desc);
                }

                var total = await query.CountAsync();
                var pageSize = request.PageSize > 0 ? request.PageSize : 20;
                var startRow = request.StartRow >= 0 ? request.StartRow : 0;
                var list = await query
                    .Select(
                        (h, st, sup) =>
                            new LocalSupplierInvoiceListDto
                            {
                                InvoiceGUID = h.InvoiceGUID,
                                StoreCode = h.StoreCode,
                                StoreName = st.StoreName,
                                SupplierCode = h.SupplierCode,
                                SupplierName = sup.Name,
                                InvoiceNo = h.InvoiceNo,
                                VoucherType = h.VoucherType,
                                OrderDate = h.OrderDate,
                                InboundDate = h.InboundDate,
                                TotalAmount = h.TotalAmount,
                                ReceivedTotalAmount = h.ReceivedTotalAmount,
                                FlowStatus = h.FlowStatus,
                                InboundStatus = h.InboundStatus,
                                Remarks = h.Remarks,
                                CreatedAt = h.CreatedAt,
                                UpdatedAt = h.UpdatedAt,
                            }
                    )
                    .Skip(startRow)
                    .Take(pageSize)
                    .ToListAsync();

                return GridResponseDto<LocalSupplierInvoiceListDto>.OK(list, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LocalSupplierInvoice Grid 查询失败");
                return GridResponseDto<LocalSupplierInvoiceListDto>.Error("查询失败");
            }
        }

        public async Task<ApiResponse<LocalSupplierInvoiceDetailDto>> GetInvoiceAsync(
            string invoiceGuid
        )
        {
            try
            {
                var db = _context.Db;
                var item = await db.Queryable<StoreLocalSupplierInvoice>()
                    .LeftJoin<Store>((h, st) => h.StoreCode == st.StoreCode)
                    .LeftJoin<HBLocalSupplier>(
                        (h, st, sup) => h.SupplierCode == sup.LocalSupplierCode
                    )
                    .Where((h, st, sup) => h.InvoiceGUID == invoiceGuid && h.IsDeleted == false)
                    .Select(
                        (h, st, sup) =>
                            new LocalSupplierInvoiceDetailDto
                            {
                                InvoiceGUID = h.InvoiceGUID,
                                AppGUID = h.AppGUID,
                                PcGUID = h.PcGUID,
                                StoreCode = h.StoreCode,
                                StoreName = st.StoreName,
                                SupplierCode = h.SupplierCode,
                                SupplierName = sup.Name,
                                InvoiceNo = h.InvoiceNo,
                                VoucherType = h.VoucherType,
                                OrderDate = h.OrderDate,
                                InboundDate = h.InboundDate,
                                TotalAmount = h.TotalAmount,
                                ReceivedTotalAmount = h.ReceivedTotalAmount,
                                VoucherImage = h.VoucherImage,
                                Remarks = h.Remarks,
                                ImportTemplate = h.ImportTemplate,
                                FlowStatus = h.FlowStatus,
                                InboundStatus = h.InboundStatus,
                                CreatedAt = h.CreatedAt,
                                UpdatedAt = h.UpdatedAt,
                            }
                    )
                    .FirstAsync();

                if (item == null)
                    return ApiResponse<LocalSupplierInvoiceDetailDto>.Error(
                        "数据不存在",
                        "NOT_FOUND"
                    );
                return ApiResponse<LocalSupplierInvoiceDetailDto>.OK(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取进货单详情失败");
                return ApiResponse<LocalSupplierInvoiceDetailDto>.Error("获取失败", "GET_ERROR");
            }
        }

        public async Task<ApiResponse<List<LocalSupplierInvoiceItemDto>>> GetDetailsAsync(
            string invoiceGuid
        )
        {
            try
            {
                var db = _context.Db;
                var list = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                    .Where((d, p) => d.InvoiceGUID == invoiceGuid && d.IsDeleted == false)
                    .Select(
                        (d, p) =>
                            new LocalSupplierInvoiceItemDto
                            {
                                DetailGUID = d.DetailGUID,
                                InvoiceGUID = d.InvoiceGUID,
                                StoreCode = d.StoreCode,
                                SupplierCode = d.SupplierCode,
                                ProductTagGUID = d.ProductTagGUID,
                                ProductCategoryGUID = d.ProductCategoryGUID,
                                StoreProductCode = d.StoreProductCode,
                                ProductCode = d.ProductCode,
                                ItemNumber = d.ItemNumber,
                                Barcode = d.Barcode,
                                ProductName = d.ProductName,
                                Specification = d.Specification,
                                Unit = d.Unit,
                                Quantity = d.Quantity,
                                LastPurchasePrice = d.LastPurchasePrice,
                                PurchasePrice = d.PurchasePrice,
                                RetailPrice = d.RetailPrice,
                                Amount = d.Amount,
                                ExistingProductCount = d.ExistingProductCount,
                                ProductImage = p.ProductImage,
                                ActivityType = d.ActivityType,
                                DiscountRate = d.DiscountRate,
                                AutoPricing = d.AutoPricing,
                                PricingFloatRate = d.PricingFloatRate,
                                NewAutoRetailPrice = d.NewAutoRetailPrice,
                                IsSpecialProduct = d.IsSpecialProduct,
                                OldStoreProductCode = d.OldStoreProductCode,
                            }
                    )
                    .ToListAsync();

                return ApiResponse<List<LocalSupplierInvoiceItemDto>>.OK(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取进货单明细失败");
                return ApiResponse<List<LocalSupplierInvoiceItemDto>>.Error(
                    "获取失败",
                    "GET_ERROR"
                );
            }
        }

        public async Task<ApiResponse<bool>> UpdateAsync(
            string invoiceGuid,
            UpdateInvoiceRequest dto
        )
        {
            try
            {
                var db = _context.Db;
                var exists = await db.Queryable<StoreLocalSupplierInvoice>()
                    .AnyAsync(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false);
                if (!exists)
                    return ApiResponse<bool>.Error("数据不存在", "NOT_FOUND");

                var now = DateTime.UtcNow;
                var updater = db.Updateable<StoreLocalSupplierInvoice>()
                    .SetColumnsIF(dto.InvoiceNo != null, x => x.InvoiceNo == dto.InvoiceNo)
                    .SetColumnsIF(dto.OrderDate != null, x => x.OrderDate == dto.OrderDate)
                    .SetColumnsIF(dto.InboundDate != null, x => x.InboundDate == dto.InboundDate)
                    .SetColumnsIF(dto.Remarks != null, x => x.Remarks == dto.Remarks)
                    .SetColumnsIF(dto.VoucherImage != null, x => x.VoucherImage == dto.VoucherImage)
                    .SetColumnsIF(dto.FlowStatus != null, x => x.FlowStatus == dto.FlowStatus)
                    .SetColumnsIF(
                        dto.InboundStatus != null,
                        x => x.InboundStatus == dto.InboundStatus
                    )
                    .SetColumnsIF(
                        !string.IsNullOrWhiteSpace(dto.StoreCode),
                        x => x.StoreCode == dto.StoreCode
                    )
                    .SetColumnsIF(
                        !string.IsNullOrWhiteSpace(dto.SupplierCode),
                        x => x.SupplierCode == dto.SupplierCode
                    )
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.InvoiceGUID == invoiceGuid);

                var affected = await updater.ExecuteCommandAsync();
                return affected > 0
                    ? ApiResponse<bool>.OK(true)
                    : ApiResponse<bool>.Error("未更新任何字段", "NO_CHANGE");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新进货单失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "更新失败";
                return ApiResponse<bool>.Error(msg, "UPDATE_ERROR");
            }
        }

        public async Task<ApiResponse<string>> CreateAsync(CreateInvoiceRequest dto)
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(dto.StoreCode)
                    || string.IsNullOrWhiteSpace(dto.SupplierCode)
                    || string.IsNullOrWhiteSpace(dto.InvoiceNo)
                )
                    return ApiResponse<string>.Error(
                        "分店、供应商、单号为必填",
                        "VALIDATION_ERROR"
                    );

                var db = _context.Db;
                var invoiceGuid = UuidHelper.GenerateUuid7();
                var now = DateTime.UtcNow;

                var header = new StoreLocalSupplierInvoice
                {
                    InvoiceGUID = invoiceGuid,
                    StoreCode = dto.StoreCode,
                    SupplierCode = dto.SupplierCode,
                    InvoiceNo = dto.InvoiceNo,
                    OrderDate = dto.OrderDate,
                    InboundDate = dto.InboundDate,
                    Remarks = dto.Remarks,
                    CreatedAt = now,
                    UpdatedAt = now,
                    IsDeleted = false,
                };

                await db.Insertable(header).ExecuteCommandAsync();

                var validItems = (dto.Items ?? new List<PastedDetailItem>())
                    .Where(i => i != null && i.Quantity > 0 && i.Price > 0)
                    .ToList();

                var detailRows = validItems
                    .Select(i => new StoreLocalSupplierInvoiceDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        InvoiceGUID = invoiceGuid,
                        StoreCode = dto.StoreCode,
                        SupplierCode = dto.SupplierCode,
                        StoreProductCode = i.StoreProductCode,
                        ProductCode = i.ProductCode,
                        ItemNumber = i.ItemNumber,
                        Barcode = !string.IsNullOrWhiteSpace(i.Barcode)
                            ? i.Barcode
                            : (IsLikelyBarcode(i.NameOrBarcode) ? i.NameOrBarcode : null),
                        ProductName = !string.IsNullOrWhiteSpace(i.ProductName)
                            ? i.ProductName
                            : (
                                string.IsNullOrWhiteSpace(i.Barcode)
                                && !IsLikelyBarcode(i.NameOrBarcode)
                                    ? i.NameOrBarcode
                                    : null
                            ),
                        Quantity = i.Quantity,
                        PurchasePrice = i.Price,
                        LastPurchasePrice = i.LastPurchasePrice,
                        RetailPrice = i.RetailPrice,
                        AutoPricing = i.AutoPricing,
                        PricingFloatRate = i.PricingFloatRate,
                        NewAutoRetailPrice = i.NewAutoRetailPrice,
                        IsSpecialProduct = i.IsSpecialProduct,
                        Amount = i.Price * i.Quantity,
                        CreatedAt = now,
                        UpdatedAt = now,
                        IsDeleted = false,
                    })
                    .ToList();

                if (detailRows.Count > 0)
                    await db.Insertable(detailRows).ExecuteCommandAsync();

                var total = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .SumAsync(x => x.Amount ?? 0);
                await db.Updateable<StoreLocalSupplierInvoice>()
                    .SetColumns(x => x.TotalAmount == total)
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.InvoiceGUID == invoiceGuid)
                    .ExecuteCommandAsync();

                return ApiResponse<string>.OK(invoiceGuid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建进货单失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "创建失败";
                return ApiResponse<string>.Error(msg, "CREATE_ERROR");
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string invoiceGuid, string updatedBy)
        {
            try
            {
                var db = _context.Db;
                await db.Ado.BeginTranAsync();
                try
                {
                    var now = DateTime.UtcNow;
                    var affectedHeader = await db.Updateable<StoreLocalSupplierInvoice>()
                        .SetColumns(x => x.IsDeleted == true)
                        .SetColumns(x => x.UpdatedAt == now)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .Where(x => x.InvoiceGUID == invoiceGuid)
                        .ExecuteCommandAsync();
                    var affectedDetails = await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                        .SetColumns(x => x.IsDeleted == true)
                        .SetColumns(x => x.UpdatedAt == now)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .Where(x => x.InvoiceGUID == invoiceGuid)
                        .ExecuteCommandAsync();
                    await db.Ado.CommitTranAsync();
                    return ApiResponse<bool>.OK(true, $"已删除单据及 {affectedDetails} 条明细");
                }
                catch (Exception exTran)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(exTran, "删除进货单事务失败");
                    return ApiResponse<bool>.Error("删除失败", "DELETE_ERROR");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除进货单失败");
                return ApiResponse<bool>.Error("删除失败", "DELETE_ERROR");
            }
        }

        public async Task<ApiResponse<BatchResultDto>> BatchUpsertDetailsAsync(
            string invoiceGuid,
            List<InvoiceDetailUpsertItemDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;
                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .FirstAsync(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false);
                if (header == null)
                    return ApiResponse<BatchResultDto>.Error("单据不存在", "NOT_FOUND");
                var storeCode = header.StoreCode;
                var supplierCode = header.SupplierCode;
                var now = DateTime.UtcNow;
                var valid = (items ?? new List<InvoiceDetailUpsertItemDto>())
                    .Where(i => i != null)
                    .ToList();
                var toInsert = new List<StoreLocalSupplierInvoiceDetails>();
                var toUpdate = new List<StoreLocalSupplierInvoiceDetails>();
                foreach (var it in valid)
                {
                    if (!string.IsNullOrWhiteSpace(it.DetailGUID))
                    {
                        toUpdate.Add(
                            new StoreLocalSupplierInvoiceDetails
                            {
                                DetailGUID = it.DetailGUID!,
                                StoreProductCode = it.StoreProductCode,
                                ProductCode = it.ProductCode,
                                ItemNumber = it.ItemNumber,
                                Barcode = it.Barcode,
                                ProductName = it.ProductName,
                                ProductCategoryGUID = it.ProductCategoryGUID,
                                Quantity = it.Quantity,
                                LastPurchasePrice = it.LastPurchasePrice,
                                PurchasePrice = it.PurchasePrice,
                                RetailPrice = it.RetailPrice,
                                Amount = it.Amount,
                                ActivityType = it.ActivityType,
                                DiscountRate = it.DiscountRate,
                                AutoPricing = it.AutoPricing,
                                PricingFloatRate = it.PricingFloatRate,
                                NewAutoRetailPrice = it.NewAutoRetailPrice,
                                IsSpecialProduct = it.IsSpecialProduct,
                                UpdatedAt = now,
                                UpdatedBy = updatedBy,
                            }
                        );
                    }
                    else
                    {
                        toInsert.Add(
                            new StoreLocalSupplierInvoiceDetails
                            {
                                DetailGUID = BlazorApp.Shared.Helper.UuidHelper.GenerateUuid7(),
                                InvoiceGUID = invoiceGuid,
                                StoreCode = storeCode,
                                SupplierCode = supplierCode,
                                StoreProductCode = it.StoreProductCode,
                                ProductCode = it.ProductCode,
                                ItemNumber = it.ItemNumber,
                                Barcode = it.Barcode,
                                ProductName = it.ProductName,
                                ProductCategoryGUID = it.ProductCategoryGUID,
                                Quantity = it.Quantity,
                                LastPurchasePrice = it.LastPurchasePrice,
                                PurchasePrice = it.PurchasePrice,
                                RetailPrice = it.RetailPrice,
                                Amount = it.Amount,
                                ActivityType = it.ActivityType,
                                DiscountRate = it.DiscountRate,
                                AutoPricing = it.AutoPricing,
                                PricingFloatRate = it.PricingFloatRate,
                                NewAutoRetailPrice = it.NewAutoRetailPrice,
                                IsSpecialProduct = it.IsSpecialProduct,
                                CreatedAt = now,
                                UpdatedAt = now,
                                CreatedBy = updatedBy,
                                UpdatedBy = updatedBy,
                                IsDeleted = false,
                            }
                        );
                    }
                }
                await db.Ado.BeginTranAsync();
                try
                {
                    var inserted = 0;
                    var updated = 0;
                    if (toInsert.Count > 0)
                        inserted = await db.Insertable(toInsert).ExecuteCommandAsync();
                    if (toUpdate.Count > 0)
                    {
                        foreach (var u in toUpdate)
                        {
                            var eff = await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                                .SetColumnsIF(
                                    u.StoreProductCode != null,
                                    x => x.StoreProductCode == u.StoreProductCode
                                )
                                .SetColumnsIF(
                                    u.ProductCode != null,
                                    x => x.ProductCode == u.ProductCode
                                )
                                .SetColumnsIF(
                                    u.ItemNumber != null,
                                    x => x.ItemNumber == u.ItemNumber
                                )
                                .SetColumnsIF(u.Barcode != null, x => x.Barcode == u.Barcode)
                                .SetColumnsIF(
                                    u.ProductName != null,
                                    x => x.ProductName == u.ProductName
                                )
                                .SetColumnsIF(
                                    u.ProductCategoryGUID != null,
                                    x => x.ProductCategoryGUID == u.ProductCategoryGUID
                                )
                                .SetColumnsIF(u.Quantity != null, x => x.Quantity == u.Quantity)
                                .SetColumnsIF(
                                    u.LastPurchasePrice != null,
                                    x => x.LastPurchasePrice == u.LastPurchasePrice
                                )
                                .SetColumnsIF(
                                    u.PurchasePrice != null,
                                    x => x.PurchasePrice == u.PurchasePrice
                                )
                                .SetColumnsIF(
                                    u.RetailPrice != null,
                                    x => x.RetailPrice == u.RetailPrice
                                )
                                .SetColumnsIF(u.Amount != null, x => x.Amount == u.Amount)
                                .SetColumnsIF(
                                    u.ActivityType != null,
                                    x => x.ActivityType == u.ActivityType
                                )
                                .SetColumnsIF(
                                    u.DiscountRate != null,
                                    x => x.DiscountRate == u.DiscountRate
                                )
                                .SetColumnsIF(
                                    u.AutoPricing != null,
                                    x => x.AutoPricing == u.AutoPricing
                                )
                                .SetColumnsIF(
                                    u.PricingFloatRate != null,
                                    x => x.PricingFloatRate == u.PricingFloatRate
                                )
                                .SetColumnsIF(
                                    u.NewAutoRetailPrice != null,
                                    x => x.NewAutoRetailPrice == u.NewAutoRetailPrice
                                )
                                .SetColumnsIF(
                                    u.IsSpecialProduct != null,
                                    x => x.IsSpecialProduct == u.IsSpecialProduct
                                )
                                .SetColumns(x => x.UpdatedAt == now)
                                .SetColumns(x => x.UpdatedBy == updatedBy)
                                .Where(x => x.DetailGUID == u.DetailGUID)
                                .ExecuteCommandAsync();
                            if (eff == 0)
                            {
                                // 回落插入：更新未命中则作为新行插入
                                var ins = new StoreLocalSupplierInvoiceDetails
                                {
                                    DetailGUID = BlazorApp.Shared.Helper.UuidHelper.GenerateUuid7(),
                                    InvoiceGUID = invoiceGuid,
                                    StoreCode = storeCode,
                                    SupplierCode = supplierCode,
                                    StoreProductCode = u.StoreProductCode,
                                    ProductCode = u.ProductCode,
                                    ItemNumber = u.ItemNumber,
                                    Barcode = u.Barcode,
                                    ProductName = u.ProductName,
                                    ProductCategoryGUID = u.ProductCategoryGUID,
                                    Quantity = u.Quantity,
                                    LastPurchasePrice = u.LastPurchasePrice,
                                    PurchasePrice = u.PurchasePrice,
                                    RetailPrice = u.RetailPrice,
                                    Amount = u.Amount,
                                    ActivityType = u.ActivityType,
                                    DiscountRate = u.DiscountRate,
                                    AutoPricing = u.AutoPricing,
                                    PricingFloatRate = u.PricingFloatRate,
                                    NewAutoRetailPrice = u.NewAutoRetailPrice,
                                    IsSpecialProduct = u.IsSpecialProduct,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                    CreatedBy = updatedBy,
                                    UpdatedBy = updatedBy,
                                    IsDeleted = false,
                                };
                                inserted += await db.Insertable(ins).ExecuteCommandAsync();
                            }
                            else
                            {
                                updated += eff;
                            }
                        }
                    }
                    await db.Ado.CommitTranAsync();
                    var total = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                        .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                        .SumAsync(x => x.Amount ?? 0);
                    await db.Updateable<StoreLocalSupplierInvoice>()
                        .SetColumns(x => x.TotalAmount == total)
                        .SetColumns(x => x.UpdatedAt == now)
                        .Where(x => x.InvoiceGUID == invoiceGuid)
                        .ExecuteCommandAsync();
                    return ApiResponse<BatchResultDto>.OK(
                        new BatchResultDto
                        {
                            Inserted = inserted,
                            Updated = updated,
                            Failed = 0,
                        }
                    );
                }
                catch (Exception exTran)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(exTran, "批量Upsert进货单明细事务失败");
                    var msg = exTran.InnerException?.Message ?? exTran.Message ?? "批量失败";
                    return ApiResponse<BatchResultDto>.Error(msg, "BATCH_UPSERT_ERROR");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量Upsert进货单明细失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "批量失败";
                return ApiResponse<BatchResultDto>.Error(msg, "BATCH_UPSERT_ERROR");
            }
        }

        public async Task<ApiResponse<List<SupplierItemDetectResult>>> DetectSupplierItemAsync(
            DetectSupplierItemRequest dto
        )
        {
            try
            {
                var db = _context.Db;
                var inputItems = dto.Items ?? new List<DetectSupplierItem>();
                var itemNumbers = inputItems.Select(x => x?.ItemNumber?.Trim()).ToList();

                var validItemNumbers = itemNumbers
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .Distinct()
                    .ToList();

                var products = new List<ProductDetectProjection>();
                if (validItemNumbers.Count > 0)
                {
                    products = await QueryInChunksAsync<ProductDetectProjection, string>(
                        validItemNumbers,
                        500,
                        async chunk =>
                            await db.Queryable<Product>()
                                .Where(p =>
                                    p.LocalSupplierCode == dto.SupplierCode
                                    && p.ItemNumber != null
                                    && chunk.Contains(p.ItemNumber)
                                    && p.IsDeleted == false
                                )
                                .Select(p => new ProductDetectProjection
                                {
                                    ItemNumber = p.ItemNumber!,
                                    ProductCode = p.ProductCode,
                                    ProductName = p.ProductName,
                                    ProductImage = p.ProductImage,
                                })
                                .ToListAsync()
                    );
                }

                var prodByItem = products.ToDictionary(x => x.ItemNumber, x => x);

                var productCodes = products
                    .Select(x => x.ProductCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .Distinct()
                    .ToList();

                var priceByCode = new Dictionary<string, PriceDetectProjection>();
                if (productCodes.Count > 0)
                {
                    var prices = await QueryInChunksAsync<PriceDetectProjection, string>(
                        productCodes,
                        500,
                        async chunk =>
                            await db.Queryable<StoreRetailPrice>()
                                .Where(x =>
                                    x.StoreCode == dto.StoreCode
                                    && x.ProductCode != null
                                    && chunk.Contains(x.ProductCode)
                                    && x.IsDeleted == false
                                )
                                .Select(x => new PriceDetectProjection
                                {
                                    ProductCode = x.ProductCode!,
                                    PurchasePrice = x.PurchasePrice,
                                    Retail = x.StoreRetailPriceValue,
                                    StoreProductCode = x.StoreProductCode,
                                })
                                .ToListAsync()
                    );
                    priceByCode = prices.ToDictionary(x => x.ProductCode, x => x);
                }

                var results = new List<SupplierItemDetectResult>(inputItems.Count);
                foreach (var it in inputItems)
                {
                    var itemNumber = it?.ItemNumber?.Trim();
                    if (string.IsNullOrWhiteSpace(itemNumber))
                    {
                        results.Add(
                            new SupplierItemDetectResult { Exists = false, Error = "货号为空" }
                        );
                        continue;
                    }

                    if (!prodByItem.TryGetValue(itemNumber, out var prod))
                    {
                        results.Add(new SupplierItemDetectResult { Exists = false });
                        continue;
                    }

                    PriceDetectProjection? price = null;
                    if (!string.IsNullOrWhiteSpace(prod.ProductCode))
                    {
                        priceByCode.TryGetValue(prod.ProductCode!, out price);
                    }

                    results.Add(
                        new SupplierItemDetectResult
                        {
                            Exists = true,
                            ProductImage = prod.ProductImage,
                            ProductCode = prod.ProductCode,
                            StoreProductCode = price?.StoreProductCode,
                            ProductName = prod.ProductName,
                            CurrentPurchasePrice = price?.PurchasePrice,
                            CurrentRetailPrice = price?.Retail,
                        }
                    );
                }

                return ApiResponse<List<SupplierItemDetectResult>>.OK(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "供应商+货号检测失败");
                return ApiResponse<List<SupplierItemDetectResult>>.Error(
                    "检测失败",
                    "DETECT_ERROR"
                );
            }
        }

        public async Task<ApiResponse<List<BarcodeDetectResult>>> DetectBarcodeAsync(
            DetectBarcodeRequest dto
        )
        {
            try
            {
                var db = _context.Db;
                var inputItems = dto.Items ?? new List<DetectBarcodeItem>();
                var inputBarcodes = inputItems.Select(x => x?.Barcode?.Trim()).ToList();
                var validBarcodes = inputBarcodes
                    .Where(b => !string.IsNullOrWhiteSpace(b))
                    .Select(b => b!)
                    .Distinct()
                    .ToList();

                var productByBarcode =
                    new Dictionary<string, List<(string code, string name, string? image)>>();
                if (validBarcodes.Count > 0)
                {
                    var prods = await QueryInChunksAsync<BarcodeProductProjection, string>(
                        validBarcodes,
                        500,
                        async chunk =>
                            await db.Queryable<Product>()
                                .Where(p =>
                                    p.IsDeleted == false
                                    && p.Barcode != null
                                    && chunk.Contains(p.Barcode)
                                )
                                .Select(p => new BarcodeProductProjection
                                {
                                    Barcode = p.Barcode!,
                                    ProductCode = p.ProductCode,
                                    ProductName = p.ProductName,
                                    ProductImage = p.ProductImage,
                                })
                                .ToListAsync()
                    );
                    foreach (var p in prods)
                    {
                        var key = p.Barcode;
                        if (!productByBarcode.TryGetValue(key, out var list))
                        {
                            list = new List<(string code, string name, string? image)>();
                            productByBarcode[key] = list;
                        }
                        if (!string.IsNullOrWhiteSpace(p.ProductCode))
                            list.Add(
                                (p.ProductCode!, p.ProductName ?? string.Empty, p.ProductImage)
                            );
                    }

                    var mprods = await QueryInChunksAsync<MultiCodeProductProjection, string>(
                        validBarcodes,
                        500,
                        async chunk =>
                            await db.Queryable<StoreMultiCodeProduct>()
                                .LeftJoin<Product>((m, p) => m.ProductCode == p.ProductCode)
                                .Where(
                                    (m, p) =>
                                        m.StoreCode == dto.StoreCode
                                        && m.MultiBarcode != null
                                        && chunk.Contains(m.MultiBarcode)
                                        && m.IsDeleted == false
                                )
                                .Select(
                                    (m, p) =>
                                        new MultiCodeProductProjection
                                        {
                                            MultiBarcode = m.MultiBarcode!,
                                            ProductCode = m.ProductCode,
                                            Name = p.ProductName,
                                            Image = p.ProductImage,
                                        }
                                )
                                .ToListAsync()
                    );
                    foreach (var mp in mprods)
                    {
                        var key = mp.MultiBarcode;
                        if (!productByBarcode.TryGetValue(key, out var list))
                        {
                            list = new List<(string code, string name, string? image)>();
                            productByBarcode[key] = list;
                        }
                        if (!string.IsNullOrWhiteSpace(mp.ProductCode))
                            list.Add((mp.ProductCode!, mp.Name ?? string.Empty, mp.Image));
                    }
                }

                var results = new List<BarcodeDetectResult>(inputItems.Count);
                // 预先汇总所有条码对应的产品码，批量查询分店商品编码，避免循环内重复查询
                var allCodes = productByBarcode
                    .Values.SelectMany(list => list.Select(x => x.code))
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!)
                    .Distinct()
                    .ToList();
                var spByCode = new Dictionary<string, List<string>>();
                if (allCodes.Count > 0)
                {
                    var allStoreProductCodes = await QueryInChunksAsync<
                        StoreProductCodeProjection,
                        string
                    >(
                        allCodes,
                        500,
                        async chunk =>
                            await db.Queryable<StoreRetailPrice>()
                                .Where(x =>
                                    x.StoreCode == dto.StoreCode
                                    && x.ProductCode != null
                                    && chunk.Contains(x.ProductCode)
                                    && x.IsDeleted == false
                                )
                                .Select(x => new StoreProductCodeProjection
                                {
                                    ProductCode = x.ProductCode!,
                                    StoreProductCode = x.StoreProductCode
                                })
                                .ToListAsync()
                    );
                    foreach (var row in allStoreProductCodes)
                    {
                        if (
                            string.IsNullOrWhiteSpace(row.ProductCode)
                            || string.IsNullOrWhiteSpace(row.StoreProductCode)
                        )
                            continue;
                        if (!spByCode.TryGetValue(row.ProductCode, out var list))
                        {
                            list = new List<string>();
                            spByCode[row.ProductCode] = list;
                        }
                        if (!list.Contains(row.StoreProductCode!))
                        {
                            list.Add(row.StoreProductCode!);
                        }
                    }
                }
                foreach (var it in inputItems)
                {
                    var barcode = it?.Barcode?.Trim();
                    if (string.IsNullOrWhiteSpace(barcode))
                    {
                        results.Add(
                            new BarcodeDetectResult
                            {
                                Matched = false,
                                MatchCount = 0,
                                OverTwo = false,
                                Error = "条码为空",
                            }
                        );
                        continue;
                    }
                    var pairs = productByBarcode.TryGetValue(barcode, out var list)
                        ? list
                        : new List<(string code, string name, string? image)>();
                    var codes = pairs
                        .Select(x => x.code)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct()
                        .ToList();
                    var names = pairs
                        .Select(x => x.name)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct()
                        .ToList();
                    var firstImg = pairs
                        .Select(x => x.image)
                        .FirstOrDefault(img => !string.IsNullOrWhiteSpace(img));
                    var count = codes.Count;
                    // 关联 StoreRetailPrice 获取分店商品编码（使用预先批量查询的映射）
                    List<string>? storeProductCodes =
                        codes.Count > 0
                            ? codes
                                .SelectMany(c =>
                                    spByCode.TryGetValue(c, out var list)
                                        ? list
                                        : new List<string>()
                                )
                                .Where(sp => !string.IsNullOrWhiteSpace(sp))
                                .Distinct()
                                .ToList()
                            : null;
                    results.Add(
                        new BarcodeDetectResult
                        {
                            Matched = count > 0,
                            MatchCount = count,
                            OverTwo = count > 2,
                            ProductCodes = codes,
                            StoreProductCodes = storeProductCodes,
                            ProductNames = names,
                            FirstProductImage = firstImg,
                        }
                    );
                }

                return ApiResponse<List<BarcodeDetectResult>>.OK(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "条码检测失败");
                return ApiResponse<List<BarcodeDetectResult>>.Error("检测失败", "DETECT_ERROR");
            }
        }

        private async Task<List<T>> QueryInChunksAsync<T, TKey>(
            IReadOnlyList<TKey> keys,
            int chunkSize,
            Func<List<TKey>, Task<List<T>>> fetch
        )
        {
            var result = new List<T>();
            if (keys == null || keys.Count == 0)
                return result;
            var total = keys.Count;
            for (var i = 0; i < total; i += chunkSize)
            {
                var chunk = new List<TKey>(Math.Min(chunkSize, total - i));
                for (var j = i; j < Math.Min(i + chunkSize, total); j++)
                {
                    chunk.Add(keys[j]);
                }
                var part = await fetch(chunk);
                if (part != null && part.Count > 0)
                {
                    result.AddRange(part);
                }
            }
            return result;
        }

        private static bool IsLikelyBarcode(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            var t = s.Trim();
            return t.All(char.IsDigit) && t.Length >= 8 && t.Length <= 13;
        }

        private class BarcodeProductProjection
        {
            public required string Barcode { get; set; }
            public string? ProductCode { get; set; }
            public string? ProductName { get; set; }
            public string? ProductImage { get; set; }
        }

        private class MultiCodeProductProjection
        {
            public required string MultiBarcode { get; set; }
            public string? ProductCode { get; set; }
            public string? Name { get; set; }
            public string? Image { get; set; }
        }

        private class StoreProductCodeProjection
        {
            public required string ProductCode { get; set; }
            public string? StoreProductCode { get; set; }
        }

        private ISugarQueryable<StoreLocalSupplierInvoice, Store, HBLocalSupplier> ApplyText(
            ISugarQueryable<StoreLocalSupplierInvoice, Store, HBLocalSupplier> query,
            string operation,
            string value,
            System.Linq.Expressions.Expression<System.Func<
                StoreLocalSupplierInvoice,
                string?
            >> selector
        )
        {
            var oldParam = selector.Parameters[0];
            var newParam = System.Linq.Expressions.Expression.Parameter(
                typeof(StoreLocalSupplierInvoice),
                "h"
            );
            var member = new ParamReplaceVisitor(oldParam, newParam).Visit(selector.Body);
            return operation switch
            {
                "equals" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<
                        StoreLocalSupplierInvoice,
                        bool
                    >>(
                        System.Linq.Expressions.Expression.Equal(
                            member,
                            System.Linq.Expressions.Expression.Constant(value, typeof(string))
                        ),
                        newParam
                    )
                ),
                "notequal" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<
                        StoreLocalSupplierInvoice,
                        bool
                    >>(
                        System.Linq.Expressions.Expression.NotEqual(
                            member,
                            System.Linq.Expressions.Expression.Constant(value, typeof(string))
                        ),
                        newParam
                    )
                ),
                "contains" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<
                        StoreLocalSupplierInvoice,
                        bool
                    >>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "notcontains" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<
                        StoreLocalSupplierInvoice,
                        bool
                    >>(
                        System.Linq.Expressions.Expression.OrElse(
                            System.Linq.Expressions.Expression.Equal(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Not(
                                System.Linq.Expressions.Expression.Call(
                                    member,
                                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                                    System.Linq.Expressions.Expression.Constant(value)
                                )
                            )
                        ),
                        newParam
                    )
                ),
                "startswith" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<
                        StoreLocalSupplierInvoice,
                        bool
                    >>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                "endswith" => query.Where(
                    System.Linq.Expressions.Expression.Lambda<System.Func<
                        StoreLocalSupplierInvoice,
                        bool
                    >>(
                        System.Linq.Expressions.Expression.AndAlso(
                            System.Linq.Expressions.Expression.NotEqual(
                                member,
                                System.Linq.Expressions.Expression.Constant(null, typeof(string))
                            ),
                            System.Linq.Expressions.Expression.Call(
                                member,
                                typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!,
                                System.Linq.Expressions.Expression.Constant(value)
                            )
                        ),
                        newParam
                    )
                ),
                _ => query,
            };
        }

        private ISugarQueryable<StoreLocalSupplierInvoice, Store, HBLocalSupplier> ApplyNumber(
            ISugarQueryable<StoreLocalSupplierInvoice, Store, HBLocalSupplier> query,
            string? operation,
            System.Linq.Expressions.Expression<System.Func<StoreLocalSupplierInvoice, decimal?>> selector,
            decimal value,
            object? filterTo
        )
        {
            var oldParam = selector.Parameters[0];
            var newParam = System.Linq.Expressions.Expression.Parameter(
                typeof(StoreLocalSupplierInvoice),
                "h"
            );
            var member = new ParamReplaceVisitor(oldParam, newParam).Visit(selector.Body);
            var constantValue = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Constant(value),
                typeof(decimal?)
            );
            System.Linq.Expressions.Expression? condition = operation switch
            {
                "equals" => System.Linq.Expressions.Expression.Equal(member, constantValue),
                "notequal" => System.Linq.Expressions.Expression.NotEqual(member, constantValue),
                "lessthan" => System.Linq.Expressions.Expression.LessThan(member, constantValue),
                "lessthanorequal" => System.Linq.Expressions.Expression.LessThanOrEqual(
                    member,
                    constantValue
                ),
                "greaterthan" => System.Linq.Expressions.Expression.GreaterThan(
                    member,
                    constantValue
                ),
                "greaterthanorequal" => System.Linq.Expressions.Expression.GreaterThanOrEqual(
                    member,
                    constantValue
                ),
                _ => null,
            };

            if (condition != null)
            {
                var lambda = System.Linq.Expressions.Expression.Lambda<
                    System.Func<StoreLocalSupplierInvoice, bool>
                >(condition, newParam);
                query = query.Where(lambda);
            }

            if (filterTo != null && operation == "inrange")
            {
                var constantValueTo = System.Linq.Expressions.Expression.Convert(
                    System.Linq.Expressions.Expression.Constant(
                        System.Convert.ToDecimal(filterTo)
                    ),
                    typeof(decimal?)
                );
                var conditionTo = System.Linq.Expressions.Expression.LessThanOrEqual(
                    member,
                    constantValueTo
                );
                var lambdaTo = System.Linq.Expressions.Expression.Lambda<
                    System.Func<StoreLocalSupplierInvoice, bool>
                >(conditionTo, newParam);
                query = query.Where(lambdaTo);
            }

            return query;
        }

        private sealed class ParamReplaceVisitor : System.Linq.Expressions.ExpressionVisitor
        {
            private readonly System.Linq.Expressions.ParameterExpression _source;
            private readonly System.Linq.Expressions.ParameterExpression _target;

            public ParamReplaceVisitor(
                System.Linq.Expressions.ParameterExpression source,
                System.Linq.Expressions.ParameterExpression target
            )
            {
                _source = source;
                _target = target;
            }

            protected override System.Linq.Expressions.Expression VisitParameter(
                System.Linq.Expressions.ParameterExpression node
            )
            {
                return node == _source ? _target : base.VisitParameter(node);
            }
        }

        private sealed class ProductDetectProjection
        {
            public string ItemNumber { get; set; } = string.Empty;
            public string? ProductCode { get; set; }
            public string? ProductName { get; set; }
            public string? ProductImage { get; set; }
        }

        private sealed class PriceDetectProjection
        {
            public string ProductCode { get; set; } = string.Empty;
            public decimal? PurchasePrice { get; set; }
            public decimal? Retail { get; set; }
            public string? StoreProductCode { get; set; }
        }
    }
}
