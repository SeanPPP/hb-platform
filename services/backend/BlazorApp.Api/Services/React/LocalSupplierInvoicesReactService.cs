using System.Linq;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocalSupplierInvoicesReactService : ILocalSupplierInvoicesReactService
    {
        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IMapper _mapper;
        private readonly ILogger<LocalSupplierInvoicesReactService> _logger;
        private readonly IAutoPricingService _autoPricingService;
        private readonly ILocalSupplierInvoiceHqProductSyncService? _hqProductSyncService;

        public LocalSupplierInvoicesReactService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger<LocalSupplierInvoicesReactService> logger,
            IAutoPricingService autoPricingService,
            ILocalSupplierInvoiceHqProductSyncService? hqProductSyncService = null
        )
        {
            _context = context;
            _hqContext = hqContext;
            _mapper = mapper;
            _logger = logger;
            _autoPricingService = autoPricingService;
            _hqProductSyncService = hqProductSyncService;
        }

        public async Task<GridResponseDto<LocalSupplierInvoiceListDto>> GetGridDataAsync(
            GridRequestDto request
        ) => await GetGridDataAsync(request, null);

        public async Task<GridResponseDto<LocalSupplierInvoiceListDto>> GetGridDataAsync(
            GridRequestDto request,
            List<string>? allowedStoreCodes
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

                if (allowedStoreCodes != null)
                {
                    if (!allowedStoreCodes.Any())
                    {
                        query = query.Where((h, st, sup) => false);
                    }
                    else
                    {
                        query = query.Where((h, st, sup) => allowedStoreCodes.Contains(h.StoreCode));
                    }
                }

                string? productKeyword = null;
                if (request.FilterModel != null && request.FilterModel.Any())
                {
                    foreach (var kv in request.FilterModel)
                    {
                        var col = NormalizeGridColumnId(kv.Key);
                        var f = kv.Value;
                        if (f == null || f.FilterType == null)
                            continue;
                        var type = f.FilterType.ToLower();

                        if (col == "ProductKeyword" && f.Filter != null)
                        {
                            productKeyword = f.Filter?.ToString()?.Trim();
                            continue;
                        }

                        if (type == "text" && f.Filter != null)
                        {
                            var v = f.Filter?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(v))
                                continue;
                            var op = (f.Type ?? "contains").ToLower();
                            switch (col)
                            {
                                case "StoreCode":
                                    query = ApplyText(query, op, v, x => x.StoreCode);
                                    break;
                                case "SupplierCode":
                                    query = ApplyText(query, op, v, x => x.SupplierCode);
                                    break;
                                case "InvoiceNo":
                                    query = ApplyText(query, op, v, x => x.InvoiceNo);
                                    break;
                                case "StoreName":
                                    query = query.Where((h, st, sup) => st.StoreName.Contains(v));
                                    break;
                                case "SupplierName":
                                    query = query.Where((h, st, sup) => sup.Name.Contains(v));
                                    break;
                                case "Remarks":
                                    query = ApplyText(query, op, v, x => x.Remarks);
                                    break;
                                case "CreatedBy":
                                    query = ApplyText(query, op, v, x => x.CreatedBy);
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
                                    case "TotalAmount":
                                        query = ApplyNumber(
                                            query,
                                            op,
                                            x => x.TotalAmount,
                                            numValue,
                                            f.FilterTo
                                        );
                                        break;
                                    case "ReceivedTotalAmount":
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
                        else if (type == "date" && f.Filter != null)
                        {
                            var op = (f.Type ?? "equals").ToLower();
                            switch (col)
                            {
                                case "OrderDate":
                                    query = ApplyDate(query, op, f.Filter, f.FilterTo, x => x.OrderDate);
                                    break;
                                case "InboundDate":
                                    query = ApplyDate(
                                        query,
                                        op,
                                        f.Filter,
                                        f.FilterTo,
                                        x => x.InboundDate
                                    );
                                    break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(productKeyword))
                {
                    var keyword = productKeyword;
                    var matchingInvoiceGuids =
                        await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                            .Where(d =>
                                d.IsDeleted == false
                                && (
                                    d.ItemNumber.Contains(keyword)
                                    || (d.Barcode != null && d.Barcode.Contains(keyword))
                                    || (
                                        d.StoreProductCode != null
                                        && d.StoreProductCode.Contains(keyword)
                                    )
                                    || (d.ProductName != null && d.ProductName.Contains(keyword))
                                )
                            )
                            .Select(d => d.InvoiceGUID)
                            .Distinct()
                            .ToListAsync();

                    if (matchingInvoiceGuids.Any())
                    {
                        var invoiceGuidSet = matchingInvoiceGuids.ToHashSet();
                        query = query.Where((h, st, sup) => invoiceGuidSet.Contains(h.InvoiceGUID));
                    }
                    else
                    {
                        query = query.Where((h, st, sup) => false);
                    }
                }

                if (request.SortModel != null && request.SortModel.Any())
                {
                    var s = request.SortModel.First();
                    var asc = s.Sort?.ToLower() == "asc";
                    query = NormalizeGridColumnId(s.ColId) switch
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
                        "UpdatedAt" => query.OrderBy(
                            (h, st, sup) => h.UpdatedAt,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        _ => query.OrderBy((h, st, sup) => h.OrderDate, OrderByType.Desc),
                    };
                }
                else
                {
                    query = query.OrderBy((h, st, sup) => h.OrderDate, OrderByType.Desc);
                }

                var total = await query.CountAsync();
                var pageSize = ClampGridPageSize(request.PageSize, 20, 20, 50, 100);
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
                                CreatedBy = h.CreatedBy,
                                UpdatedAt = h.UpdatedAt,
                                UpdatedBy = h.UpdatedBy,
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
                                BarcodeStatus = d.BarcodeStatus,
                                BarcodeMatchCount = d.BarcodeMatchCount,
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

        public async Task<GridResponseDto<LocalSupplierInvoiceItemDto>> GetDetailsGridAsync(
            string invoiceGuid,
            GridRequestDto request
        )
        {
            try
            {
                var db = _context.Db;
                var query = db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
                    .Where((d, p) => d.InvoiceGUID == invoiceGuid && d.IsDeleted == false)
                    .OrderBy((d, p) => d.CreatedAt, OrderByType.Desc)
                    .OrderBy((d, p) => d.DetailGUID, OrderByType.Asc);

                var total = await query.CountAsync();
                var pageSize = ClampGridPageSize(request.PageSize, 50, 50, 100, 200);
                var startRow = request.StartRow >= 0 ? request.StartRow : 0;

                var list = await query
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
                                BarcodeStatus = d.BarcodeStatus,
                                BarcodeMatchCount = d.BarcodeMatchCount,
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
                    .Skip(startRow)
                    .Take(pageSize)
                    .ToListAsync();

                return GridResponseDto<LocalSupplierInvoiceItemDto>.OK(list, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取进货单分页明细失败");
                return GridResponseDto<LocalSupplierInvoiceItemDto>.Error("获取失败");
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
                await db.Ado.BeginTranAsync();
                try
                {
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

                    if (affected > 0 && (!string.IsNullOrWhiteSpace(dto.StoreCode) || !string.IsNullOrWhiteSpace(dto.SupplierCode)))
                    {
                        var detailUpdater = db.Updateable<StoreLocalSupplierInvoiceDetails>()
                            .SetColumnsIF(!string.IsNullOrWhiteSpace(dto.StoreCode), x => x.StoreCode == dto.StoreCode)
                            .SetColumnsIF(!string.IsNullOrWhiteSpace(dto.SupplierCode), x => x.SupplierCode == dto.SupplierCode)
                            .Where(x => x.InvoiceGUID == invoiceGuid);

                        await detailUpdater.ExecuteCommandAsync();

                        _logger.LogInformation(
                            "[InvoiceUpdate] 级联更新明细: InvoiceGUID={InvoiceGUID}, StoreCode={StoreCode}, SupplierCode={SupplierCode}",
                            invoiceGuid, dto.StoreCode ?? "(不变)", dto.SupplierCode ?? "(不变)"
                        );
                    }

                    await db.Ado.CommitTranAsync();

                    return affected > 0
                        ? ApiResponse<bool>.OK(true)
                        : ApiResponse<bool>.Error("未更新任何字段", "NO_CHANGE");
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
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
                await db.Ado.BeginTranAsync();
                try
                {
                    var existingInvoice = await db.Queryable<StoreLocalSupplierInvoice>()
                        .Where(x => x.IsDeleted == false)
                        .Where(x => x.StoreCode == dto.StoreCode)
                        .Where(x => x.SupplierCode == dto.SupplierCode)
                        .Where(x => x.InvoiceNo == dto.InvoiceNo)
                        .FirstAsync();

                    if (existingInvoice != null)
                    {
                        await db.Ado.RollbackTranAsync();
                        return DuplicateInvoiceError(dto);
                    }

                    // 主表、明细、总金额更新必须处于同一事务，避免中途失败后留下脏数据。
                    var header = new StoreLocalSupplierInvoice
                    {
                        InvoiceGUID = invoiceGuid,
                        StoreCode = dto.StoreCode,
                        SupplierCode = dto.SupplierCode,
                        InvoiceNo = dto.InvoiceNo,
                        OrderDate = dto.OrderDate,
                        InboundDate = dto.InboundDate,
                        Remarks = dto.Remarks,
                        FlowStatus = 0,
                        InboundStatus = 0,
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

                    var total = detailRows.Sum(x => x.Amount ?? 0);
                    await db.Updateable<StoreLocalSupplierInvoice>()
                        .SetColumns(x => x.TotalAmount == total)
                        .SetColumns(x => x.UpdatedAt == now)
                        .Where(x => x.InvoiceGUID == invoiceGuid)
                        .ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();
                }
                catch (Exception ex) when (IsUniqueConstraintViolation(ex))
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogWarning(
                        ex,
                        "创建进货单命中数据库唯一约束: StoreCode={StoreCode}, SupplierCode={SupplierCode}, InvoiceNo={InvoiceNo}",
                        dto.StoreCode,
                        dto.SupplierCode,
                        dto.InvoiceNo
                    );
                    return DuplicateInvoiceError(dto);
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                return ApiResponse<string>.OK(invoiceGuid);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(
                    ex,
                    "创建进货单命中数据库唯一约束: StoreCode={StoreCode}, SupplierCode={SupplierCode}, InvoiceNo={InvoiceNo}",
                    dto.StoreCode,
                    dto.SupplierCode,
                    dto.InvoiceNo
                );
                return DuplicateInvoiceError(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建进货单失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "创建失败";
                return ApiResponse<string>.Error(msg, "CREATE_ERROR");
            }
        }

        private static ApiResponse<string> DuplicateInvoiceError(CreateInvoiceRequest dto)
        {
            return ApiResponse<string>.Error(
                $"分店【{dto.StoreCode}】、供应商【{dto.SupplierCode}】、单号【{dto.InvoiceNo}】已存在，不能重复创建",
                "DUPLICATE_INVOICE"
            );
        }

        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                // SQL Server 唯一索引/唯一约束冲突分别对应 2601/2627。
                if (current is SqlException { Number: 2601 or 2627 })
                {
                    return true;
                }

                // PostgreSQL 唯一约束冲突 SQLSTATE=23505。
                if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return true;
                }
            }

            return false;
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
                        // 修复 N+1 问题：先批量查询现有记录，分离更新和插入
                        var detailGuids = toUpdate.Select(x => x.DetailGUID).ToList();
                        var existingRecords = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                            .Where(x => detailGuids.Contains(x.DetailGUID) && x.IsDeleted == false)
                            .ToListAsync();
                        var existingDict = existingRecords.ToDictionary(x => x.DetailGUID);

                        // 分离：已存在的需要更新，不存在的需要插入
                        var needUpdate = toUpdate
                            .Where(x => existingDict.ContainsKey(x.DetailGUID))
                            .ToList();
                        var needInsert = toUpdate
                            .Where(x => !existingDict.ContainsKey(x.DetailGUID))
                            .ToList();

                        // 批量处理需要插入的记录（回落插入逻辑）
                        if (needInsert.Count > 0)
                        {
                            var insertList = needInsert
                                .Select(u => new StoreLocalSupplierInvoiceDetails
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
                                })
                                .ToList();
                            inserted += await db.Insertable(insertList).ExecuteCommandAsync();
                        }

                        // 批量处理需要更新的记录：在内存中合并更新值，然后批量更新
                        if (needUpdate.Count > 0)
                        {
                            var mergedList = new List<StoreLocalSupplierInvoiceDetails>(
                                needUpdate.Count
                            );
                            foreach (var u in needUpdate)
                            {
                                var existing = existingDict[u.DetailGUID];
                                // 合并更新：只更新非 null 字段，保持条件更新语义
                                if (u.StoreProductCode != null)
                                    existing.StoreProductCode = u.StoreProductCode;
                                if (u.ProductCode != null)
                                    existing.ProductCode = u.ProductCode;
                                if (u.ItemNumber != null)
                                    existing.ItemNumber = u.ItemNumber;
                                if (u.Barcode != null)
                                    existing.Barcode = u.Barcode;
                                if (u.ProductName != null)
                                    existing.ProductName = u.ProductName;
                                if (u.ProductCategoryGUID != null)
                                    existing.ProductCategoryGUID = u.ProductCategoryGUID;
                                if (u.Quantity != null)
                                    existing.Quantity = u.Quantity;
                                if (u.LastPurchasePrice != null)
                                    existing.LastPurchasePrice = u.LastPurchasePrice;
                                if (u.PurchasePrice != null)
                                    existing.PurchasePrice = u.PurchasePrice;
                                if (u.RetailPrice != null)
                                    existing.RetailPrice = u.RetailPrice;
                                if (u.Amount != null)
                                    existing.Amount = u.Amount;
                                if (u.ActivityType != null)
                                    existing.ActivityType = u.ActivityType;
                                if (u.DiscountRate != null)
                                    existing.DiscountRate = u.DiscountRate;
                                if (u.AutoPricing != null)
                                    existing.AutoPricing = u.AutoPricing;
                                if (u.PricingFloatRate != null)
                                    existing.PricingFloatRate = u.PricingFloatRate;
                                if (u.NewAutoRetailPrice != null)
                                    existing.NewAutoRetailPrice = u.NewAutoRetailPrice;
                                if (u.IsSpecialProduct != null)
                                    existing.IsSpecialProduct = u.IsSpecialProduct;
                                existing.UpdatedAt = now;
                                existing.UpdatedBy = updatedBy;
                                mergedList.Add(existing);
                            }
                            // 使用 SqlSugar 批量更新，按主键 DetailGUID 匹配
                            updated += await db.Updateable(mergedList)
                                .WhereColumns(x => x.DetailGUID)
                                .ExecuteCommandAsync();
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

        public async Task<ApiResponse<BatchResultDto>> BatchUpdateDetailsAsync(
            string invoiceGuid,
            BatchUpdateInvoiceDetailsRequest request,
            string updatedBy
        )
        {
            try
            {
                var editFields = request.EditFields ?? new UpdateToStorePricesFields();
                var hasAnyField =
                    editFields.UpdatePurchasePrice
                    || editFields.UpdateRetailPrice
                    || editFields.UpdateIsAutoPricing
                    || editFields.UpdateIsSpecialProduct
                    || editFields.UpdateDiscountRate;
                if (!hasAnyField)
                    return ApiResponse<BatchResultDto>.Error("请至少选择一个要更新的字段", "VALIDATION_ERROR");

                var fieldValueErrors = ValidateBatchUpdateFieldValues(editFields);
                if (fieldValueErrors.Count > 0)
                {
                    return ApiResponse<BatchResultDto>.Error(
                        string.Join("，", fieldValueErrors),
                        "VALIDATION_ERROR"
                    );
                }

                var detailGuids = (request.Items ?? new List<InvoiceDetailUpsertItemDto>())
                    .Where(x => !string.IsNullOrWhiteSpace(x.DetailGUID))
                    .Select(x => x.DetailGUID!)
                    .Distinct()
                    .ToList();
                if (detailGuids.Count == 0)
                    return ApiResponse<BatchResultDto>.Error("未选择任何明细", "VALIDATION_ERROR");

                var db = _context.Db;
                var now = DateTime.UtcNow;
                var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x =>
                        x.InvoiceGUID == invoiceGuid
                        && detailGuids.Contains(x.DetailGUID)
                        && x.IsDeleted == false
                    )
                    .ToListAsync();
                if (details.Count == 0)
                    return ApiResponse<BatchResultDto>.Error("没有找到要更新的明细", "NOT_FOUND");

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .FirstAsync(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false);
                foreach (var detail in details)
                {
                    // 批量编辑只写入用户勾选的字段，false 和 0 也是有效业务值，不能被空值判断吞掉。
                    if (editFields.UpdatePurchasePrice && editFields.PurchasePrice.HasValue)
                    {
                        detail.PurchasePrice = editFields.PurchasePrice.Value;
                        detail.Amount = (detail.Quantity ?? 0m) * editFields.PurchasePrice.Value;
                    }
                    if (editFields.UpdateRetailPrice && editFields.RetailPrice.HasValue)
                        detail.RetailPrice = editFields.RetailPrice.Value;
                    if (editFields.UpdateIsAutoPricing && editFields.IsAutoPricing.HasValue)
                        detail.AutoPricing = editFields.IsAutoPricing.Value;
                    if (editFields.UpdateIsSpecialProduct && editFields.IsSpecialProduct.HasValue)
                        detail.IsSpecialProduct = editFields.IsSpecialProduct.Value;
                    if (editFields.UpdateDiscountRate && editFields.DiscountRate.HasValue)
                        detail.DiscountRate = editFields.DiscountRate.Value;

                    await ApplyAutoPricingPreviewAsync(detail, header?.SupplierCode, header?.StoreCode);

                    detail.UpdatedAt = now;
                    detail.UpdatedBy = updatedBy;
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    var updated = await db.Updateable(details)
                        .WhereColumns(x => x.DetailGUID)
                        .ExecuteCommandAsync();
                    var total = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                        .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                        .SumAsync(x => x.Amount ?? 0);
                    await db.Updateable<StoreLocalSupplierInvoice>()
                        .SetColumns(x => x.TotalAmount == total)
                        .SetColumns(x => x.UpdatedAt == now)
                        .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                        .ExecuteCommandAsync();
                    await db.Ado.CommitTranAsync();

                    return ApiResponse<BatchResultDto>.OK(
                        new BatchResultDto
                        {
                            Inserted = 0,
                            Updated = updated,
                            Failed = detailGuids.Count - updated,
                        }
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
                _logger.LogError(ex, "批量编辑进货单明细失败");
                return ApiResponse<BatchResultDto>.Error("批量更新失败", "BATCH_UPDATE_ERROR");
            }
        }

        private static List<string> ValidateBatchUpdateFieldValues(UpdateToStorePricesFields editFields)
        {
            var errors = new List<string>();
            // 批量编辑勾选字段必须带值，避免返回成功但实际业务字段没有变化。
            if (editFields.UpdatePurchasePrice && !editFields.PurchasePrice.HasValue)
                errors.Add("进货价不能为空");
            if (editFields.UpdateRetailPrice && !editFields.RetailPrice.HasValue)
                errors.Add("零售价不能为空");
            if (editFields.UpdateIsAutoPricing && !editFields.IsAutoPricing.HasValue)
                errors.Add("自动定价不能为空");
            if (editFields.UpdateIsSpecialProduct && !editFields.IsSpecialProduct.HasValue)
                errors.Add("特殊商品不能为空");
            if (editFields.UpdateDiscountRate && !editFields.DiscountRate.HasValue)
                errors.Add("折扣率不能为空");
            return errors;
        }

        private async Task ApplyAutoPricingPreviewAsync(
            StoreLocalSupplierInvoiceDetails detail,
            string? supplierCode,
            string? storeCode
        )
        {
            if (detail.AutoPricing != true || !detail.PurchasePrice.HasValue || detail.PurchasePrice <= 0)
                return;

            var strategy = await _autoPricingService.FindStrategyForPriceAsync(
                detail.PurchasePrice.Value,
                supplierCode ?? detail.SupplierCode,
                storeCode ?? detail.StoreCode
            );
            detail.PricingFloatRate = _autoPricingService.CalculateRate(
                detail.PurchasePrice.Value,
                strategy
            );
            detail.NewAutoRetailPrice = _autoPricingService.CalculateRetailPrice(
                detail.PurchasePrice.Value,
                strategy
            );
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
                                    StoreProductCode = x.StoreProductCode,
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

        /// <summary>
        /// 并行分块查询，用于提升大数据量查询性能
        /// 多 chunk 使用独立查询连接，避免并发共享 SqlSugarClient 初始化映射缓存时修改集合
        /// 参考：https://www.donet5.com/home/doc?masterId=1&amp;typeId=2349
        /// </summary>
        private async Task<List<T>> QueryInChunksParallelAsync<T, TKey>(
            IReadOnlyList<TKey> keys,
            int chunkSize,
            Func<ISqlSugarClient, List<TKey>, Task<List<T>>> fetch,
            int maxConcurrency = 5
        )
        {
            if (keys == null || keys.Count == 0)
                return new List<T>();

            var chunks = new List<List<TKey>>();
            var total = keys.Count;
            for (var i = 0; i < total; i += chunkSize)
            {
                var chunk = new List<TKey>(Math.Min(chunkSize, total - i));
                for (var j = i; j < Math.Min(i + chunkSize, total); j++)
                {
                    chunk.Add(keys[j]);
                }
                chunks.Add(chunk);
            }

            if (chunks.Count == 0)
                return new List<T>();

            var db = _context.Db;

            if (chunks.Count == 1)
            {
                var singleResult = await fetch(db, chunks[0]);
                return singleResult ?? new List<T>();
            }

            var result = new List<T>[chunks.Count];
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

            var tasks = chunks
                .Select(
                    (chunk, index) =>
                    {
                        return Task.Run(async () =>
                        {
                            await semaphore.WaitAsync();
                            try
                            {
                                // 并发 chunk 不能共享同一个 SqlSugarClient，避免 Queryable 初始化映射缓存时互相修改集合。
                                using var queryDb = _context.CreateConcurrentQueryConnection();
                                var part = await fetch(queryDb, chunk);
                                result[index] = part ?? new List<T>();
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        });
                    }
                )
                .ToArray();

            await Task.WhenAll(tasks);

            var finalResult = new List<T>(result.Sum(r => r?.Count ?? 0));
            foreach (var r in result)
            {
                if (r != null && r.Count > 0)
                    finalResult.AddRange(r);
            }
            return finalResult;
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
            System.Linq.Expressions.Expression<System.Func<
                StoreLocalSupplierInvoice,
                decimal?
            >> selector,
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
                var lambda = System.Linq.Expressions.Expression.Lambda<System.Func<
                    StoreLocalSupplierInvoice,
                    bool
                >>(condition, newParam);
                query = query.Where(lambda);
            }

            if (filterTo != null && operation == "inrange")
            {
                var constantValueTo = System.Linq.Expressions.Expression.Convert(
                    System.Linq.Expressions.Expression.Constant(System.Convert.ToDecimal(filterTo)),
                    typeof(decimal?)
                );
                var conditionTo = System.Linq.Expressions.Expression.LessThanOrEqual(
                    member,
                    constantValueTo
                );
                var lambdaTo = System.Linq.Expressions.Expression.Lambda<System.Func<
                    StoreLocalSupplierInvoice,
                    bool
                >>(conditionTo, newParam);
                query = query.Where(lambdaTo);
            }

            return query;
        }

        private ISugarQueryable<StoreLocalSupplierInvoice, Store, HBLocalSupplier> ApplyDate(
            ISugarQueryable<StoreLocalSupplierInvoice, Store, HBLocalSupplier> query,
            string? operation,
            string? filter,
            string? filterTo,
            System.Linq.Expressions.Expression<System.Func<
                StoreLocalSupplierInvoice,
                DateTime?
            >> selector
        )
        {
            if (!TryParseGridDate(filter, out var value))
            {
                return query;
            }

            var oldParam = selector.Parameters[0];
            var newParam = System.Linq.Expressions.Expression.Parameter(
                typeof(StoreLocalSupplierInvoice),
                "h"
            );
            var member = new ParamReplaceVisitor(oldParam, newParam).Visit(selector.Body);
            var startValue = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Constant(value.Date),
                typeof(DateTime?)
            );
            var endValue = System.Linq.Expressions.Expression.Convert(
                System.Linq.Expressions.Expression.Constant(ToInclusiveEndOfDay(value)),
                typeof(DateTime?)
            );

            System.Linq.Expressions.Expression? condition = operation switch
            {
                "equals" => System.Linq.Expressions.Expression.AndAlso(
                    System.Linq.Expressions.Expression.GreaterThanOrEqual(member, startValue),
                    System.Linq.Expressions.Expression.LessThanOrEqual(member, endValue)
                ),
                "notequal" => System.Linq.Expressions.Expression.OrElse(
                    System.Linq.Expressions.Expression.LessThan(member, startValue),
                    System.Linq.Expressions.Expression.GreaterThan(member, endValue)
                ),
                "lessthan" => System.Linq.Expressions.Expression.LessThan(member, startValue),
                "lessthanorequal" => System.Linq.Expressions.Expression.LessThanOrEqual(
                    member,
                    endValue
                ),
                "greaterthan" => System.Linq.Expressions.Expression.GreaterThan(member, endValue),
                "greaterthanorequal" => System.Linq.Expressions.Expression.GreaterThanOrEqual(
                    member,
                    startValue
                ),
                "inrange" when TryParseGridDate(filterTo, out var toValue) =>
                    System.Linq.Expressions.Expression.AndAlso(
                        System.Linq.Expressions.Expression.GreaterThanOrEqual(member, startValue),
                        System.Linq.Expressions.Expression.LessThanOrEqual(
                            member,
                            System.Linq.Expressions.Expression.Convert(
                                System.Linq.Expressions.Expression.Constant(
                                    ToInclusiveEndOfDay(toValue)
                                ),
                                typeof(DateTime?)
                            )
                        )
                    ),
                _ => null,
            };

            if (condition == null)
            {
                return query;
            }

            var lambda = System.Linq.Expressions.Expression.Lambda<System.Func<
                StoreLocalSupplierInvoice,
                bool
            >>(condition, newParam);
            return query.Where(lambda);
        }

        private static int ClampGridPageSize(int requested, int fallback, params int[] allowed)
        {
            return allowed.Contains(requested) ? requested : fallback;
        }

        private static string NormalizeGridColumnId(string? columnId)
        {
            return columnId?.Trim().ToLowerInvariant() switch
            {
                "storecode" => "StoreCode",
                "suppliercode" => "SupplierCode",
                "invoiceno" => "InvoiceNo",
                "storename" => "StoreName",
                "suppliername" => "SupplierName",
                "remarks" => "Remarks",
                "createdby" => "CreatedBy",
                "productkeyword" => "ProductKeyword",
                "totalamount" => "TotalAmount",
                "receivedtotalamount" => "ReceivedTotalAmount",
                "orderdate" => "OrderDate",
                "inbounddate" => "InboundDate",
                "createdat" => "CreatedAt",
                "updatedat" => "UpdatedAt",
                _ => columnId?.Trim() ?? string.Empty,
            };
        }

        private static bool TryParseGridDate(string? value, out DateTime date)
        {
            return DateTime.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out date
            );
        }

        private static DateTime ToInclusiveEndOfDay(DateTime value)
        {
            return value.Date.AddDays(1).AddTicks(-1);
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

        private sealed class StorePriceUpdatePlan
        {
            public StoreRetailPrice Entity { get; init; } = new();
            public HashSet<string> Columns { get; } = new();
        }

        private static bool IsPositiveValue(decimal? value)
        {
            return value.HasValue && value.Value > 0;
        }

        private static (HashSet<string> Columns, List<string> SkippedFields) ApplyUpdateToStorePriceFields(
            StoreRetailPrice storePrice,
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            var columns = new HashSet<string>();
            var skippedFields = new List<string>();

            if (updateFields.UpdatePurchasePrice)
            {
                // 请求未指定固定值时，使用当前进货单明细里的进货价。
                var purchasePriceToUpdate = updateFields.PurchasePrice ?? detail.PurchasePrice;
                if (IsPositiveValue(purchasePriceToUpdate))
                {
                    storePrice.PurchasePrice = purchasePriceToUpdate.Value;
                    columns.Add(nameof(StoreRetailPrice.PurchasePrice));
                }
                else
                {
                    skippedFields.Add("进货价为空或为0");
                }
            }

            if (updateFields.UpdateRetailPrice)
            {
                // 明细零售价为空时，回退使用商品检测计算出的新自动零售价。
                var retailPriceToUpdate = updateFields.RetailPrice ?? detail.RetailPrice ?? detail.NewAutoRetailPrice;
                if (IsPositiveValue(retailPriceToUpdate))
                {
                    storePrice.StoreRetailPriceValue = retailPriceToUpdate.Value;
                    columns.Add(nameof(StoreRetailPrice.StoreRetailPriceValue));
                }
                else
                {
                    skippedFields.Add("零售价为空或为0");
                }
            }

            if (updateFields.UpdateIsAutoPricing)
            {
                var isAutoPricingToUpdate = updateFields.IsAutoPricing ?? detail.AutoPricing ?? false;
                storePrice.IsAutoPricing = isAutoPricingToUpdate;
                columns.Add(nameof(StoreRetailPrice.IsAutoPricing));
            }

            if (updateFields.UpdateIsSpecialProduct)
            {
                var isSpecialProductToUpdate = updateFields.IsSpecialProduct ?? detail.IsSpecialProduct;
                if (isSpecialProductToUpdate != null)
                {
                    storePrice.IsSpecialProduct = isSpecialProductToUpdate.Value;
                    columns.Add(nameof(StoreRetailPrice.IsSpecialProduct));
                }
                else
                {
                    skippedFields.Add("特殊商品为空");
                }
            }

            if (updateFields.UpdateDiscountRate)
            {
                var discountRateToUpdate = updateFields.DiscountRate ?? detail.DiscountRate;
                if (IsPositiveValue(discountRateToUpdate))
                {
                    storePrice.DiscountRate = discountRateToUpdate.Value;
                    columns.Add(nameof(StoreRetailPrice.DiscountRate));
                }
                else
                {
                    skippedFields.Add("折扣率为空或为0");
                }
            }

            return (columns, skippedFields);
        }

        public async Task<ApiResponse<UpdateToStorePricesResultDto>> UpdateDetailsToStorePricesAsync(
            UpdateToStorePricesRequest dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                // 获取订单明细
                var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(d =>
                        d.InvoiceGUID == dto.InvoiceGuid
                        && dto.DetailGuids.Contains(d.DetailGUID)
                        && d.IsDeleted == false
                    )
                    .ToListAsync();

                if (details == null || details.Count == 0)
                {
                    return ApiResponse<UpdateToStorePricesResultDto>.Error("未找到要更新的明细记录", "NOT_FOUND");
                }

                var totalUpdated = 0;
                var targetStoreCodes = dto.TargetStoreCodes
                    .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
                    .Distinct()
                    .ToList();

                var productCodes = details
                    .Where(d => d.ProductCode != null)
                    .Select(d => d.ProductCode!)
                    .Distinct()
                    .ToList();

                var allPotentialPrices = await db.Queryable<StoreRetailPrice>()
                    .Where(sp =>
                        sp.IsDeleted == false
                        && targetStoreCodes.Contains(sp.StoreCode)
                        && productCodes.Contains(sp.ProductCode!)
                    )
                    .ToListAsync();

                var priceDict = allPotentialPrices
                    .GroupBy(sp => $"{sp.StoreCode}_{sp.ProductCode}")
                    .ToDictionary(
                        group => group.Key,
                        group => group.First()
                    );

                const int updateBatchSize = 500;

                await db.Ado.BeginTranAsync();
                try
                {
                    // 同一分店商品只保留最后一次计算结果，避免重复明细把同一价格记录反复加入大批量更新。
                    var updateMap = new Dictionary<string, StorePriceUpdatePlan>();
                    var insertMap = new Dictionary<string, StoreRetailPrice>();
                    var skipped = 0;
                    var skipMessages = new List<string>();
                    var now = DateTime.Now;

                    foreach (var storeCode in targetStoreCodes)
                    {
                        foreach (var detail in details)
                        {
                            if (string.IsNullOrWhiteSpace(detail.ProductCode))
                            {
                                skipped++;
                                skipMessages.Add($"{detail.DetailGUID}：{storeCode}：商品编码为空，已跳过");
                                continue;
                            }

                            var key = $"{storeCode}_{detail.ProductCode}";

                            if (priceDict.TryGetValue(key, out var storePrice))
                            {
                                // 记录存在，准备更新
                                var (columns, skippedFields) = ApplyUpdateToStorePriceFields(
                                    storePrice,
                                    detail,
                                    dto.UpdateFields
                                );

                                if (columns.Count == 0)
                                {
                                    skipped++;
                                    skipMessages.Add($"{detail.DetailGUID}：{storeCode}：{string.Join("，", skippedFields)}，已跳过");
                                    continue;
                                }

                                storePrice.UpdatedAt = now;
                                storePrice.UpdatedBy = updatedBy;
                                columns.Add(nameof(StoreRetailPrice.UpdatedAt));
                                columns.Add(nameof(StoreRetailPrice.UpdatedBy));

                                if (!updateMap.TryGetValue(key, out var plan))
                                {
                                    plan = new StorePriceUpdatePlan { Entity = storePrice };
                                    updateMap[key] = plan;
                                }
                                foreach (var column in columns)
                                {
                                    plan.Columns.Add(column);
                                }
                                continue;
                            }

                            if (!insertMap.TryGetValue(key, out storePrice))
                            {
                                storePrice = new StoreRetailPrice
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = storeCode,
                                    ProductCode = detail.ProductCode,
                                    StoreProductCode = storeCode + detail.ProductCode,
                                    SupplierCode = detail.SupplierCode,
                                    IsActive = true,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                    CreatedBy = updatedBy,
                                    UpdatedBy = updatedBy,
                                    IsDeleted = false,
                                };
                            }

                            var (insertColumns, insertSkippedFields) = ApplyUpdateToStorePriceFields(
                                storePrice,
                                detail,
                                dto.UpdateFields
                            );

                            if (insertColumns.Count == 0)
                            {
                                skipped++;
                                skipMessages.Add($"{detail.DetailGUID}：{storeCode}：{string.Join("，", insertSkippedFields)}，已跳过");
                                continue;
                            }

                            storePrice.UpdatedAt = now;
                            storePrice.UpdatedBy = updatedBy;
                            insertMap[key] = storePrice;
                        }
                    }

                    // 批量插入缺失的分店价格记录，再批量更新已存在记录。
                    var inserts = insertMap.Values.ToList();
                    if (inserts.Count > 0)
                    {
                        for (var i = 0; i < inserts.Count; i += updateBatchSize)
                        {
                            var batch = inserts.Skip(i).Take(updateBatchSize).ToList();
                            await db.Insertable(batch).ExecuteCommandAsync();
                        }
                        _logger.LogInformation(
                            "批量新建分店价格表成功，共新建 {Count} 条记录",
                            inserts.Count
                        );
                    }

                    var updates = updateMap.Values.ToList();
                    if (updates.Count > 0)
                    {
                        foreach (var group in updates.GroupBy(x => string.Join("|", x.Columns.OrderBy(column => column))))
                        {
                            var updateColumnArray = group.First().Columns.ToArray();
                            var entities = group.Select(x => x.Entity).ToList();
                            for (var i = 0; i < entities.Count; i += updateBatchSize)
                            {
                                var batch = entities.Skip(i).Take(updateBatchSize).ToList();
                                await db.Updateable(batch)
                                    .UpdateColumns(updateColumnArray)
                                    .ExecuteCommandAsync();
                            }
                        }
                        totalUpdated = updates.Count;
                        _logger.LogInformation(
                            "批量更新分店价格表成功，共更新 {Count} 条记录",
                            updates.Count
                        );
                    }

                    if (inserts.Count == 0 && updates.Count == 0)
                    {
                        _logger.LogWarning("没有找到需要更新或新建的分店价格记录");
                    }

                    if (skipped > 0)
                    {
                        _logger.LogInformation(
                            "更新到分店价格跳过 {Count} 条空值或0值记录",
                            skipped
                        );
                    }

                    await db.Ado.CommitTranAsync();

                    var result = new UpdateToStorePricesResultDto
                    {
                        Inserted = inserts.Count,
                        Updated = totalUpdated,
                        Skipped = skipped,
                        Failed = 0,
                        Errors = skipMessages,
                    };

                    return ApiResponse<UpdateToStorePricesResultDto>.OK(result);
                }
                catch (Exception exTran)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(exTran, "更新到分店价格表事务失败");
                    var msg = exTran.InnerException?.Message ?? exTran.Message ?? "更新失败";
                    return ApiResponse<UpdateToStorePricesResultDto>.Error(msg, "UPDATE_ERROR");
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新到分店价格表失败");
                var msg = ex.InnerException?.Message ?? ex.Message ?? "更新失败";
                return ApiResponse<UpdateToStorePricesResultDto>.Error(msg, "UPDATE_ERROR");
            }
        }

        /// <summary>
        /// 检测商品
        /// 根据货号匹配商品信息，根据条码检测条码状态
        /// 检测完成后直接将匹配结果写入订单明细（ProductCode, StoreProductCode, LastPurchasePrice 等字段）
        /// </summary>
        /// <param name="dto">检测请求，包含订单GUID和可选的明细GUID列表</param>
        /// <returns>检测结果，包含每个明细的匹配状态和汇总信息</returns>
        public async Task<ApiResponse<CheckProductsResponseDto>> CheckProductsAsync(
            CheckProductsRequest dto
        )
        {
            try
            {
                var db = _context.Db;

                // 第一步：获取订单头信息
                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == dto.InvoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<CheckProductsResponseDto>.Error("订单不存在", "NOT_FOUND");

                // 第二步：获取订单明细（支持按明细GUID筛选）
                var detailsQuery = db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => x.InvoiceGUID == dto.InvoiceGuid && x.IsDeleted == false);

                if (dto.DetailGuids != null && dto.DetailGuids.Count > 0)
                {
                    detailsQuery = detailsQuery.Where(x => dto.DetailGuids.Contains(x.DetailGUID));
                }

                var details = await detailsQuery.ToListAsync();

                // 第三步：提取货号和条码列表
                var itemNumbers = details
                    .Select(x => x.ItemNumber?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var barcodes = details
                    .Select(x => x.Barcode?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct()
                    .ToList();

                // 第四步：根据货号查询商品（按供应商过滤）
                var productByItemNumber = new Dictionary<string, Product>(
                    StringComparer.OrdinalIgnoreCase
                );
                if (itemNumbers.Count > 0)
                {
                    // 先走精确匹配，避免在 ItemNumber 上套 UPPER 导致索引失效。
                    var products = await QueryInChunksParallelAsync<Product, string>(
                        itemNumbers,
                        200,
                        (db, chunk) => db.Queryable<Product>()
                            .Where(p =>
                                p.LocalSupplierCode == header.SupplierCode
                                && p.ItemNumber != null
                                && chunk.Contains(p.ItemNumber)
                                && p.IsDeleted == false
                            )
                            .ToListAsync()
                    );
                    foreach (var p in products)
                    {
                        if (!string.IsNullOrWhiteSpace(p.ItemNumber))
                            productByItemNumber[p.ItemNumber] = p;
                    }

                    var missingItemNumbers = itemNumbers
                        .Where(itemNumber => !productByItemNumber.ContainsKey(itemNumber))
                        .ToList();

                    if (missingItemNumbers.Count > 0)
                    {
                        // 仅对未命中的货号回退大小写无关匹配，兼容历史数据大小写不一致。
                        var fallbackProducts = await QueryInChunksParallelAsync<Product, string>(
                            missingItemNumbers,
                            200,
                            (db, chunk) =>
                            {
                                var upperChunk = chunk.Select(x => x.ToUpper()).ToList();
                                return db.Queryable<Product>()
                                    .Where(p =>
                                        p.LocalSupplierCode == header.SupplierCode
                                        && p.ItemNumber != null
                                        && upperChunk.Contains(p.ItemNumber.ToUpper())
                                        && p.IsDeleted == false
                                    )
                                    .ToListAsync();
                            }
                        );

                        foreach (var p in fallbackProducts)
                        {
                            if (!string.IsNullOrWhiteSpace(p.ItemNumber))
                                productByItemNumber[p.ItemNumber] = p;
                        }
                    }
                }

                // 第五步：根据商品编码查询分店零售价信息
                var storePricesByCode = new Dictionary<string, StoreRetailPrice>();
                var productCodes = productByItemNumber
                    .Values.Select(p => p.ProductCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                if (productCodes.Count > 0)
                {
                    var storePrices = await QueryInChunksParallelAsync<StoreRetailPrice, string>(
                        productCodes,
                        200,
                        (db, chunk) =>
                            db.Queryable<StoreRetailPrice>()
                                .Where(x =>
                                    x.StoreCode == header.StoreCode
                                    && x.ProductCode != null
                                    && chunk.Contains(x.ProductCode)
                                    && x.IsDeleted == false
                                )
                                .ToListAsync()
                    );
                    foreach (var sp in storePrices)
                    {
                        if (!string.IsNullOrWhiteSpace(sp.ProductCode))
                            storePricesByCode[sp.ProductCode] = sp;
                    }
                }

                // 第六步：根据条码查询商品和条码映射
                var productByBarcode = new Dictionary<string, int>();
                if (barcodes.Count > 0)
                {
                    // 查询商品表中的条码
                    var prods = await QueryInChunksParallelAsync<Product, string>(
                        barcodes,
                        200,
                        (db, chunk) =>
                            db.Queryable<Product>()
                                .Where(p =>
                                    p.IsDeleted == false
                                    && p.Barcode != null
                                    && chunk.Contains(p.Barcode)
                                )
                                .ToListAsync()
                    );
                    foreach (var p in prods)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Barcode))
                        {
                            if (!productByBarcode.ContainsKey(p.Barcode))
                                productByBarcode[p.Barcode] = 0;
                            productByBarcode[p.Barcode]++;
                        }
                    }

                    // 查询多条码映射表
                    var multiCodes = await QueryInChunksParallelAsync<
                        StoreMultiCodeProduct,
                        string
                    >(
                        barcodes,
                        200,
                        (db, chunk) =>
                            db.Queryable<StoreMultiCodeProduct>()
                                .Where(x =>
                                    x.StoreCode == header.StoreCode
                                    && x.MultiBarcode != null
                                    && chunk.Contains(x.MultiBarcode)
                                    && x.IsDeleted == false
                                )
                                .ToListAsync()
                    );
                    foreach (var mc in multiCodes)
                    {
                        if (!string.IsNullOrWhiteSpace(mc.MultiBarcode))
                        {
                            if (!productByBarcode.ContainsKey(mc.MultiBarcode))
                                productByBarcode[mc.MultiBarcode] = 0;
                            productByBarcode[mc.MultiBarcode]++;
                        }
                    }
                }

                // 第七步：预加载定价策略，避免 N+1 查询
                var allStrategies = await _autoPricingService.GetAllActiveStrategiesAsync();
                var supplierStrategies = allStrategies
                    .Where(s =>
                        s.Targets?.Any(t =>
                            t.TargetType == "Supplier" && t.TargetCode == header.SupplierCode
                        ) ?? false
                    )
                    .ToList();
                var storeStrategies = allStrategies
                    .Where(s =>
                        s.Targets?.Any(t =>
                            t.TargetType == "Store" && t.TargetCode == header.StoreCode
                        ) ?? false
                    )
                    .ToList();
                var globalStrategies = allStrategies
                    .Where(s => s.Level == "Global" || (s.Targets == null || s.Targets.Count == 0))
                    .ToList();

                // 第八步：遍历明细，生成检测结果
                var results = new List<ProductCheckResultDto>();
                var summary = new CheckProductsSummaryDto { Total = details.Count };

                foreach (var detail in details)
                {
                    var itemNumber = detail.ItemNumber?.Trim();
                    var barcode = detail.Barcode?.Trim();

                    var result = new ProductCheckResultDto
                    {
                        DetailGuid = detail.DetailGUID,
                        ProductStatus = 0,
                        BarcodeStatus = 0,
                        ExistingProductCount = 0,
                    };

                    // 检测货号是否匹配商品
                    if (!string.IsNullOrWhiteSpace(itemNumber))
                    {
                        if (productByItemNumber.TryGetValue(itemNumber, out var product))
                        {
                            // 货号匹配成功，标记商品存在
                            result.ProductStatus = 1;
                            result.ExistingProductCount = 1;
                            summary.ProductExists++;

                            // 填充商品基本信息
                            result.ProductInfo = new ProductCheckInfoDto();
                            result.ProductInfo.ProductCode = product.ProductCode;
                            result.ProductInfo.ProductName = product.ProductName;
                            result.ProductInfo.ProductImage = product.ProductImage;

                            // 填充分店零售价信息
                            if (
                                !string.IsNullOrWhiteSpace(product.ProductCode)
                                && storePricesByCode.TryGetValue(
                                    product.ProductCode,
                                    out var storePrice
                                )
                            )
                            {
                                result.ProductInfo.PurchasePrice = storePrice.PurchasePrice;
                                result.ProductInfo.RetailPrice = storePrice.StoreRetailPriceValue;
                                result.ProductInfo.StoreProductCode = storePrice.StoreProductCode;
                                // 商品检测不能覆盖用户在进货单明细里手动调整过的自动定价开关。
                                result.AutoPricing = detail.AutoPricing ?? storePrice.IsAutoPricing;
                                result.IsSpecialProduct = storePrice.IsSpecialProduct;
                                result.DiscountRate = storePrice.DiscountRate;
                                result.LastPurchasePrice = storePrice.PurchasePrice;
                            }
                        }
                        else
                        {
                            // 货号未匹配到商品
                            result.ProductStatus = 2;
                            summary.ProductNotExists++;
                        }
                    }

                    // 检测条码是否正常
                    if (!string.IsNullOrWhiteSpace(barcode))
                    {
                        if (productByBarcode.TryGetValue(barcode, out var matchCount))
                        {
                            result.BarcodeMatchCount = matchCount;

                            // 根据商品状态判断条码状态
                            if (result.ProductStatus == 1)
                            {
                                // 商品存在时，条码匹配数>0为正常
                                result.BarcodeStatus = matchCount > 0 ? 1 : 2;
                            }
                            else
                            {
                                // 商品不存在时，条码匹配数=0为正常
                                result.BarcodeStatus = matchCount == 0 ? 1 : 2;
                            }

                            if (result.BarcodeStatus == 1)
                                summary.BarcodeNormal++;
                            else
                                summary.BarcodeAbnormal++;
                        }
                        else
                        {
                            // 条码未匹配到商品
                            if (result.ProductStatus == 1)
                            {
                                result.BarcodeStatus = 2;
                                summary.BarcodeAbnormal++;
                            }
                            else
                            {
                                result.BarcodeStatus = 1;
                                summary.BarcodeNormal++;
                            }
                        }
                    }

                    if (result.ProductStatus != 1)
                    {
                        // 新商品默认开启自动定价；用户已手动设置过的明细仍按明细值保留。
                        result.AutoPricing = detail.AutoPricing ?? true;
                    }

                    // 自动定价预览只依赖明细进货价和自动定价标记，不应因为商品尚未创建而隐藏。
                    var shouldShowAutoPricingPreview =
                        (result.AutoPricing ?? detail.AutoPricing) == true
                        && detail.PurchasePrice.HasValue
                        && detail.PurchasePrice > 0;
                    if (shouldShowAutoPricingPreview)
                    {
                        var purchasePrice = detail.PurchasePrice.Value;
                        var strategy = _autoPricingService.FindBestStrategyForPrice(
                            purchasePrice,
                            supplierStrategies,
                            storeStrategies,
                            globalStrategies
                        );
                        result.PricingFloatRate = _autoPricingService.CalculateRate(
                            purchasePrice,
                            strategy
                        );
                        result.NewAutoRetailPrice = _autoPricingService.CalculateRetailPrice(
                            purchasePrice,
                            strategy
                        );
                    }

                    // 【新增】计算默认操作
                    var defaultAction = 0;
                    if (detail.PurchasePrice.HasValue && detail.PurchasePrice.Value > 0)
                    {
                        bool productExists = result.ProductStatus == 1;
                        bool barcodeNormal = result.BarcodeStatus == 1;

                        if (!productExists && barcodeNormal)
                        {
                            defaultAction = 1; // CreateProduct
                        }
                        else if (productExists && !barcodeNormal)
                        {
                            defaultAction = 5; // AddMultiCode
                        }
                        else if (productExists && barcodeNormal)
                        {
                            defaultAction = 2; // UpdatePurchasePrice
                        }
                        else
                        {
                            defaultAction = 3; // WaitForOperation
                        }
                    }
                    result.DefaultAction = defaultAction;

                    results.Add(result);
                }

                // 第九步：批量更新订单明细，将检测结果写入数据库
                var updateNow = DateTime.UtcNow;
                await db.Ado.BeginTranAsync();
                try
                {
                    var updateItems = results
                        .Select(r => new StoreLocalSupplierInvoiceDetails
                        {
                            DetailGUID = r.DetailGuid,
                            ProductCode = r.ProductInfo?.ProductCode,
                            StoreProductCode = r.ProductInfo?.StoreProductCode,
                            LastPurchasePrice = r.LastPurchasePrice,
                            AutoPricing = r.AutoPricing,
                            IsSpecialProduct = r.IsSpecialProduct,
                            DiscountRate = r.DiscountRate,
                            ExistingProductCount = r.ExistingProductCount,
                            BarcodeStatus = r.BarcodeStatus,
                            BarcodeMatchCount = r.BarcodeMatchCount,
                            PricingFloatRate = r.PricingFloatRate,
                            NewAutoRetailPrice = r.NewAutoRetailPrice,
                            ActivityType = r.DefaultAction,
                            UpdatedAt = updateNow,
                        })
                        .ToList();

                    if (updateItems.Count > 0)
                    {
                        // 检测结果必须覆盖写入，避免旧商品编码、旧条码状态和旧操作类型残留。
                        var updateColumns = new[]
                        {
                            "ProductCode",
                            "StoreProductCode",
                            "LastPurchasePrice",
                            "AutoPricing",
                            "IsSpecialProduct",
                            "DiscountRate",
                            "ExistingProductCount",
                            "BarcodeStatus",
                            "BarcodeMatchCount",
                            "PricingFloatRate",
                            "NewAutoRetailPrice",
                            "ActivityType",
                            "UpdatedAt",
                        };

                        await db.Updateable(updateItems)
                            .UpdateColumns(updateColumns)
                            .ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();
                }
                catch (Exception)
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                return ApiResponse<CheckProductsResponseDto>.OK(
                    new CheckProductsResponseDto { Results = results, Summary = summary }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测商品失败");
                return ApiResponse<CheckProductsResponseDto>.Error("检测失败", "CHECK_ERROR");
            }
        }

        public async Task<ApiResponse<BatchResultDto>> PasteDetailsAsync(
            PasteDetailsRequest dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == dto.InvoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<BatchResultDto>.Error("订单不存在", "NOT_FOUND");

                var now = DateTime.UtcNow;

                if (dto.Mode == "replace")
                {
                    await db.Deleteable<StoreLocalSupplierInvoiceDetails>()
                        .Where(x => x.InvoiceGUID == dto.InvoiceGuid && x.IsDeleted == false)
                        .ExecuteCommandAsync();
                }

                var items = dto.Items ?? new List<PastedDetailItemDto>();
                var validItems = items
                    .Where(i =>
                        !string.IsNullOrWhiteSpace(i.ItemNumber)
                        || !string.IsNullOrWhiteSpace(i.Barcode)
                    )
                    .ToList();

                var detailRows = validItems
                    .Select(i => new StoreLocalSupplierInvoiceDetails
                    {
                        DetailGUID = UuidHelper.GenerateUuid7(),
                        InvoiceGUID = dto.InvoiceGuid,
                        StoreCode = header.StoreCode,
                        SupplierCode = header.SupplierCode,
                        ItemNumber = i.ItemNumber,
                        Barcode = i.Barcode,
                        ProductName = i.ProductName,
                        Quantity = i.Quantity ?? 1,
                        PurchasePrice = i.PurchasePrice,
                        NewAutoRetailPrice = i.NewAutoRetailPrice,
                        RetailPrice = i.RetailPrice,
                        AutoPricing = true,
                        Amount = (i.Quantity ?? 1) * (i.PurchasePrice ?? 0),
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy,
                        IsDeleted = false,
                    })
                    .ToList();

                var inserted = 0;
                if (detailRows.Count > 0)
                {
                    inserted = await db.Insertable(detailRows).ExecuteCommandAsync();
                }

                var total = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => x.InvoiceGUID == dto.InvoiceGuid && x.IsDeleted == false)
                    .SumAsync(x => x.Amount ?? 0);

                await db.Updateable<StoreLocalSupplierInvoice>()
                    .SetColumns(x => x.TotalAmount == total)
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.InvoiceGUID == dto.InvoiceGuid)
                    .ExecuteCommandAsync();

                return ApiResponse<BatchResultDto>.OK(
                    new BatchResultDto
                    {
                        Inserted = inserted,
                        Updated = 0,
                        Failed = 0,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "粘贴数据失败");
                return ApiResponse<BatchResultDto>.Error("粘贴失败", "PASTE_ERROR");
            }
        }

        public async Task<ApiResponse<bool>> UpdateDetailActionAsync(
            string invoiceGuid,
            string detailGuid,
            int action
        )
        {
            try
            {
                if (!IsClientSelectableDetailAction(action))
                    return ApiResponse<bool>.Error("操作类型无效", "VALIDATION_ERROR");

                var db = _context.Db;

                var detail = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x =>
                        x.DetailGUID == detailGuid
                        && x.InvoiceGUID == invoiceGuid
                        && x.IsDeleted == false
                    )
                    .FirstAsync();

                if (detail == null)
                    return ApiResponse<bool>.Error("明细不存在", "NOT_FOUND");
                if (detail.ActivityType == 99)
                    return ApiResponse<bool>.Error("已执行完成的明细不能修改操作类型", "VALIDATION_ERROR");

                var now = DateTime.UtcNow;
                await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                    .SetColumns(x => x.ActivityType == action)
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.DetailGUID == detailGuid)
                    .ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新明细操作类型失败");
                return ApiResponse<bool>.Error("更新失败", "UPDATE_ERROR");
            }
        }

        public async Task<ApiResponse<BatchResultDto>> BatchUpdateDetailActionAsync(
            string invoiceGuid,
            BatchUpdateDetailActionRequest dto
        )
        {
            try
            {
                var db = _context.Db;
                var now = DateTime.UtcNow;

                if (!IsClientSelectableDetailAction(dto.Action))
                    return ApiResponse<BatchResultDto>.Error("操作类型无效", "VALIDATION_ERROR");

                if (dto.DetailGuids == null || dto.DetailGuids.Count == 0)
                {
                    return ApiResponse<BatchResultDto>.Error("未选择任何明细", "VALIDATION_ERROR");
                }

                var detailsToUpdate = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x =>
                        x.InvoiceGUID == invoiceGuid
                        && dto.DetailGuids.Contains(x.DetailGUID)
                        && x.IsDeleted == false
                    )
                    .ToListAsync();

                if (detailsToUpdate.Count == 0)
                {
                    return ApiResponse<BatchResultDto>.Error("没有找到要更新的明细", "NOT_FOUND");
                }
                if (detailsToUpdate.Any(x => x.ActivityType == 99))
                {
                    return ApiResponse<BatchResultDto>.Error(
                        "已执行完成的明细不能修改操作类型",
                        "VALIDATION_ERROR"
                    );
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    var updatedCount = await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                        .SetColumns(x => x.ActivityType == dto.Action)
                        .SetColumns(x => x.UpdatedAt == now)
                        .Where(x =>
                            dto.DetailGuids.Contains(x.DetailGUID)
                            && x.InvoiceGUID == invoiceGuid
                            && x.IsDeleted == false
                        )
                        .ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    return ApiResponse<BatchResultDto>.OK(
                        new BatchResultDto
                        {
                            Updated = updatedCount,
                            Inserted = 0,
                            Failed = detailsToUpdate.Count - updatedCount,
                        }
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
                _logger.LogError(ex, "批量更新操作类型失败");
                return ApiResponse<BatchResultDto>.Error("批量更新失败", "BATCH_UPDATE_ERROR");
            }
        }

        public async Task<ApiResponse<bool>> DeleteDetailsAsync(
            string invoiceGuid,
            List<string> detailGuids,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                if (detailGuids == null || detailGuids.Count == 0)
                    return ApiResponse<bool>.Error("未选择任何明细", "VALIDATION_ERROR");

                var now = DateTime.UtcNow;
                var affected = await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                    .SetColumns(x => x.IsDeleted == true)
                    .SetColumns(x => x.UpdatedAt == now)
                    .SetColumns(x => x.UpdatedBy == updatedBy)
                    .Where(x =>
                        x.InvoiceGUID == invoiceGuid
                        && detailGuids.Contains(x.DetailGUID)
                        && x.IsDeleted == false
                    )
                    .ExecuteCommandAsync();

                var total = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .SumAsync(x => x.Amount ?? 0);

                await db.Updateable<StoreLocalSupplierInvoice>()
                    .SetColumns(x => x.TotalAmount == total)
                    .SetColumns(x => x.UpdatedAt == now)
                    .Where(x => x.InvoiceGUID == invoiceGuid)
                    .ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, $"成功删除 {affected} 条明细");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除明细失败");
                return ApiResponse<bool>.Error("删除失败", "DELETE_ERROR");
            }
        }

        public async Task<
            ApiResponse<GetBarcodeAbnormalDetailsResponse>
        > GetBarcodeAbnormalDetailsAsync(string invoiceGuid)
        {
            try
            {
                var db = _context.Db;

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<GetBarcodeAbnormalDetailsResponse>.Error(
                        "订单不存在",
                        "NOT_FOUND"
                    );

                var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .ToListAsync();

                var barcodes = details
                    .Select(x => x.Barcode?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

                var itemNumbers = details
                    .Select(x => x.ItemNumber?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();

                var productByItemNumber = new Dictionary<string, Product>();
                if (itemNumbers.Count > 0)
                {
                    var products = await QueryInChunksAsync<Product, string>(
                        itemNumbers,
                        1000,
                        async chunk =>
                            await db.Queryable<Product>()
                                .Where(p =>
                                    p.LocalSupplierCode == header.SupplierCode
                                    && p.ItemNumber != null
                                    && chunk.Contains(p.ItemNumber)
                                    && p.IsDeleted == false
                                )
                                .ToListAsync()
                    );
                    foreach (var p in products)
                    {
                        if (!string.IsNullOrWhiteSpace(p.ItemNumber))
                            productByItemNumber[p.ItemNumber] = p;
                    }
                }

                var productByBarcode = new Dictionary<string, List<string>>();
                if (barcodes.Count > 0)
                {
                    var prods = await QueryInChunksAsync<Product, string>(
                        barcodes,
                        1000,
                        async chunk =>
                            await db.Queryable<Product>()
                                .Where(p =>
                                    p.IsDeleted == false
                                    && p.Barcode != null
                                    && chunk.Contains(p.Barcode)
                                )
                                .ToListAsync()
                    );
                    foreach (var p in prods)
                    {
                        if (!string.IsNullOrWhiteSpace(p.Barcode))
                        {
                            if (!productByBarcode.ContainsKey(p.Barcode))
                                productByBarcode[p.Barcode] = new List<string>();
                            productByBarcode[p.Barcode].Add(p.ProductCode);
                        }
                    }

                    var multiCodes = await QueryInChunksAsync<StoreMultiCodeProduct, string>(
                        barcodes,
                        1000,
                        async chunk =>
                            await db.Queryable<StoreMultiCodeProduct>()
                                .Where(x =>
                                    x.StoreCode == header.StoreCode
                                    && x.MultiBarcode != null
                                    && chunk.Contains(x.MultiBarcode)
                                    && x.IsDeleted == false
                                )
                                .ToListAsync()
                    );
                    foreach (var mc in multiCodes)
                    {
                        if (!string.IsNullOrWhiteSpace(mc.MultiBarcode))
                        {
                            if (!productByBarcode.ContainsKey(mc.MultiBarcode))
                                productByBarcode[mc.MultiBarcode] = new List<string>();
                            productByBarcode[mc.MultiBarcode].Add(mc.ProductCode);
                        }
                    }
                }

                var productCodes = new HashSet<string>();
                foreach (var codes in productByBarcode.Values)
                {
                    foreach (var code in codes)
                        productCodes.Add(code);
                }

                var productDetails = new Dictionary<string, Product>();
                if (productCodes.Count > 0)
                {
                    var prods = await QueryInChunksAsync<Product, string>(
                        productCodes.ToList(),
                        1000,
                        async chunk =>
                            await db.Queryable<Product>()
                                .Where(p => chunk.Contains(p.ProductCode) && p.IsDeleted == false)
                                .ToListAsync()
                    );
                    foreach (var p in prods)
                        productDetails[p.ProductCode] = p;
                }

                var supplierCodes = productDetails
                    .Values.Select(p => p.LocalSupplierCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var suppliers = new Dictionary<string, string>();
                if (supplierCodes.Count > 0)
                {
                    var supplierList = await db.Queryable<HBLocalSupplier>()
                        .Where(x =>
                            supplierCodes.Contains(x.LocalSupplierCode) && x.IsDeleted == false
                        )
                        .Select(x => new { x.LocalSupplierCode, x.Name })
                        .ToListAsync();
                    foreach (var s in supplierList)
                        suppliers[s.LocalSupplierCode] = s.Name;
                }

                var result = new GetBarcodeAbnormalDetailsResponse();
                var abnormalDetails = new List<BarcodeAbnormalDetailDto>();

                foreach (var detail in details)
                {
                    var itemNumber = detail.ItemNumber?.Trim();
                    var barcode = detail.Barcode?.Trim();

                    string? matchedProductCode = null;
                    if (
                        !string.IsNullOrWhiteSpace(itemNumber)
                        && productByItemNumber.TryGetValue(itemNumber, out var product)
                    )
                    {
                        matchedProductCode = product.ProductCode;
                    }

                    if (string.IsNullOrWhiteSpace(barcode))
                        continue;

                    if (!productByBarcode.TryGetValue(barcode, out var matchedCodes))
                        continue;

                    var productStatus =
                        !string.IsNullOrWhiteSpace(itemNumber)
                        && productByItemNumber.ContainsKey(itemNumber)
                            ? 1
                            : 2;

                    bool isAbnormal = false;
                    if (productStatus == 1 && !string.IsNullOrWhiteSpace(matchedProductCode))
                    {
                        isAbnormal = !matchedCodes.Contains(matchedProductCode);
                    }

                    if (!isAbnormal)
                        continue;

                    var detailDto = new BarcodeAbnormalDetailDto
                    {
                        DetailGuid = detail.DetailGUID,
                        ItemNumber = detail.ItemNumber ?? string.Empty,
                        Barcode = detail.Barcode ?? string.Empty,
                        ProductName = detail.ProductName ?? string.Empty,
                        ProductStatus = productStatus,
                        MatchedProductCode = matchedProductCode,
                    };

                    foreach (var code in matchedCodes)
                    {
                        if (productDetails.TryGetValue(code, out var matchedProduct))
                        {
                            detailDto.MatchedProducts.Add(
                                new BarcodeAbnormalMatchedProductDto
                                {
                                    ProductCode = matchedProduct.ProductCode,
                                    ProductName = matchedProduct.ProductName ?? string.Empty,
                                    SupplierCode = matchedProduct.LocalSupplierCode ?? string.Empty,
                                    SupplierName = suppliers.GetValueOrDefault(
                                        matchedProduct.LocalSupplierCode ?? string.Empty
                                    ),
                                    ItemNumber = matchedProduct.ItemNumber,
                                    Barcode = matchedProduct.Barcode ?? string.Empty,
                                    ProductImage = matchedProduct.ProductImage,
                                    IsMultiCode = false,
                                    IsBundle = false,
                                }
                            );
                        }
                    }

                    abnormalDetails.Add(detailDto);
                }

                result.Details = abnormalDetails;
                return ApiResponse<GetBarcodeAbnormalDetailsResponse>.OK(result, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取条码异常明细失败");
                return ApiResponse<GetBarcodeAbnormalDetailsResponse>.Error(
                    "获取失败",
                    "GET_ERROR"
                );
            }
        }

        public async Task<ApiResponse<GetProductsByBarcodeResponse>> GetProductsByBarcodeAsync(
            string invoiceGuid,
            string barcode
        )
        {
            try
            {
                var db = _context.Db;

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<GetProductsByBarcodeResponse>.Error(
                        "订单不存在",
                        "NOT_FOUND"
                    );

                if (string.IsNullOrWhiteSpace(barcode))
                    return ApiResponse<GetProductsByBarcodeResponse>.Error(
                        "条码不能为空",
                        "VALIDATION_ERROR"
                    );

                var trimmedBarcode = barcode.Trim();
                var matchedProductCodes = new HashSet<string>();
                var productDetails = new Dictionary<string, Product>();

                var prods = await db.Queryable<Product>()
                    .Where(p =>
                        p.IsDeleted == false && p.Barcode != null && p.Barcode == trimmedBarcode
                    )
                    .ToListAsync();

                foreach (var p in prods)
                {
                    if (!string.IsNullOrWhiteSpace(p.ProductCode))
                    {
                        matchedProductCodes.Add(p.ProductCode);
                        if (!productDetails.ContainsKey(p.ProductCode))
                            productDetails[p.ProductCode] = p;
                    }
                }

                var multiCodes = await db.Queryable<StoreMultiCodeProduct>()
                    .Where(x =>
                        x.StoreCode == header.StoreCode
                        && x.MultiBarcode != null
                        && x.MultiBarcode == trimmedBarcode
                        && x.IsDeleted == false
                    )
                    .ToListAsync();

                foreach (var mc in multiCodes)
                {
                    if (!string.IsNullOrWhiteSpace(mc.ProductCode))
                    {
                        matchedProductCodes.Add(mc.ProductCode);

                        if (!productDetails.ContainsKey(mc.ProductCode))
                        {
                            var product = await db.Queryable<Product>()
                                .Where(p => p.ProductCode == mc.ProductCode && p.IsDeleted == false)
                                .FirstAsync();
                            if (product != null)
                                productDetails[mc.ProductCode] = product;
                        }
                    }
                }

                if (matchedProductCodes.Count > 0 && productDetails.Count == 0)
                {
                    var allProducts = await db.Queryable<Product>()
                        .Where(p =>
                            matchedProductCodes.Contains(p.ProductCode!) && p.IsDeleted == false
                        )
                        .ToListAsync();

                    foreach (var p in allProducts)
                        productDetails[p.ProductCode] = p;
                }

                var supplierCodes = productDetails
                    .Values.Select(p => p.LocalSupplierCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var suppliers = new Dictionary<string, string>();
                if (supplierCodes.Count > 0)
                {
                    var supplierList = await db.Queryable<HBLocalSupplier>()
                        .Where(x =>
                            supplierCodes.Contains(x.LocalSupplierCode) && x.IsDeleted == false
                        )
                        .Select(x => new { x.LocalSupplierCode, x.Name })
                        .ToListAsync();
                    foreach (var s in supplierList)
                        suppliers[s.LocalSupplierCode] = s.Name;
                }

                var result = new GetProductsByBarcodeResponse
                {
                    Barcode = trimmedBarcode,
                    MatchedProducts = productDetails
                        .Values.Select(p => new BarcodeAbnormalMatchedProductDto
                        {
                            ProductCode = p.ProductCode,
                            ProductName = p.ProductName ?? string.Empty,
                            SupplierCode = p.LocalSupplierCode ?? string.Empty,
                            SupplierName = suppliers.GetValueOrDefault(
                                p.LocalSupplierCode ?? string.Empty
                            ),
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode ?? string.Empty,
                            ProductImage = p.ProductImage,
                            IsMultiCode = multiCodes.Any(mc => mc.ProductCode == p.ProductCode),
                            IsBundle = false,
                        })
                        .ToList(),
                };

                return ApiResponse<GetProductsByBarcodeResponse>.OK(result, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按条码查询匹配商品失败");
                return ApiResponse<GetProductsByBarcodeResponse>.Error("获取失败", "GET_ERROR");
            }
        }

        public async Task<
            ApiResponse<GetProductsByProductCodeResponse>
        > GetProductsByProductCodeAsync(string invoiceGuid, string productCode)
        {
            try
            {
                var db = _context.Db;

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<GetProductsByProductCodeResponse>.Error(
                        "订单不存在",
                        "NOT_FOUND"
                    );

                if (string.IsNullOrWhiteSpace(productCode))
                    return ApiResponse<GetProductsByProductCodeResponse>.Error(
                        "商品编码不能为空",
                        "VALIDATION_ERROR"
                    );

                var trimmedProductCode = productCode.Trim();
                var productDetails = new Dictionary<string, Product>();

                var prods = await db.Queryable<Product>()
                    .Where(p =>
                        p.IsDeleted == false
                        && p.ProductCode != null
                        && p.ProductCode == trimmedProductCode
                    )
                    .ToListAsync();

                foreach (var p in prods)
                {
                    if (!string.IsNullOrWhiteSpace(p.ProductCode))
                    {
                        if (!productDetails.ContainsKey(p.ProductCode))
                            productDetails[p.ProductCode] = p;
                    }
                }

                var supplierCodes = productDetails
                    .Values.Select(p => p.LocalSupplierCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var suppliers = new Dictionary<string, string>();
                if (supplierCodes.Count > 0)
                {
                    var supplierList = await db.Queryable<HBLocalSupplier>()
                        .Where(x =>
                            supplierCodes.Contains(x.LocalSupplierCode) && x.IsDeleted == false
                        )
                        .Select(x => new { x.LocalSupplierCode, x.Name })
                        .ToListAsync();
                    foreach (var s in supplierList)
                        suppliers[s.LocalSupplierCode] = s.Name;
                }

                var result = new GetProductsByProductCodeResponse
                {
                    ProductCode = trimmedProductCode,
                    MatchedProducts = productDetails
                        .Values.Select(p => new BarcodeAbnormalMatchedProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ProductName = p.ProductName ?? string.Empty,
                            SupplierCode = p.LocalSupplierCode ?? string.Empty,
                            SupplierName = suppliers.GetValueOrDefault(
                                p.LocalSupplierCode ?? string.Empty
                            ),
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode ?? string.Empty,
                            ProductImage = p.ProductImage,
                            IsMultiCode = false,
                            IsBundle = false,
                            ProductType = p.ProductType,
                        })
                        .ToList(),
                };

                return ApiResponse<GetProductsByProductCodeResponse>.OK(result, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按商品编码查询匹配商品失败");
                return ApiResponse<GetProductsByProductCodeResponse>.Error("获取失败", "GET_ERROR");
            }
        }

        public async Task<ApiResponse<InvoiceNoCheckResult>> CheckInvoiceNoExistsAsync(
            string storeCode,
            string supplierCode,
            string invoiceNo
        )
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(storeCode)
                    || string.IsNullOrWhiteSpace(supplierCode)
                    || string.IsNullOrWhiteSpace(invoiceNo)
                )
                    return ApiResponse<InvoiceNoCheckResult>.OK(
                        new InvoiceNoCheckResult { Exists = false }
                    );

                var db = _context.Db;
                var existing = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x =>
                        x.StoreCode == storeCode.Trim()
                        && x.SupplierCode == supplierCode
                        && x.InvoiceNo == invoiceNo.Trim()
                        && x.IsDeleted == false
                    )
                    .Select(x => new { x.InvoiceNo, x.CreatedAt })
                    .FirstAsync();

                if (existing != null)
                {
                    return ApiResponse<InvoiceNoCheckResult>.OK(
                        new InvoiceNoCheckResult
                        {
                            Exists = true,
                            ExistingInvoiceNo = existing.InvoiceNo,
                            ExistingCreatedAt = existing.CreatedAt,
                        }
                    );
                }

                return ApiResponse<InvoiceNoCheckResult>.OK(
                    new InvoiceNoCheckResult { Exists = false }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查随货单号是否存在失败");
                return ApiResponse<InvoiceNoCheckResult>.Error("检查失败", "CHECK_ERROR");
            }
        }

        private static bool IsClientSelectableDetailAction(int action)
        {
            return action >= (int)DetailAction.None && action <= (int)DetailAction.AddMultiCode;
        }

        public async Task<ApiResponse<BatchExecuteActionsResultDto>> BatchExecuteActionsAsync(
            string invoiceGuid,
            List<string> detailGuids,
            string userName
        )
        {
            try
            {
                var result = new BatchExecuteActionsResultDto();
                var db = _context.Db;

                var selectedDetailGuids = detailGuids?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList() ?? new List<string>();
                if (selectedDetailGuids.Count == 0)
                {
                    return ApiResponse<BatchExecuteActionsResultDto>.Error(
                        "请选择要执行的明细",
                        "VALIDATION_ERROR"
                    );
                }

                // 1. 获取进货单头信息
                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                {
                    return ApiResponse<BatchExecuteActionsResultDto>.Error(
                        "进货单不存在",
                        "NOT_FOUND"
                    );
                }

                // 2. 获取明细列表
                var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x =>
                        x.InvoiceGUID == invoiceGuid
                        && selectedDetailGuids.Contains(x.DetailGUID)
                        && x.IsDeleted == false
                    )
                    .ToListAsync();

                if (details.Count != selectedDetailGuids.Count)
                {
                    return ApiResponse<BatchExecuteActionsResultDto>.Error(
                        "部分明细不存在或不属于当前进货单",
                        "VALIDATION_ERROR"
                    );
                }

                // 3. 获取已有商品的 ItemNumber（用于判断是否需要更新货号）
                var productCodes = details
                    .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                    .Select(x => x.ProductCode!)
                    .Distinct()
                    .ToList();

                var productItemNumbers = new Dictionary<string, string>();
                if (productCodes.Count > 0)
                {
                    var products = await db.Queryable<Product>()
                        .Where(x => productCodes.Contains(x.ProductCode!) && x.IsDeleted == false)
                        .Select(x => new { x.ProductCode, x.ItemNumber })
                        .ToListAsync();

                    foreach (var p in products)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(p.ProductCode)
                            && !string.IsNullOrWhiteSpace(p.ItemNumber)
                        )
                        {
                            productItemNumbers[p.ProductCode] = p.ItemNumber;
                        }
                    }
                }

                var successfulDetailGuids = new List<string>();
                await db.Ado.BeginTranAsync();
                try
                {
                    var validationErrors = await ValidateBatchExecuteDetailsAsync(details, header);
                    if (validationErrors.Count > 0)
                    {
                        result.Failed = validationErrors.Count;
                        result.Errors.AddRange(validationErrors);
                        await db.Ado.RollbackTranAsync();
                        return ApiResponse<BatchExecuteActionsResultDto>.Error(
                            "批量执行校验失败",
                            "VALIDATION_ERROR",
                            result
                        );
                    }

                    // 4. 按用户保存的 ActivityType 分组，不能在执行阶段重新推导覆盖用户选择。
                    var groupedDetails =
                        new Dictionary<DetailAction, List<StoreLocalSupplierInvoiceDetails>>();

                    foreach (var detail in details)
                    {
                        var action = GetSavedActionForDetail(detail);

                        if (!groupedDetails.ContainsKey(action))
                        {
                            groupedDetails[action] = new List<StoreLocalSupplierInvoiceDetails>();
                        }
                        groupedDetails[action].Add(detail);
                    }

                    // 5. 批量处理每个 action 组
                    // ========== 创建商品 ==========
                    if (
                        groupedDetails.TryGetValue(DetailAction.CreateProduct, out var createList)
                        && createList.Count > 0
                    )
                    {
                        var createResult = await BatchCreateProductsAsync(createList, header, userName);
                        result.CreatedProducts = createResult.SuccessCount;
                        result.Failed += createResult.FailedCount;
                        result.Errors.AddRange(createResult.Errors);
                        result.Skipped += createResult.SkippedCount;
                        successfulDetailGuids.AddRange(createResult.SuccessfulDetailGuids);
                    }

                    // ========== 更新进货价 ==========
                    if (
                        groupedDetails.TryGetValue(DetailAction.UpdatePurchasePrice, out var priceList)
                        && priceList.Count > 0
                    )
                    {
                        var priceResult = await BatchUpdatePurchasePriceAsync(priceList, userName);
                        result.UpdatedPurchasePrices = priceResult.SuccessCount;
                        result.Failed += priceResult.FailedCount;
                        result.Errors.AddRange(priceResult.Errors);
                        result.Skipped += priceResult.SkippedCount;
                        successfulDetailGuids.AddRange(priceResult.SuccessfulDetailGuids);
                    }

                    // ========== 更新货号 ==========
                    if (
                        groupedDetails.TryGetValue(DetailAction.UpdateItemNumber, out var itemList)
                        && itemList.Count > 0
                    )
                    {
                        var itemResult = await BatchUpdateItemNumberAsync(
                            itemList,
                            productItemNumbers,
                            userName
                        );
                        result.UpdatedItemNumbers = itemResult.SuccessCount;
                        result.Failed += itemResult.FailedCount;
                        result.Errors.AddRange(itemResult.Errors);
                        result.Skipped += itemResult.SkippedCount;
                        successfulDetailGuids.AddRange(itemResult.SuccessfulDetailGuids);
                    }

                    // ========== 添加多码 ==========
                    if (
                        groupedDetails.TryGetValue(DetailAction.AddMultiCode, out var multiCodeList)
                        && multiCodeList.Count > 0
                    )
                    {
                        var multiCodeResult = await BatchAddMultiCodesAsync(
                            multiCodeList,
                            header,
                            userName
                        );
                        result.AddedMultiCodes = multiCodeResult.SuccessCount;
                        result.Failed += multiCodeResult.FailedCount;
                        result.Errors.AddRange(multiCodeResult.Errors);
                        result.Skipped += multiCodeResult.SkippedCount;
                        successfulDetailGuids.AddRange(multiCodeResult.SuccessfulDetailGuids);
                    }

                    if (result.Failed > 0)
                    {
                        await db.Ado.RollbackTranAsync();
                        return ApiResponse<BatchExecuteActionsResultDto>.Error(
                            "批量执行失败，已回滚",
                            "BATCH_EXECUTE_ERROR",
                            result
                        );
                    }

                    // ========== 无操作/等待操作/已完成 ==========
                    if (groupedDetails.TryGetValue(DetailAction.None, out var noneList))
                    {
                        result.Skipped += noneList.Count;
                    }
                    if (groupedDetails.TryGetValue(DetailAction.WaitForOperation, out var waitList))
                    {
                        result.Skipped += waitList.Count;
                    }

                    // 6. 只把真正执行成功的明细标记为完成，保留跳过项和待处理项。
                    if (successfulDetailGuids.Count > 0)
                    {
                        await BatchUpdateDetailActivityTypeAsync(successfulDetailGuids, userName);
                    }

                    await db.Ado.CommitTranAsync();
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                return ApiResponse<BatchExecuteActionsResultDto>.OK(result, "批量执行完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量执行操作失败");
                return ApiResponse<BatchExecuteActionsResultDto>.Error(
                    "批量执行失败",
                    "BATCH_EXECUTE_ERROR"
                );
            }
        }

        private async Task<List<string>> ValidateBatchExecuteDetailsAsync(
            List<StoreLocalSupplierInvoiceDetails> details,
            StoreLocalSupplierInvoice header
        )
        {
            var db = _context.Db;
            var errors = new List<string>();
            var createItemNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var createBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var multiCodeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var detail in details)
            {
                if (!detail.ActivityType.HasValue)
                {
                    errors.Add($"明细 {detail.DetailGUID} 未设置操作类型，请先执行商品检测或手动设置操作类型");
                    continue;
                }

                var actionValue = detail.ActivityType.Value;
                if (actionValue == 99)
                    continue;

                if (!Enum.IsDefined(typeof(DetailAction), actionValue))
                {
                    errors.Add($"明细 {detail.DetailGUID} 操作类型无效：{actionValue}");
                    continue;
                }

                var action = (DetailAction)actionValue;
                switch (action)
                {
                    case DetailAction.None:
                    case DetailAction.WaitForOperation:
                        break;

                    case DetailAction.CreateProduct:
                        if (string.IsNullOrWhiteSpace(detail.ItemNumber))
                            errors.Add($"明细 {detail.DetailGUID} 新建商品失败：货号不能为空");
                        if (string.IsNullOrWhiteSpace(detail.Barcode))
                            errors.Add($"明细 {detail.DetailGUID} 新建商品失败：条码不能为空");
                        if (detail.PurchasePrice == null || detail.PurchasePrice <= 0)
                            errors.Add($"明细 {detail.DetailGUID} 新建商品失败：进货价必须大于0");
                        if (
                            !string.IsNullOrWhiteSpace(detail.ItemNumber)
                            && !createItemNumbers.Add(detail.ItemNumber.Trim())
                        )
                            errors.Add($"明细 {detail.DetailGUID} 新建商品失败：本次执行内货号重复");
                        if (
                            !string.IsNullOrWhiteSpace(detail.Barcode)
                            && !createBarcodes.Add(detail.Barcode.Trim())
                        )
                            errors.Add($"明细 {detail.DetailGUID} 新建商品失败：本次执行内条码重复");
                        if (
                            !string.IsNullOrWhiteSpace(detail.ItemNumber)
                            || !string.IsNullOrWhiteSpace(detail.Barcode)
                        )
                        {
                            var normalizedItemNumber = NormalizeCaseInsensitiveValue(
                                detail.ItemNumber
                            );
                            var normalizedBarcode = NormalizeCaseInsensitiveValue(detail.Barcode);
                            var duplicateProduct = await db.Queryable<Product>()
                                .AnyAsync(p =>
                                    p.IsDeleted == false
                                    && p.LocalSupplierCode == header.SupplierCode
                                    && (
                                        (
                                            normalizedItemNumber != null
                                            && SqlFunc.ToUpper(p.ItemNumber) == normalizedItemNumber
                                        )
                                        || (
                                            normalizedBarcode != null
                                            && SqlFunc.ToUpper(p.Barcode) == normalizedBarcode
                                        )
                                    )
                                );
                            if (duplicateProduct)
                                errors.Add($"明细 {detail.DetailGUID} 新建商品失败：货号或条码已存在");
                        }
                        break;

                    case DetailAction.UpdatePurchasePrice:
                        if (string.IsNullOrWhiteSpace(detail.ProductCode))
                        {
                            errors.Add($"明细 {detail.DetailGUID} 更新进货价失败：未找到商品编码");
                            break;
                        }
                        if (!await ProductExistsByCodeAsync(detail.ProductCode))
                            errors.Add($"明细 {detail.DetailGUID} 更新进货价失败：商品不存在");
                        if (
                            string.IsNullOrWhiteSpace(detail.StoreCode)
                            || !await StorePriceExistsAsync(detail.StoreCode, detail.ProductCode)
                        )
                            errors.Add($"明细 {detail.DetailGUID} 更新进货价失败：分店价格不存在");
                        break;

                    case DetailAction.UpdateItemNumber:
                        if (string.IsNullOrWhiteSpace(detail.ItemNumber))
                            errors.Add($"明细 {detail.DetailGUID} 更新货号失败：新货号不能为空");
                        if (string.IsNullOrWhiteSpace(detail.ProductCode))
                        {
                            errors.Add($"明细 {detail.DetailGUID} 更新货号失败：未找到商品编码");
                            break;
                        }
                        if (!await ProductExistsByCodeAsync(detail.ProductCode))
                            errors.Add($"明细 {detail.DetailGUID} 更新货号失败：商品不存在");
                        break;

                    case DetailAction.AddMultiCode:
                        if (string.IsNullOrWhiteSpace(detail.ProductCode))
                        {
                            errors.Add($"明细 {detail.DetailGUID} 添加多码失败：未找到商品编码");
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(detail.Barcode))
                            errors.Add($"明细 {detail.DetailGUID} 添加多码失败：条码不能为空");
                        if (!await ProductExistsByCodeAsync(detail.ProductCode))
                            errors.Add($"明细 {detail.DetailGUID} 添加多码失败：商品不存在");
                        if (!string.IsNullOrWhiteSpace(detail.Barcode))
                        {
                            var normalizedBarcode = NormalizeCaseInsensitiveValue(detail.Barcode);
                            var key = $"{detail.ProductCode}|{normalizedBarcode}";
                            if (!multiCodeKeys.Add(key))
                                errors.Add($"明细 {detail.DetailGUID} 添加多码失败：本次执行内多码重复");

                            var duplicateMultiCode = await db.Queryable<StoreMultiCodeProduct>()
                                .AnyAsync(x =>
                                    x.ProductCode == detail.ProductCode
                                    && SqlFunc.ToUpper(x.MultiBarcode) == normalizedBarcode
                                    && x.IsDeleted == false
                                );
                            if (duplicateMultiCode)
                                errors.Add($"明细 {detail.DetailGUID} 添加多码失败：分店多码已存在");

                            var duplicateProductSetCode = await db.Queryable<ProductSetCode>()
                                .AnyAsync(x =>
                                    x.ProductCode == detail.ProductCode
                                    && SqlFunc.ToUpper(x.SetBarcode) == normalizedBarcode
                                    && x.IsDeleted == false
                                );
                            if (duplicateProductSetCode)
                                errors.Add($"明细 {detail.DetailGUID} 添加多码失败：商品多码关系已存在");
                        }
                        break;
                }
            }

            return errors;
        }

        private static string? NormalizeCaseInsensitiveValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToUpperInvariant();
        }

        private async Task<bool> ProductExistsByCodeAsync(string productCode)
        {
            return await _context.Db.Queryable<Product>()
                .AnyAsync(p => p.ProductCode == productCode && p.IsDeleted == false);
        }

        private async Task<bool> StorePriceExistsAsync(string storeCode, string productCode)
        {
            return await _context.Db.Queryable<StoreRetailPrice>()
                .AnyAsync(sp =>
                    sp.StoreCode == storeCode
                    && sp.ProductCode == productCode
                    && sp.IsDeleted == false
                );
        }

        private DetailAction GetSavedActionForDetail(StoreLocalSupplierInvoiceDetails detail)
        {
            if (detail.ActivityType == 99)
                return DetailAction.None;
            return (DetailAction)detail.ActivityType!.Value;
        }

        private DetailAction GetActionForDetail(
            StoreLocalSupplierInvoiceDetails detail,
            Dictionary<string, string> productItemNumbers
        )
        {
            // 记录诊断信息
            _logger.LogDebug(
                "GetActionForDetail: ProductCode={ProductCode}, ExistingProductCount={ExistingProductCount}, BarcodeStatus={BarcodeStatus}",
                detail.ProductCode,
                detail.ExistingProductCount,
                detail.BarcodeStatus
            );

            // 先检查进货价
            if (detail.PurchasePrice == null || detail.PurchasePrice <= 0)
            {
                return DetailAction.None;
            }

            // 商品存在判断
            bool productExists = detail.ExistingProductCount > 0;
            bool barcodeNormal = detail.BarcodeStatus == 1;

            // 商品不存在，条码正常 -> 创建商品
            if (!productExists && barcodeNormal)
            {
                return DetailAction.CreateProduct;
            }

            // 商品存在，条码异常 -> 添加多码
            if (productExists && !barcodeNormal)
            {
                return DetailAction.AddMultiCode;
            }

            // 商品存在，条码正常 -> 更新进货价
            if (productExists && barcodeNormal)
            {
                // 判断是否需要更新货号（货号不一致）
                if (
                    !string.IsNullOrWhiteSpace(detail.ItemNumber)
                    && !string.IsNullOrWhiteSpace(detail.ProductCode)
                    && productItemNumbers.TryGetValue(
                        detail.ProductCode,
                        out var existingItemNumber
                    )
                    && !string.IsNullOrWhiteSpace(existingItemNumber)
                    && existingItemNumber != detail.ItemNumber
                )
                {
                    return DetailAction.UpdateItemNumber;
                }

                return DetailAction.UpdatePurchasePrice;
            }

            // 其他情况 -> 等待操作
            return DetailAction.WaitForOperation;
        }

        private async Task<BatchOperationResult> BatchCreateProductsAsync(
            List<StoreLocalSupplierInvoiceDetails> details,
            StoreLocalSupplierInvoice header,
            string userName
        )
        {
            var result = new BatchOperationResult();
            var db = _context.Db;
            var now = DateTime.UtcNow;

            // 获取所有激活分店
            var activeStores = await db.Queryable<Store>()
                .Where(s => s.IsActive == true)
                .Select(s => s.StoreCode)
                .ToListAsync();

            var productsToCreate = new List<Product>();
            var storePricesToCreate = new List<StoreRetailPrice>();
            var pricingStrategyCache = new Dictionary<decimal, PricingStrategy?>();

            foreach (var detail in details)
            {
                // 验证必填字段
                if (string.IsNullOrWhiteSpace(detail.ItemNumber))
                {
                    result.Errors.Add($"创建商品失败：货号不能为空");
                    result.FailedCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(detail.Barcode))
                {
                    result.Errors.Add($"创建商品失败：条码不能为空");
                    result.FailedCount++;
                    continue;
                }

                if (detail.PurchasePrice == null || detail.PurchasePrice <= 0)
                {
                    result.Errors.Add($"创建商品失败：进货价必须大于0");
                    result.FailedCount++;
                    continue;
                }

                // 生成商品 UUID
                var productUUID = UuidHelper.GenerateUuid7();

                // 计算零售价：根据自动定价标志决定使用哪个零售价
                decimal calculatedRetailPrice = 0;
                if (detail.AutoPricing == true)
                {
                    // 自动定价开启时，使用新自动零售价
                    if (detail.NewAutoRetailPrice.HasValue && detail.NewAutoRetailPrice > 0)
                    {
                        calculatedRetailPrice = detail.NewAutoRetailPrice.Value;
                    }
                    else if (detail.PurchasePrice.HasValue && detail.PurchasePrice > 0)
                    {
                        // 同一批常有大量相同进货价，按价格缓存策略，避免创建商品时重复查策略。
                        if (!pricingStrategyCache.TryGetValue(detail.PurchasePrice.Value, out var strategy))
                        {
                            strategy = await _autoPricingService.FindStrategyForPriceAsync(
                                detail.PurchasePrice.Value,
                                header.SupplierCode,
                                null
                            );
                            pricingStrategyCache[detail.PurchasePrice.Value] = strategy;
                        }

                        calculatedRetailPrice = _autoPricingService.CalculateRetailPrice(
                            detail.PurchasePrice.Value,
                            strategy
                        );
                    }
                    else
                    {
                        calculatedRetailPrice = (detail.PurchasePrice ?? 0) * 2.5m; // 默认加价 250%
                    }
                }
                else
                {
                    // 自动定价关闭时，使用指定零售价
                    if (detail.RetailPrice.HasValue && detail.RetailPrice > 0)
                    {
                        calculatedRetailPrice = detail.RetailPrice.Value;
                    }
                    else
                    {
                        calculatedRetailPrice = (detail.PurchasePrice ?? 0) * 2.5m; // 默认加价 250%
                    }
                }

                var product = new Product
                {
                    UUID = productUUID,
                    ProductCode = productUUID, // ProductCode 使用 UUID
                    ItemNumber = detail.ItemNumber,
                    Barcode = detail.Barcode,
                    ProductName = detail.ProductName ?? string.Empty,
                    LocalSupplierCode = header.SupplierCode,
                    PurchasePrice = detail.PurchasePrice ?? 0,
                    RetailPrice = calculatedRetailPrice,
                    IsAutoPricing = detail.AutoPricing ?? true,
                    IsSpecialProduct = detail.IsSpecialProduct ?? false,
                    ProductImage = detail.ProductImage,
                    ProductType = 0,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = userName,
                    UpdatedBy = userName,
                };
                productsToCreate.Add(product);

                // 为所有激活分店创建 StoreRetailPrice
                foreach (var storeCode in activeStores)
                {
                    var storePrice = new StoreRetailPrice
                    {
                        UUID = UuidHelper.GenerateUuid7(),
                        StoreCode = storeCode,
                        ProductCode = productUUID,
                        StoreProductCode = storeCode + productUUID,
                        SupplierCode = header.SupplierCode,
                        PurchasePrice = detail.PurchasePrice ?? 0,
                        StoreRetailPriceValue = calculatedRetailPrice,
                        IsAutoPricing = detail.AutoPricing ?? true,
                        IsSpecialProduct = detail.IsSpecialProduct ?? false,
                        DiscountRate = detail.DiscountRate,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = userName,
                        UpdatedBy = userName,
                    };
                    storePricesToCreate.Add(storePrice);
                }

                result.SuccessCount++;
                result.SuccessfulDetailGuids.Add(detail.DetailGUID);
            }

            if (productsToCreate.Count > 0)
            {
                await db.Fastest<Product>().BulkCopyAsync(productsToCreate);
            }

            if (storePricesToCreate.Count > 0)
            {
                await db.Fastest<StoreRetailPrice>().BulkCopyAsync(storePricesToCreate);
            }

            return result;
        }

        private async Task<BatchOperationResult> BatchUpdatePurchasePriceAsync(
            List<StoreLocalSupplierInvoiceDetails> details,
            string userName
        )
        {
            var result = new BatchOperationResult();
            var db = _context.Db;
            var now = DateTime.UtcNow;
            var validDetails = new List<StoreLocalSupplierInvoiceDetails>();

            foreach (var detail in details)
            {
                if (detail.PurchasePrice == null || detail.PurchasePrice <= 0)
                {
                    result.Errors.Add($"更新进货价跳过：{detail.DetailGUID} 新进货价为空或为0");
                    result.SkippedCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(detail.ProductCode))
                {
                    result.Errors.Add($"更新进货价失败：未找到商品编码");
                    result.FailedCount++;
                    continue;
                }

                validDetails.Add(detail);
            }

            if (validDetails.Count == 0)
            {
                return result;
            }

            var productCodes = validDetails
                .Select(x => x.ProductCode!)
                .Distinct()
                .ToList();
            var storeCodes = validDetails
                .Select(x => x.StoreCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            // 明细 ProductCode 与商品 ProductCode 保持一致，不能混用 UUID；先批量查出再批量更新。
            var productsByCode = (await db.Queryable<Product>()
                    .Where(p =>
                        p.ProductCode != null
                        && productCodes.Contains(p.ProductCode)
                        && p.IsDeleted == false
                    )
                    .ToListAsync())
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                .GroupBy(p => p.ProductCode!)
                .ToDictionary(g => g.Key, g => g.First());

            var storePricesByKey = (await db.Queryable<StoreRetailPrice>()
                    .Where(srp =>
                        srp.ProductCode != null
                        && srp.StoreCode != null
                        && productCodes.Contains(srp.ProductCode)
                        && storeCodes.Contains(srp.StoreCode)
                        && srp.IsDeleted == false
                    )
                    .ToListAsync())
                .GroupBy(srp => $"{srp.ProductCode}\u001f{srp.StoreCode}")
                .ToDictionary(g => g.Key, g => g.First());

            var productsToUpdate = new Dictionary<string, Product>();
            var storePricesToUpdate = new Dictionary<string, StoreRetailPrice>();
            foreach (var detail in validDetails)
            {
                var storePriceKey = $"{detail.ProductCode}\u001f{detail.StoreCode}";
                if (
                    !productsByCode.TryGetValue(detail.ProductCode!, out var product)
                    || !storePricesByKey.TryGetValue(storePriceKey, out var storePrice)
                )
                {
                    result.Errors.Add($"更新进货价失败：商品或分店价格未更新");
                    result.FailedCount++;
                    continue;
                }

                var purchasePrice = detail.PurchasePrice.GetValueOrDefault();
                product.PurchasePrice = purchasePrice;
                product.UpdatedAt = now;
                product.UpdatedBy = userName;
                storePrice.PurchasePrice = purchasePrice;
                storePrice.UpdatedAt = now;
                storePrice.UpdatedBy = userName;

                productsToUpdate[detail.ProductCode!] = product;
                storePricesToUpdate[storePriceKey] = storePrice;
                result.SuccessCount++;
                result.SuccessfulDetailGuids.Add(detail.DetailGUID);
            }

            if (productsToUpdate.Count > 0)
            {
                await db.Updateable(productsToUpdate.Values.ToList()).ExecuteCommandAsync();
            }

            if (storePricesToUpdate.Count > 0)
            {
                await db.Updateable(storePricesToUpdate.Values.ToList()).ExecuteCommandAsync();
            }

            return result;
        }

        private async Task<BatchOperationResult> BatchUpdateItemNumberAsync(
            List<StoreLocalSupplierInvoiceDetails> details,
            Dictionary<string, string> productItemNumbers,
            string userName
        )
        {
            var result = new BatchOperationResult();
            var db = _context.Db;
            var now = DateTime.UtcNow;
            var validDetails = new List<StoreLocalSupplierInvoiceDetails>();

            foreach (var detail in details)
            {
                if (string.IsNullOrWhiteSpace(detail.ItemNumber))
                {
                    result.Errors.Add($"更新货号失败：新货号不能为空");
                    result.FailedCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(detail.ProductCode))
                {
                    result.Errors.Add($"更新货号失败：未找到商品编码");
                    result.FailedCount++;
                    continue;
                }

                validDetails.Add(detail);
            }

            if (validDetails.Count == 0)
            {
                return result;
            }

            var productCodes = validDetails
                .Select(x => x.ProductCode!)
                .Distinct()
                .ToList();
            // 明细 ProductCode 与商品 ProductCode 保持一致，不能混用 UUID；商品一次查出后批量更新货号。
            var productsByCode = (await db.Queryable<Product>()
                    .Where(p =>
                        p.ProductCode != null
                        && productCodes.Contains(p.ProductCode)
                        && p.IsDeleted == false
                    )
                    .ToListAsync())
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                .GroupBy(p => p.ProductCode!)
                .ToDictionary(g => g.Key, g => g.First());

            var productsToUpdate = new Dictionary<string, Product>();
            foreach (var detail in validDetails)
            {
                if (!productsByCode.TryGetValue(detail.ProductCode!, out var product))
                {
                    result.Errors.Add($"更新货号失败：商品未更新");
                    result.FailedCount++;
                    continue;
                }

                product.ItemNumber = detail.ItemNumber;
                product.UpdatedAt = now;
                product.UpdatedBy = userName;
                productsToUpdate[detail.ProductCode!] = product;
                result.SuccessCount++;
                result.SuccessfulDetailGuids.Add(detail.DetailGUID);
            }

            if (productsToUpdate.Count > 0)
            {
                await db.Updateable(productsToUpdate.Values.ToList()).ExecuteCommandAsync();
            }

            return result;
        }

        private async Task<BatchOperationResult> BatchAddMultiCodesAsync(
            List<StoreLocalSupplierInvoiceDetails> details,
            StoreLocalSupplierInvoice header,
            string userName
        )
        {
            var result = new BatchOperationResult();
            var db = _context.Db;
            var now = DateTime.UtcNow;

            // 1. 获取所有有效分店
            var activeStores = await db.Queryable<Store>()
                .Where(s => s.IsActive == true)
                .Select(s => s.StoreCode)
                .ToListAsync();

            // 2. 收集需要修改商品类型的数据
            var productCodesToUpdate = details
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .Select(x => x.ProductCode!)
                .Distinct()
                .ToList();

            if (productCodesToUpdate.Count > 0)
            {
                var products = await db.Queryable<Product>()
                    .Where(p =>
                        p.ProductCode != null
                        && productCodesToUpdate.Contains(p.ProductCode)
                        && p.IsDeleted == false
                    )
                    .Select(p => new { p.ProductCode, p.ProductType })
                    .ToListAsync();

                var uuidsToUpdate = products
                    .Where(p => p.ProductType != 1)
                    .Select(p => p.ProductCode)
                    .ToList();

                if (uuidsToUpdate.Count > 0)
                {
                    await db.Updateable<Product>()
                        .SetColumns(p => p.ProductType == 1)
                        .SetColumns(p => p.UpdatedAt == now)
                        .SetColumns(p => p.UpdatedBy == userName)
                        .Where(p => uuidsToUpdate.Contains(p.ProductCode) && p.IsDeleted == false)
                        .ExecuteCommandAsync();
                }
            }

            // 3. 准备创建 StoreMultiCodeProduct 和 ProductSetCode
            var multiCodesToCreate = new List<StoreMultiCodeProduct>();
            var productSetCodesToCreate = new List<ProductSetCode>();

            foreach (var detail in details)
            {
                if (string.IsNullOrWhiteSpace(detail.ProductCode))
                {
                    result.Errors.Add($"添加多码失败：未找到商品编码");
                    result.FailedCount++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(detail.Barcode))
                {
                    result.Errors.Add($"添加多码失败：条码不能为空");
                    result.FailedCount++;
                    continue;
                }
                // 生成 StoreMultiCodeProduct UUID
                var multiCodeUUID = UuidHelper.GenerateUuid7();
                // 创建 ProductSetCode 关联记录
                var productSetCode = new ProductSetCode
                {
                    SetCodeId = UuidHelper.GenerateUuid7(),
                    ProductCode = detail.ProductCode,
                    SetProductCode = multiCodeUUID,
                    SetItemNumber = detail.ItemNumber ?? string.Empty,
                    SetBarcode = detail.Barcode,
                    SetPurchasePrice = detail.PurchasePrice,
                    SetRetailPrice = detail.RetailPrice,
                    SetQuantity = 1,
                    SetType = 2,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = userName,
                    UpdatedBy = userName,
                };
                productSetCodesToCreate.Add(productSetCode);

                // 为每个有效分店创建记录
                foreach (var storeCode in activeStores)
                {
                    // 创建 StoreMultiCodeProduct
                    var multiCode = new StoreMultiCodeProduct
                    {
                        UUID = UuidHelper.GenerateUuid7(),
                        StoreCode = storeCode,
                        ProductCode = detail.ProductCode,
                        MultiCodeProductCode = multiCodeUUID,
                        StoreMultiCodeProductCode = storeCode + multiCodeUUID,
                        MultiBarcode = detail.Barcode,
                        PurchasePrice = detail.PurchasePrice,
                        MultiCodeRetailPrice = detail.RetailPrice,
                        DiscountRate = detail.DiscountRate,
                        IsAutoPricing = detail.AutoPricing ?? true,
                        IsSpecialProduct = detail.IsSpecialProduct ?? false,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = userName,
                        UpdatedBy = userName,
                    };
                    multiCodesToCreate.Add(multiCode);
                }

                result.SuccessCount++;
                result.SuccessfulDetailGuids.Add(detail.DetailGUID);
            }

            // 4. 批量插入 StoreMultiCodeProduct
            if (multiCodesToCreate.Count > 0)
            {
                await db.Fastest<StoreMultiCodeProduct>().BulkCopyAsync(multiCodesToCreate);
            }

            // 5. 批量插入 ProductSetCode
            if (productSetCodesToCreate.Count > 0)
            {
                await db.Fastest<ProductSetCode>().BulkCopyAsync(productSetCodesToCreate);
            }

            return result;
        }

        private async Task BatchUpdateDetailActivityTypeAsync(
            List<string> detailGuids,
            string userName
        )
        {
            var db = _context.Db;
            var now = DateTime.UtcNow;

            await db.Updateable<StoreLocalSupplierInvoiceDetails>()
                .SetColumns(x => x.ActivityType == 99)
                .SetColumns(x => x.UpdatedAt == now)
                .SetColumns(x => x.UpdatedBy == userName)
                .Where(x => detailGuids.Contains(x.DetailGUID))
                .ExecuteCommandAsync();
        }

        private class BatchOperationResult
        {
            public int SuccessCount { get; set; }
            public int FailedCount { get; set; }
            public int SkippedCount { get; set; }
            public List<string> Errors { get; set; } = new();
            public List<string> SuccessfulDetailGuids { get; set; } = new();
        }

        public async Task<SyncResult> PushInvoicesToHqAsync(List<string> invoiceGuids)
        {
            var result = new SyncResult { StartTime = DateTime.UtcNow, IsSuccess = true };

            try
            {
                var localDb = _context.Db;
                var hqDb = _hqContext.Db;

                var invoices = await localDb
                    .Queryable<StoreLocalSupplierInvoice>()
                    .Where(i => invoiceGuids.Contains(i.InvoiceGUID) && i.IsDeleted == false)
                    .ToListAsync();

                if (!invoices.Any())
                {
                    result.IsSuccess = false;
                    result.Message = "未找到有效的进货单数据";
                    result.EndTime = DateTime.UtcNow;
                    result.Duration = result.EndTime - result.StartTime;
                    return result;
                }

                var invoiceGuidList = invoices.Select(i => i.InvoiceGUID).ToList();
                var details = await localDb
                    .Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(d => invoiceGuidList.Contains(d.InvoiceGUID) && d.IsDeleted == false)
                    .ToListAsync();

                var hqInvoiceGuids = invoices.Select(i => i.InvoiceGUID).ToList();
                var existingHqInvoices = await hqDb.Queryable<RED_进货单主表Store>()
                    .Where(h => hqInvoiceGuids.Contains(h.HGUID))
                    .ToListAsync();
                var existingHqInvoiceSet = existingHqInvoices.Select(h => h.HGUID).ToHashSet();

                var hqDetailGuids = details.Select(d => d.DetailGUID).ToList();
                var existingHqDetails = new HashSet<string>();
                if (hqDetailGuids.Any())
                {
                    var chunkSize = 500;
                    for (int i = 0; i < hqDetailGuids.Count; i += chunkSize)
                    {
                        var chunk = hqDetailGuids.Skip(i).Take(chunkSize).ToList();
                        var chunkExisting = await hqDb.Queryable<RED_进货单详情表Store>()
                            .Where(h => chunk.Contains(h.HGUID))
                            .Select(h => h.HGUID)
                            .ToListAsync();
                        foreach (var g in chunkExisting)
                            existingHqDetails.Add(g);
                    }
                }

                var addedInvoiceCount = 0;
                var updatedInvoiceCount = 0;
                var addedDetailCount = 0;
                var updatedDetailCount = 0;

                foreach (var invoice in invoices)
                {
                    try
                    {
                        var hqEntity = _mapper.Map<RED_进货单主表Store>(invoice);
                        if (existingHqInvoiceSet.Contains(invoice.InvoiceGUID))
                        {
                            var existing = existingHqInvoices.First(h =>
                                h.HGUID == invoice.InvoiceGUID
                            );
                            hqEntity.ID = existing.ID;
                            await hqDb.Updateable(hqEntity).ExecuteCommandAsync();
                            updatedInvoiceCount++;
                        }
                        else
                        {
                            await hqDb.Insertable(hqEntity).ExecuteCommandAsync();
                            addedInvoiceCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "推送进货单主表失败: {GUID}", invoice.InvoiceGUID);
                        result.ErrorCount++;
                    }
                }

                foreach (var detail in details)
                {
                    try
                    {
                        var hqEntity = _mapper.Map<RED_进货单详情表Store>(detail);
                        if (existingHqDetails.Contains(detail.DetailGUID))
                        {
                            var existingDetail = await hqDb.Queryable<RED_进货单详情表Store>()
                                .FirstAsync(h => h.HGUID == detail.DetailGUID);
                            if (existingDetail != null)
                            {
                                hqEntity.ID = existingDetail.ID;
                                await hqDb.Updateable(hqEntity).ExecuteCommandAsync();
                            }
                            updatedDetailCount++;
                        }
                        else
                        {
                            await hqDb.Insertable(hqEntity).ExecuteCommandAsync();
                            addedDetailCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "推送进货单详情失败: {GUID}", detail.DetailGUID);
                        result.ErrorCount++;
                    }
                }

                result.AddedCount = addedInvoiceCount + addedDetailCount;
                result.UpdatedCount = updatedInvoiceCount + updatedDetailCount;
                result.TotalCount = invoices.Count;
                result.Message =
                    $"成功推送 {invoices.Count} 个进货单（主表 新增{addedInvoiceCount}/更新{updatedInvoiceCount}，详情 新增{addedDetailCount}/更新{updatedDetailCount}）";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送进货单到HQ异常");
                result.IsSuccess = false;
                result.Message = $"推送异常: {ex.Message}";
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }
    }
}
