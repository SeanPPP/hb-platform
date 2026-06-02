using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class LocalSupplierInvoiceHqProductSyncService : ILocalSupplierInvoiceHqProductSyncService
    {
        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hqContext;
        private readonly ILogger<LocalSupplierInvoiceHqProductSyncService> _logger;

        public LocalSupplierInvoiceHqProductSyncService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            ILogger<LocalSupplierInvoiceHqProductSyncService> logger
        )
        {
            _context = context;
            _hqContext = hqContext;
            _logger = logger;
        }

        public async Task<ApiResponse<EnsureHqProductsResult>> EnsureHqProductsAsync(
            string invoiceGuid,
            EnsureHqProductsRequest request,
            string updatedBy
        )
        {
            var result = new EnsureHqProductsResult();
            var detailGuids = request.DetailGuids
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
            var targetStoreCodes = request.TargetStoreCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (detailGuids.Count == 0)
                return ApiResponse<EnsureHqProductsResult>.Error("请选择要同步的明细", "VALIDATION_ERROR", result);
            if (targetStoreCodes.Count == 0)
                return ApiResponse<EnsureHqProductsResult>.Error("请选择目标分店", "VALIDATION_ERROR", result);

            result.Total = detailGuids.Count;

            try
            {
                var db = _context.Db;
                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();
                if (header == null)
                    return ApiResponse<EnsureHqProductsResult>.Error("进货单不存在", "NOT_FOUND", result);

                var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x =>
                        x.InvoiceGUID == invoiceGuid
                        && detailGuids.Contains(x.DetailGUID)
                        && x.IsDeleted == false
                    )
                    .ToListAsync();
                if (details.Count == 0)
                    return ApiResponse<EnsureHqProductsResult>.Error("未找到要同步的明细", "NOT_FOUND", result);

                var activeStoreCodes = await db.Queryable<Store>()
                    .Where(x => x.IsActive && x.IsDeleted == false)
                    .Select(x => x.StoreCode)
                    .ToListAsync();
                activeStoreCodes = activeStoreCodes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();
                if (activeStoreCodes.Count == 0)
                    return ApiResponse<EnsureHqProductsResult>.Error("未找到启用分店", "NO_ACTIVE_STORE", result);

                var invalidTargetStores = targetStoreCodes
                    .Where(storeCode => !activeStoreCodes.Contains(storeCode))
                    .ToList();
                if (invalidTargetStores.Count > 0)
                {
                    return ApiResponse<EnsureHqProductsResult>.Error(
                        $"目标分店不存在或未启用：{string.Join(", ", invalidTargetStores)}",
                        "INVALID_TARGET_STORE",
                        result
                    );
                }

                var syncItems = new List<PreparedSyncItem>();
                await db.Ado.BeginTranAsync();
                try
                {
                    foreach (var detail in details)
                    {
                        var prepared = await PrepareLocalProductAsync(
                            header,
                            detail,
                            activeStoreCodes,
                            targetStoreCodes,
                            updatedBy,
                            result
                        );
                        if (prepared != null)
                            syncItems.Add(prepared);
                    }

                    await db.Ado.CommitTranAsync();
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                foreach (var item in syncItems)
                {
                    try
                    {
                        await SyncHqProductAsync(item, activeStoreCodes, targetStoreCodes, updatedBy, result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "同步进货单明细商品到HQ失败 DetailGuid={DetailGuid}",
                            item.Detail.DetailGUID
                        );
                        AddError(result, item.Detail.DetailGUID, null, $"同步HQ失败：{ex.Message}");
                    }
                }

                if (result.Failed > 0)
                {
                    return ApiResponse<EnsureHqProductsResult>.Error(
                        "同步商品到HQ部分失败",
                        "HQ_SYNC_PARTIAL_FAILED",
                        result
                    );
                }

                return ApiResponse<EnsureHqProductsResult>.OK(result, "同步商品到HQ完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品到HQ异常");
                return ApiResponse<EnsureHqProductsResult>.Error(
                    $"同步商品到HQ失败: {ex.Message}",
                    "HQ_SYNC_ERROR",
                    result
                );
            }
        }

        private async Task<PreparedSyncItem?> PrepareLocalProductAsync(
            StoreLocalSupplierInvoice header,
            StoreLocalSupplierInvoiceDetails detail,
            List<string> activeStoreCodes,
            List<string> targetStoreCodes,
            string updatedBy,
            EnsureHqProductsResult result
        )
        {
            if (detail.PurchasePrice == null || detail.PurchasePrice <= 0)
            {
                AddError(result, detail.DetailGUID, detail.StoreCode, "进货价必须大于0");
                return null;
            }

            var db = _context.Db;
            var now = DateTime.UtcNow;
            Product? product = null;

            if (!string.IsNullOrWhiteSpace(detail.ProductCode))
            {
                product = await db.Queryable<Product>()
                    .Where(x => x.ProductCode == detail.ProductCode && x.IsDeleted == false)
                    .FirstAsync();
            }
            else if (!string.IsNullOrWhiteSpace(detail.ItemNumber) || !string.IsNullOrWhiteSpace(detail.Barcode))
            {
                var supplierCode = header.SupplierCode ?? detail.SupplierCode;
                var itemNumber = detail.ItemNumber;
                var barcode = detail.Barcode;
                var productQuery = db.Queryable<Product>()
                    .Where(x => x.IsDeleted == false && x.LocalSupplierCode == supplierCode);

                if (!string.IsNullOrWhiteSpace(itemNumber) && !string.IsNullOrWhiteSpace(barcode))
                    productQuery = productQuery.Where(x => x.ItemNumber == itemNumber || x.Barcode == barcode);
                else if (!string.IsNullOrWhiteSpace(itemNumber))
                    productQuery = productQuery.Where(x => x.ItemNumber == itemNumber);
                else
                    productQuery = productQuery.Where(x => x.Barcode == barcode);

                product = await productQuery.FirstAsync();
            }

            var isNewProduct = product == null;
            if (product == null)
            {
                if (string.IsNullOrWhiteSpace(detail.ItemNumber))
                {
                    AddError(result, detail.DetailGUID, detail.StoreCode, "新建商品货号不能为空");
                    return null;
                }
                if (string.IsNullOrWhiteSpace(detail.Barcode))
                {
                    AddError(result, detail.DetailGUID, detail.StoreCode, "新建商品条码不能为空");
                    return null;
                }

                var productCode = UuidHelper.GenerateUuid7();
                product = new Product
                {
                    UUID = productCode,
                    ProductCode = productCode,
                    ProductCategoryGUID = detail.ProductCategoryGUID,
                    LocalSupplierCode = header.SupplierCode ?? detail.SupplierCode,
                    ItemNumber = detail.ItemNumber,
                    Barcode = detail.Barcode,
                    ProductName = detail.ProductName ?? string.Empty,
                    ProductType = 0,
                    PurchasePrice = detail.PurchasePrice,
                    RetailPrice = ResolveRetailPrice(detail),
                    IsAutoPricing = detail.AutoPricing ?? true,
                    IsSpecialProduct = detail.IsSpecialProduct ?? false,
                    ProductImage = detail.ProductImage,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = updatedBy,
                    UpdatedBy = updatedBy,
                };

                await db.Insertable(product).ExecuteCommandAsync();
                result.HbwebCreated++;
            }
            else
            {
                // 已有商品是全局主档，这里只绑定明细并更新目标分店价格，避免越过分店范围修改商品资料。
            }

            detail.ProductCode = product.ProductCode;
            detail.StoreProductCode ??= BuildStoreProductCode(detail.StoreCode, product.ProductCode);
            detail.LastPurchasePrice = detail.PurchasePrice ?? product.PurchasePrice;
            detail.UpdatedAt = now;
            detail.UpdatedBy = updatedBy;
            await db.Updateable(detail).ExecuteCommandAsync();

            // 新建商品为所有启用分店创建价格；已有商品只更新/补齐目标分店。
            var localPriceScope = isNewProduct ? activeStoreCodes : targetStoreCodes;
            await UpsertLocalStorePricesAsync(detail, product, localPriceScope, updatedBy);

            return new PreparedSyncItem(detail, product, isNewProduct);
        }

        private async Task UpsertLocalStorePricesAsync(
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            List<string> storeCodes,
            string updatedBy
        )
        {
            var db = _context.Db;
            var now = DateTime.UtcNow;
            var productCode = product.ProductCode!;
            var existingPrices = await db.Queryable<StoreRetailPrice>()
                .Where(x =>
                    storeCodes.Contains(x.StoreCode)
                    && x.ProductCode == productCode
                    && x.IsDeleted == false
                )
                .ToListAsync();
            var existingByStore = existingPrices
                .Where(x => !string.IsNullOrWhiteSpace(x.StoreCode))
                .ToDictionary(x => x.StoreCode!, x => x);

            foreach (var storeCode in storeCodes)
            {
                if (existingByStore.TryGetValue(storeCode, out var existing))
                {
                    existing.SupplierCode = product.LocalSupplierCode ?? detail.SupplierCode;
                    existing.PurchasePrice = detail.PurchasePrice;
                    existing.StoreRetailPriceValue = ResolveRetailPrice(detail);
                    existing.DiscountRate = detail.DiscountRate;
                    existing.IsAutoPricing = detail.AutoPricing ?? existing.IsAutoPricing;
                    existing.IsSpecialProduct = detail.IsSpecialProduct ?? existing.IsSpecialProduct;
                    existing.UpdatedAt = now;
                    existing.UpdatedBy = updatedBy;
                    await db.Updateable(existing).ExecuteCommandAsync();
                    continue;
                }

                await db.Insertable(new StoreRetailPrice
                {
                    UUID = UuidHelper.GenerateUuid7(),
                    StoreCode = storeCode,
                    ProductCode = productCode,
                    StoreProductCode = BuildStoreProductCode(storeCode, productCode),
                    SupplierCode = product.LocalSupplierCode ?? detail.SupplierCode,
                    PurchasePrice = detail.PurchasePrice,
                    StoreRetailPriceValue = ResolveRetailPrice(detail),
                    DiscountRate = detail.DiscountRate,
                    IsActive = true,
                    IsAutoPricing = detail.AutoPricing ?? true,
                    IsSpecialProduct = detail.IsSpecialProduct ?? false,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = updatedBy,
                    UpdatedBy = updatedBy,
                }).ExecuteCommandAsync();
            }
        }

        private async Task SyncHqProductAsync(
            PreparedSyncItem item,
            List<string> activeStoreCodes,
            List<string> targetStoreCodes,
            string updatedBy,
            EnsureHqProductsResult result
        )
        {
            var hqDb = _hqContext.Db;
            var detail = item.Detail;
            var product = item.Product;
            var productCode = product.ProductCode!;
            var now = DateTime.UtcNow;
            var hqProduct = await hqDb.Queryable<DIC_商品信息字典表>()
                .Where(x => x.H商品编码 == productCode)
                .FirstAsync();
            var hqProductExisted = hqProduct != null;

            if (hqProductExisted)
            {
                result.HqExisting++;
                // HQ商品字典是全局主档；已有商品只同步目标分店价格，不改主档字段。
                result.HqSynced++;
            }
            else
            {
                await hqDb.Insertable(new DIC_商品信息字典表
                {
                    HGUID = product.UUID,
                    H商品标签GUID = detail.ProductTagGUID ?? string.Empty,
                    H商品分类码GUID = product.ProductCategoryGUID ?? string.Empty,
                    H供货商编码 = product.LocalSupplierCode ?? string.Empty,
                    H商品编码 = productCode,
                    H货号 = product.ItemNumber ?? string.Empty,
                    H主条形码 = product.Barcode ?? string.Empty,
                    H商品名称 = product.ProductName ?? string.Empty,
                    H商品类型 = product.ProductType ?? 0,
                    H大写名称 = product.ProductName ?? string.Empty,
                    H规格 = detail.Specification ?? string.Empty,
                    H单位 = detail.Unit ?? string.Empty,
                    H进货价 = product.PurchasePrice ?? 0,
                    H零售价 = product.RetailPrice ?? ResolveRetailPrice(detail),
                    H是否自动定价 = product.IsAutoPricing,
                    H商品图片 = product.ProductImage ?? string.Empty,
                    中包数量 = product.MiddlePackageQuantity ?? 0,
                    H腾讯云图地址 = string.Empty,
                    H使用状态 = product.IsActive,
                    H是否特殊商品 = product.IsSpecialProduct,
                    H进货单主表GUID = detail.InvoiceGUID ?? string.Empty,
                    H进货单详情GUID = detail.DetailGUID,
                    CBP商品中文名称 = product.ProductName ?? string.Empty,
                    CBP供应商编码 = product.LocalSupplierCode ?? string.Empty,
                    CBP商品分类码GUID = product.WarehouseCategoryGUID ?? string.Empty,
                    FGC_Creator = updatedBy,
                    FGC_CreateDate = now,
                    FGC_LastModifier = updatedBy,
                    FGC_LastModifyDate = now,
                    FGC_UpdateHelp = string.Empty,
                }).IgnoreColumns(x => x.ID).ExecuteCommandAsync();
                result.HqCreated++;
            }

            // 新建HQ商品为所有启用分店创建价格；已有HQ商品只更新/补齐目标分店。
            var hqPriceScope = hqProductExisted ? targetStoreCodes : activeStoreCodes;
            await UpsertHqStorePricesAsync(detail, product, hqPriceScope, updatedBy, result);
        }

        private async Task UpsertHqStorePricesAsync(
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            List<string> storeCodes,
            string updatedBy,
            EnsureHqProductsResult result
        )
        {
            var hqDb = _hqContext.Db;
            var productCode = product.ProductCode!;
            var now = DateTime.UtcNow;
            var existingPrices = await hqDb.Queryable<DIC_商品零售价表>()
                .Where(x => storeCodes.Contains(x.H分店代码) && x.H商品编码 == productCode)
                .ToListAsync();
            var existingByStore = existingPrices.ToDictionary(x => x.H分店代码, x => x);

            foreach (var storeCode in storeCodes)
            {
                try
                {
                    if (existingByStore.TryGetValue(storeCode, out var existing))
                    {
                        existing.H分店商品编码 = BuildStoreProductCode(storeCode, productCode);
                        existing.H供应商编码 = product.LocalSupplierCode ?? detail.SupplierCode ?? string.Empty;
                        existing.H分店供应商编码 = BuildStoreSupplierCode(
                            storeCode,
                            product.LocalSupplierCode ?? detail.SupplierCode
                        );
                        existing.H进货价 = detail.PurchasePrice ?? product.PurchasePrice ?? 0;
                        existing.H分店零售价 = ResolveRetailPrice(detail);
                        existing.H折扣率 = detail.DiscountRate ?? existing.H折扣率;
                        existing.H使用状态 = true;
                        existing.H是否自动定价 = detail.AutoPricing ?? existing.H是否自动定价;
                        existing.H是否特殊商品 = detail.IsSpecialProduct ?? existing.H是否特殊商品;
                        existing.FGC_LastModifier = updatedBy;
                        existing.FGC_LastModifyDate = now;
                        await hqDb.Updateable(existing).ExecuteCommandAsync();
                        result.HqPurchasePricesUpdated++;
                        continue;
                    }

                    await hqDb.Insertable(new DIC_商品零售价表
                    {
                        HGUID = UuidHelper.GenerateUuid7(),
                        H分店代码 = storeCode,
                        H商品编码 = productCode,
                        H分店商品编码 = BuildStoreProductCode(storeCode, productCode),
                        H供应商编码 = product.LocalSupplierCode ?? detail.SupplierCode ?? string.Empty,
                        H分店供应商编码 = BuildStoreSupplierCode(
                            storeCode,
                            product.LocalSupplierCode ?? detail.SupplierCode
                        ),
                        H进货价 = detail.PurchasePrice ?? product.PurchasePrice ?? 0,
                        H分店零售价 = ResolveRetailPrice(detail),
                        H库存 = 0,
                        H库存金额 = 0,
                        H库存预警数 = 0,
                        H商品缺货日期 = DateTime.MinValue,
                        H是否缺货状态 = false,
                        H最小订货量 = 0,
                        H最小订货量合计金额 = 0,
                        H活动类型 = string.Empty,
                        H满减活动代码 = string.Empty,
                        H活动开始日期 = DateTime.MinValue,
                        H活动结束日期 = DateTime.MinValue,
                        H折扣率 = detail.DiscountRate ?? 0,
                        H满减数量 = 0,
                        H满减金额 = 0,
                        H多码数量 = 0,
                        H使用状态 = true,
                        H是否自动定价 = detail.AutoPricing ?? true,
                        H自动新价格 = detail.NewAutoRetailPrice ?? 0,
                        H盘点入库记录数 = 0,
                        H是否特殊商品 = detail.IsSpecialProduct ?? false,
                        H动态销售数量 = 0,
                        H动态销售额 = 0,
                        H动态成本 = 0,
                        H动态毛利 = 0,
                        H动态毛利率 = 0,
                        H动态销售占比 = 0,
                        FGC_Creator = updatedBy,
                        FGC_CreateDate = now,
                        FGC_LastModifier = updatedBy,
                        FGC_LastModifyDate = now,
                    }).IgnoreColumns(x => x.ID).ExecuteCommandAsync();
                    result.HqPurchasePricesUpdated++;
                }
                catch (Exception ex)
                {
                    AddError(result, detail.DetailGUID, storeCode, $"同步HQ分店价格失败：{ex.Message}");
                }
            }
        }

        private static decimal ResolveRetailPrice(StoreLocalSupplierInvoiceDetails detail)
        {
            if (detail.AutoPricing == true && detail.NewAutoRetailPrice.GetValueOrDefault() > 0)
                return detail.NewAutoRetailPrice!.Value;
            if (detail.RetailPrice.GetValueOrDefault() > 0)
                return detail.RetailPrice!.Value;
            if (detail.NewAutoRetailPrice.GetValueOrDefault() > 0)
                return detail.NewAutoRetailPrice!.Value;
            return (detail.PurchasePrice ?? 0) * 2.5m;
        }

        private static string BuildStoreProductCode(string? storeCode, string productCode)
        {
            return $"{storeCode ?? string.Empty}{productCode}";
        }

        private static string BuildStoreSupplierCode(string storeCode, string? supplierCode)
        {
            return $"{storeCode}{supplierCode ?? string.Empty}";
        }

        private static void AddError(
            EnsureHqProductsResult result,
            string detailGuid,
            string? storeCode,
            string message
        )
        {
            result.Failed++;
            result.Errors.Add(new EnsureHqProductError
            {
                DetailGuid = detailGuid,
                StoreCode = storeCode,
                Message = message,
            });
        }

        private sealed record PreparedSyncItem(
            StoreLocalSupplierInvoiceDetails Detail,
            Product Product,
            bool IsNewProduct
        );
    }
}
