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

                syncItems = await AttachDomesticSupplierCodesAsync(db, syncItems);

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

        public async Task<ApiResponse<UpdateHqProductsResult>> UpdateHqProductsAsync(
            string invoiceGuid,
            UpdateHqProductsRequest? request,
            string updatedBy
        )
        {
            var result = new UpdateHqProductsResult();
            // HQ字段更新直接写总部价格表，入口先兜底空payload，避免异常绕过可展示的失败结果。
            if (request == null)
                return ApiResponse<UpdateHqProductsResult>.Error("请求参数不能为空", "VALIDATION_ERROR", result);

            var updateFields = request.UpdateFields;
            var detailGuids = (request.DetailGuids ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
            var targetStoreCodes = (request.TargetStoreCodes ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (detailGuids.Count == 0)
                return ApiResponse<UpdateHqProductsResult>.Error("请选择要更新的明细", "VALIDATION_ERROR", result);
            if (targetStoreCodes.Count == 0)
                return ApiResponse<UpdateHqProductsResult>.Error("请选择目标分店", "VALIDATION_ERROR", result);
            if (updateFields == null || !HasAnyUpdateField(updateFields))
                return ApiResponse<UpdateHqProductsResult>.Error("请选择要更新的HQ字段", "VALIDATION_ERROR", result);

            result.Total = detailGuids.Count;

            try
            {
                var db = _context.Db;
                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();
                if (header == null)
                    return ApiResponse<UpdateHqProductsResult>.Error("进货单不存在", "NOT_FOUND", result);

                var details = await db.Queryable<StoreLocalSupplierInvoiceDetails>()
                    .Where(x =>
                        x.InvoiceGUID == invoiceGuid
                        && detailGuids.Contains(x.DetailGUID)
                        && x.IsDeleted == false
                    )
                    .ToListAsync();
                if (details.Count == 0)
                    return ApiResponse<UpdateHqProductsResult>.Error("未找到要更新的明细", "NOT_FOUND", result);

                var activeStoreCodes = await db.Queryable<Store>()
                    .Where(x => x.IsActive && x.IsDeleted == false)
                    .Select(x => x.StoreCode)
                    .ToListAsync();
                activeStoreCodes = activeStoreCodes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .ToList();
                if (activeStoreCodes.Count == 0)
                    return ApiResponse<UpdateHqProductsResult>.Error("未找到启用分店", "NO_ACTIVE_STORE", result);

                var invalidTargetStores = targetStoreCodes
                    .Where(storeCode => !activeStoreCodes.Contains(storeCode))
                    .ToList();
                if (invalidTargetStores.Count > 0)
                {
                    return ApiResponse<UpdateHqProductsResult>.Error(
                        $"目标分店不存在或未启用：{string.Join(", ", invalidTargetStores)}",
                        "INVALID_TARGET_STORE",
                        result
                    );
                }

                var updateItems = new List<PreparedSyncItem>();
                await db.Ado.BeginTranAsync();
                try
                {
                    foreach (var detail in details)
                    {
                        var prepared = await PrepareLocalProductForHqUpdateAsync(
                            header,
                            detail,
                            activeStoreCodes,
                            updatedBy,
                            result
                        );
                        if (prepared != null)
                            updateItems.Add(prepared);
                    }

                    await db.Ado.CommitTranAsync();
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                updateItems = await AttachDomesticSupplierCodesAsync(db, updateItems);

                foreach (var item in updateItems)
                {
                    try
                    {
                        await UpdateHqStorePricesAsync(
                            item,
                            activeStoreCodes,
                            targetStoreCodes,
                            updateFields,
                            updatedBy,
                            result
                        );
                        await SyncHqMultiCodesAsync(item, targetStoreCodes, updatedBy, result);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "更新HQ商品字段失败 DetailGuid={DetailGuid}",
                            item.Detail.DetailGUID
                        );
                        AddError(result, item.Detail.DetailGUID, null, $"更新HQ商品失败：{ex.Message}");
                    }
                }

                if (result.Failed > 0)
                {
                    return ApiResponse<UpdateHqProductsResult>.Error(
                        "更新HQ商品部分失败",
                        "HQ_UPDATE_PARTIAL_FAILED",
                        result
                    );
                }

                return ApiResponse<UpdateHqProductsResult>.OK(result, "更新HQ商品完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新HQ商品异常");
                return ApiResponse<UpdateHqProductsResult>.Error(
                    $"更新HQ商品失败: {ex.Message}",
                    "HQ_UPDATE_ERROR",
                    result
                );
            }
        }

        private async Task<PreparedSyncItem?> PrepareLocalProductForHqUpdateAsync(
            StoreLocalSupplierInvoice header,
            StoreLocalSupplierInvoiceDetails detail,
            List<string> activeStoreCodes,
            string updatedBy,
            UpdateHqProductsResult result
        )
        {
            var db = _context.Db;
            var now = DateTime.UtcNow;
            var product = await FindExistingProductAsync(
                db,
                detail.ProductCode,
                header.SupplierCode ?? detail.SupplierCode,
                detail.ItemNumber,
                detail.Barcode
            );

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
                if (detail.PurchasePrice == null || detail.PurchasePrice <= 0)
                {
                    AddError(result, detail.DetailGUID, detail.StoreCode, "新建商品进货价必须大于0");
                    return null;
                }

                var generatedProductCode = UuidHelper.GenerateUuid7();
                product = new Product
                {
                    UUID = generatedProductCode,
                    ProductCode = generatedProductCode,
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

            // 更新HQ商品链路不回写本单明细，避免把“上次进货价”等明细字段改成本次操作值。
            // 这里只在内存中补齐商品编码，供后续本地价格和HQ价格写入使用。
            detail.ProductCode = product.ProductCode;
            detail.StoreProductCode ??= BuildStoreProductCode(detail.StoreCode, product.ProductCode!);

            // 更新HQ商品时，只有新建本地商品才需要补齐所有启用分店价格。
            if (isNewProduct)
            {
                await UpsertLocalStorePricesAsync(detail, product, activeStoreCodes, updatedBy);
            }

            return new PreparedSyncItem(detail, product, isNewProduct);
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
            var product = await FindExistingProductAsync(
                db,
                detail.ProductCode,
                header.SupplierCode ?? detail.SupplierCode,
                detail.ItemNumber,
                detail.Barcode
            );

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

                var generatedProductCode = UuidHelper.GenerateUuid7();
                product = new Product
                {
                    UUID = generatedProductCode,
                    ProductCode = generatedProductCode,
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

            if (string.IsNullOrWhiteSpace(product.ProductCode))
            {
                throw new InvalidOperationException("同步分店进货单时商品编码不能为空");
            }

            var productCode = product.ProductCode;
            detail.ProductCode = productCode;
            detail.StoreProductCode ??= BuildStoreProductCode(detail.StoreCode, productCode);
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
                    x.StoreCode != null
                    && storeCodes.Contains(x.StoreCode)
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
            var domesticSupplierCode = item.DomesticSupplierCode;
            var hqProduct = await hqDb.Queryable<DIC_商品信息字典表>()
                .Where(x => x.H商品编码 == productCode)
                .FirstAsync();
            var hqProductExisted = hqProduct != null;

            if (hqProductExisted)
            {
                result.HqExisting++;
                // HQ 商品字典是全局主档；只在 CBP 供应商缺失或误写 200 时补国内供应商编码。
                await PatchHqCbpSupplierCodeIfNeededAsync(
                    hqDb,
                    hqProduct!,
                    domesticSupplierCode,
                    updatedBy,
                    now
                );
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
                    CBP供应商编码 = domesticSupplierCode ?? string.Empty,
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

        private async Task UpdateHqStorePricesAsync(
            PreparedSyncItem item,
            List<string> activeStoreCodes,
            List<string> targetStoreCodes,
            UpdateToStorePricesFields updateFields,
            string updatedBy,
            UpdateHqProductsResult result
        )
        {
            var hqDb = _hqContext.Db;
            var detail = item.Detail;
            var product = item.Product;
            var productCode = product.ProductCode!;
            var now = DateTime.UtcNow;
            var domesticSupplierCode = item.DomesticSupplierCode;

            var hqProduct = await FindExistingHqProductAsync(
                hqDb,
                productCode,
                product.LocalSupplierCode ?? detail.SupplierCode,
                product.ItemNumber ?? detail.ItemNumber,
                product.Barcode ?? detail.Barcode
            );
            var hqProductCode = hqProduct?.H商品编码 ?? productCode;
            if (hqProduct == null)
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
                    CBP供应商编码 = domesticSupplierCode ?? string.Empty,
                    CBP商品分类码GUID = product.WarehouseCategoryGUID ?? string.Empty,
                    FGC_Creator = updatedBy,
                    FGC_CreateDate = now,
                    FGC_LastModifier = updatedBy,
                    FGC_LastModifyDate = now,
                    FGC_UpdateHelp = string.Empty,
                }).IgnoreColumns(x => x.ID).ExecuteCommandAsync();
                result.HqCreated++;
            }
            else
            {
                await PatchHqCbpSupplierCodeIfNeededAsync(
                    hqDb,
                    hqProduct,
                    domesticSupplierCode,
                    updatedBy,
                    now
                );
                result.HqExisting++;
                result.HqSynced++;
            }

            // 新建HQ商品后要补齐所有启用分店价格；已有HQ商品只更新用户选择的目标分店。
            var hqPriceScope = hqProduct == null ? activeStoreCodes : targetStoreCodes;
            var existingPrices = await hqDb.Queryable<DIC_商品零售价表>()
                .Where(x => hqPriceScope.Contains(x.H分店代码) && x.H商品编码 == hqProductCode)
                .ToListAsync();
            var existingByStore = existingPrices.ToDictionary(x => x.H分店代码, x => x);

            foreach (var storeCode in hqPriceScope)
            {
                try
                {
                    if (!existingByStore.TryGetValue(storeCode, out var price))
                    {
                        price = BuildHqStorePrice(detail, product, storeCode, updatedBy, now, hqProductCode);
                        if (!ApplyAllHqFieldsForInsert(price, detail, product, updateFields, result, storeCode))
                        {
                            continue;
                        }
                        await hqDb.Insertable(price).IgnoreColumns(x => x.ID).ExecuteCommandAsync();
                        result.Updated++;
                        continue;
                    }

                    if (!ApplySelectedHqFields(price, detail, product, updateFields, result, storeCode))
                    {
                        continue;
                    }
                    price.FGC_LastModifier = updatedBy;
                    price.FGC_LastModifyDate = now;
                    await hqDb.Updateable(price).ExecuteCommandAsync();
                    result.Updated++;
                }
                catch (Exception ex)
                {
                    AddError(result, detail.DetailGUID, storeCode, $"更新HQ分店价格失败：{ex.Message}");
                }
            }
        }

        private async Task SyncHqMultiCodesAsync(
            PreparedSyncItem item,
            List<string> targetStoreCodes,
            string updatedBy,
            UpdateHqProductsResult result
        )
        {
            var productCode = item.Product.ProductCode;
            if (string.IsNullOrWhiteSpace(productCode))
                return;

            var localDb = _context.Db;
            var hqDb = _hqContext.Db;
            var now = DateTime.UtcNow;
            var storeCodes = targetStoreCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            var productSetCodes = await localDb.Queryable<ProductSetCode>()
                .Where(x =>
                    x.ProductCode == productCode
                    && x.IsDeleted == false
                    && x.SetBarcode != null
                )
                .ToListAsync();
            if (productSetCodes.Count == 0)
                return;

            var storeMultiCodes = await localDb.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.ProductCode == productCode
                    && x.StoreCode != null
                    && storeCodes.Contains(x.StoreCode)
                    && x.IsDeleted == false
                    && x.MultiBarcode != null
                )
                .ToListAsync();
            var storeMultiByCode = storeMultiCodes
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.StoreCode)
                    && !string.IsNullOrWhiteSpace(x.MultiCodeProductCode)
                )
                .GroupBy(x => BuildHqStoreMultiCodeKey(x.StoreCode, x.ProductCode, x.MultiCodeProductCode))
                .ToDictionary(g => g.Key, g => g.First());
            var storeMultiByBarcode = storeMultiCodes
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.StoreCode)
                    && !string.IsNullOrWhiteSpace(x.MultiBarcode)
                )
                .GroupBy(x => BuildHqStoreMultiCodeKey(
                    x.StoreCode,
                    x.ProductCode,
                    NormalizeCaseInsensitiveValue(x.MultiBarcode)
                ))
                .ToDictionary(g => g.Key, g => g.First());

            var existingHqProductSetCodes = await hqDb.Queryable<DIC_一品多码表>()
                .Where(x => x.H商品编码 == productCode)
                .ToListAsync();
            var hqProductSetByCode = existingHqProductSetCodes
                .Where(x => !string.IsNullOrWhiteSpace(x.H多码商品编号))
                .GroupBy(x => x.H多码商品编号!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var hqProductSetByBarcode = existingHqProductSetCodes
                .Where(x => !string.IsNullOrWhiteSpace(x.H多条形码))
                .GroupBy(x => NormalizeCaseInsensitiveValue(x.H多条形码)!)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var productSetCode in productSetCodes)
            {
                var mapped = BuildHqProductSetCode(item, productSetCode, updatedBy, now);
                var existing = FindExistingHqProductSetCode(
                    productSetCode,
                    hqProductSetByCode,
                    hqProductSetByBarcode
                );
                if (existing == null)
                {
                    await hqDb.Insertable(mapped).IgnoreColumns(x => x.ID).ExecuteCommandAsync();
                    result.HqProductSetCodesCreated++;
                    continue;
                }

                ApplyHqProductSetCode(existing, mapped);
                await hqDb.Updateable(existing).ExecuteCommandAsync();
                result.HqProductSetCodesUpdated++;
            }

            var existingHqStoreMultiCodes = await hqDb.Queryable<DIC_分店一品多码表>()
                .Where(x =>
                    x.H商品编码 == productCode
                    && x.H分店代码 != null
                    && storeCodes.Contains(x.H分店代码)
                )
                .ToListAsync();
            var hqStoreByCode = existingHqStoreMultiCodes
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.H分店代码)
                    && !string.IsNullOrWhiteSpace(x.H多码商品编码)
                )
                .GroupBy(x => BuildHqStoreMultiCodeKey(x.H分店代码, x.H商品编码, x.H多码商品编码))
                .ToDictionary(g => g.Key, g => g.First());
            var hqStoreByBarcode = existingHqStoreMultiCodes
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.H分店代码)
                    && !string.IsNullOrWhiteSpace(x.H多条形码)
                )
                .GroupBy(x => BuildHqStoreMultiCodeKey(
                    x.H分店代码,
                    x.H商品编码,
                    NormalizeCaseInsensitiveValue(x.H多条形码)
                ))
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var storeCode in storeCodes)
            {
                foreach (var productSetCode in productSetCodes)
                {
                    var storeMultiCode = FindLocalStoreMultiCode(
                        storeCode,
                        productCode,
                        productSetCode,
                        storeMultiByCode,
                        storeMultiByBarcode
                    );
                    var mapped = BuildHqStoreMultiCode(
                        item,
                        productSetCode,
                        storeMultiCode,
                        storeCode,
                        updatedBy,
                        now
                    );
                    var existing = FindExistingHqStoreMultiCode(
                        storeCode,
                        productCode,
                        productSetCode,
                        hqStoreByCode,
                        hqStoreByBarcode
                    );
                    if (existing == null)
                    {
                        await hqDb.Insertable(mapped).IgnoreColumns(x => x.ID).ExecuteCommandAsync();
                        result.HqStoreMultiCodesCreated++;
                        continue;
                    }

                    ApplyHqStoreMultiCode(existing, mapped);
                    await hqDb.Updateable(existing).ExecuteCommandAsync();
                    result.HqStoreMultiCodesUpdated++;
                }
            }
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
                        H是否自动定价 = detail.AutoPricing ?? false,
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

        private static DIC_一品多码表? FindExistingHqProductSetCode(
            ProductSetCode productSetCode,
            Dictionary<string, DIC_一品多码表> hqProductSetByCode,
            Dictionary<string, DIC_一品多码表> hqProductSetByBarcode
        )
        {
            if (
                !string.IsNullOrWhiteSpace(productSetCode.SetProductCode)
                && hqProductSetByCode.TryGetValue(productSetCode.SetProductCode, out var byCode)
            )
            {
                return byCode;
            }

            var barcodeKey = NormalizeCaseInsensitiveValue(productSetCode.SetBarcode);
            return barcodeKey != null && hqProductSetByBarcode.TryGetValue(barcodeKey, out var byBarcode)
                ? byBarcode
                : null;
        }

        private static StoreMultiCodeProduct? FindLocalStoreMultiCode(
            string storeCode,
            string productCode,
            ProductSetCode productSetCode,
            Dictionary<string, StoreMultiCodeProduct> storeMultiByCode,
            Dictionary<string, StoreMultiCodeProduct> storeMultiByBarcode
        )
        {
            if (!string.IsNullOrWhiteSpace(productSetCode.SetProductCode))
            {
                var codeKey = BuildHqStoreMultiCodeKey(
                    storeCode,
                    productCode,
                    productSetCode.SetProductCode
                );
                if (storeMultiByCode.TryGetValue(codeKey, out var byCode))
                    return byCode;
            }

            var barcodeKey = NormalizeCaseInsensitiveValue(productSetCode.SetBarcode);
            if (barcodeKey == null)
                return null;

            return storeMultiByBarcode.TryGetValue(
                BuildHqStoreMultiCodeKey(storeCode, productCode, barcodeKey),
                out var byBarcode
            )
                ? byBarcode
                : null;
        }

        private static DIC_分店一品多码表? FindExistingHqStoreMultiCode(
            string storeCode,
            string productCode,
            ProductSetCode productSetCode,
            Dictionary<string, DIC_分店一品多码表> hqStoreByCode,
            Dictionary<string, DIC_分店一品多码表> hqStoreByBarcode
        )
        {
            if (!string.IsNullOrWhiteSpace(productSetCode.SetProductCode))
            {
                var codeKey = BuildHqStoreMultiCodeKey(
                    storeCode,
                    productCode,
                    productSetCode.SetProductCode
                );
                if (hqStoreByCode.TryGetValue(codeKey, out var byCode))
                    return byCode;
            }

            var barcodeKey = NormalizeCaseInsensitiveValue(productSetCode.SetBarcode);
            if (barcodeKey == null)
                return null;

            return hqStoreByBarcode.TryGetValue(
                BuildHqStoreMultiCodeKey(storeCode, productCode, barcodeKey),
                out var byBarcode
            )
                ? byBarcode
                : null;
        }

        private static DIC_一品多码表 BuildHqProductSetCode(
            PreparedSyncItem item,
            ProductSetCode productSetCode,
            string updatedBy,
            DateTime now
        )
        {
            var detail = item.Detail;
            var product = item.Product;
            return new DIC_一品多码表
            {
                HGUID = productSetCode.SetCodeId,
                H商品编码 = product.ProductCode ?? string.Empty,
                H多码商品编号 = productSetCode.SetProductCode ?? string.Empty,
                H供应商编码 = product.LocalSupplierCode ?? detail.SupplierCode ?? string.Empty,
                H主条形码 = product.Barcode ?? detail.Barcode ?? string.Empty,
                H多条形码 = productSetCode.SetBarcode ?? string.Empty,
                H进货价 = productSetCode.SetPurchasePrice ?? product.PurchasePrice ?? detail.PurchasePrice ?? 0,
                H一品多码零售价 =
                    productSetCode.SetRetailPrice ?? product.RetailPrice ?? ResolveRetailPrice(detail),
                H使用状态 = productSetCode.IsActive,
                H是否自动定价 = product.IsAutoPricing,
                FGC_Creator = updatedBy,
                FGC_CreateDate = now,
                FGC_LastModifier = updatedBy,
                FGC_LastModifyDate = now,
                FGC_UpdateHelp = "分店进货单更新HQ商品同步",
            };
        }

        private static DIC_分店一品多码表 BuildHqStoreMultiCode(
            PreparedSyncItem item,
            ProductSetCode productSetCode,
            StoreMultiCodeProduct? storeMultiCode,
            string storeCode,
            string updatedBy,
            DateTime now
        )
        {
            var detail = item.Detail;
            var product = item.Product;
            var productCode = product.ProductCode ?? string.Empty;
            var multiCodeProductCode =
                storeMultiCode?.MultiCodeProductCode
                ?? productSetCode.SetProductCode
                ?? UuidHelper.GenerateUuid7();
            return new DIC_分店一品多码表
            {
                HGUID = storeMultiCode?.UUID ?? UuidHelper.GenerateUuid7(),
                H分店代码 = storeCode,
                H商品编码 = productCode,
                H分店商品编码 = BuildStoreProductCode(storeCode, productCode),
                H多码商品编码 = multiCodeProductCode,
                H分店多码商品编码 =
                    storeMultiCode?.StoreMultiCodeProductCode ?? storeCode + multiCodeProductCode,
                H供应商编码 = product.LocalSupplierCode ?? detail.SupplierCode ?? string.Empty,
                H主条形码 = product.Barcode ?? detail.Barcode ?? string.Empty,
                H多条形码 = storeMultiCode?.MultiBarcode ?? productSetCode.SetBarcode ?? string.Empty,
                H进货价 =
                    storeMultiCode?.PurchasePrice
                    ?? productSetCode.SetPurchasePrice
                    ?? product.PurchasePrice
                    ?? detail.PurchasePrice
                    ?? 0,
                H折扣率 = storeMultiCode?.DiscountRate ?? detail.DiscountRate ?? 0,
                H一品多码零售价 =
                    storeMultiCode?.MultiCodeRetailPrice
                    ?? productSetCode.SetRetailPrice
                    ?? product.RetailPrice
                    ?? ResolveRetailPrice(detail),
                H库存 = 0,
                H库存金额 = 0,
                H自动新价格 = detail.NewAutoRetailPrice ?? 0,
                H库存预警数 = 0,
                H商品缺货日期 = DateTime.MinValue,
                H是否缺货状态 = false,
                H最小订货量 = 0,
                H最小订货量合计金额 = 0,
                H活动类型 = string.Empty,
                H满减活动代码 = string.Empty,
                H活动开始日期 = DateTime.MinValue,
                H活动结束日期 = DateTime.MinValue,
                H满减数量 = 0,
                H满减金额 = 0,
                H是否自动定价 = storeMultiCode?.IsAutoPricing ?? product.IsAutoPricing,
                H是否特殊商品 = storeMultiCode?.IsSpecialProduct ?? product.IsSpecialProduct,
                H商品柜组号 = string.Empty,
                H使用状态 = storeMultiCode?.IsActive ?? productSetCode.IsActive,
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
            };
        }

        private static void ApplyHqProductSetCode(
            DIC_一品多码表 existing,
            DIC_一品多码表 mapped
        )
        {
            existing.HGUID = string.IsNullOrWhiteSpace(existing.HGUID) ? mapped.HGUID : existing.HGUID;
            existing.H商品编码 = mapped.H商品编码;
            existing.H多码商品编号 = mapped.H多码商品编号;
            existing.H供应商编码 = mapped.H供应商编码;
            existing.H主条形码 = mapped.H主条形码;
            existing.H多条形码 = mapped.H多条形码;
            existing.H进货价 = mapped.H进货价;
            existing.H一品多码零售价 = mapped.H一品多码零售价;
            existing.H使用状态 = mapped.H使用状态;
            existing.H是否自动定价 = mapped.H是否自动定价;
            existing.FGC_LastModifier = mapped.FGC_LastModifier;
            existing.FGC_LastModifyDate = mapped.FGC_LastModifyDate;
            existing.FGC_UpdateHelp = mapped.FGC_UpdateHelp;
        }

        private static void ApplyHqStoreMultiCode(
            DIC_分店一品多码表 existing,
            DIC_分店一品多码表 mapped
        )
        {
            var existingStoreMultiCode = existing.H分店多码商品编码;
            existing.HGUID = string.IsNullOrWhiteSpace(existing.HGUID) ? mapped.HGUID : existing.HGUID;
            existing.H分店代码 = mapped.H分店代码;
            existing.H商品编码 = mapped.H商品编码;
            existing.H分店商品编码 = mapped.H分店商品编码;
            existing.H多码商品编码 = mapped.H多码商品编码;
            existing.H分店多码商品编码 = string.IsNullOrWhiteSpace(existingStoreMultiCode)
                ? mapped.H分店多码商品编码
                : existingStoreMultiCode;
            existing.H供应商编码 = mapped.H供应商编码;
            existing.H主条形码 = mapped.H主条形码;
            existing.H多条形码 = mapped.H多条形码;
            existing.H进货价 = mapped.H进货价;
            existing.H折扣率 = mapped.H折扣率;
            existing.H一品多码零售价 = mapped.H一品多码零售价;
            existing.H自动新价格 = mapped.H自动新价格;
            existing.H是否自动定价 = mapped.H是否自动定价;
            existing.H是否特殊商品 = mapped.H是否特殊商品;
            existing.H使用状态 = mapped.H使用状态;
            existing.FGC_LastModifier = mapped.FGC_LastModifier;
            existing.FGC_LastModifyDate = mapped.FGC_LastModifyDate;
        }

        private static string BuildHqStoreMultiCodeKey(
            string? storeCode,
            string? productCode,
            string? multiCodeOrBarcode
        )
        {
            return string.Join(
                "\u001f",
                NormalizeCaseInsensitiveValue(storeCode) ?? string.Empty,
                NormalizeCaseInsensitiveValue(productCode) ?? string.Empty,
                NormalizeCaseInsensitiveValue(multiCodeOrBarcode) ?? string.Empty
            );
        }

        private static DIC_商品零售价表 BuildHqStorePrice(
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            string storeCode,
            string updatedBy,
            DateTime now,
            string? hqProductCode = null
        )
        {
            var productCode = hqProductCode ?? product.ProductCode!;
            return new DIC_商品零售价表
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
                H是否自动定价 = detail.AutoPricing ?? false,
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
            };
        }

        private static bool ApplySelectedHqFields(
            DIC_商品零售价表 price,
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            UpdateToStorePricesFields updateFields,
            UpdateHqProductsResult result,
            string storeCode
        )
        {
            var updated = false;
            var skippedFields = new List<string>();
            if (updateFields.UpdatePurchasePrice)
            {
                var value = ResolvePurchasePriceForUpdate(detail, product, updateFields);
                if (IsPositiveValue(value))
                {
                    price.H进货价 = value!.Value;
                    result.HqPurchasePricesUpdated++;
                    updated = true;
                }
                else
                {
                    skippedFields.Add("进货价为空或为0");
                }
            }

            if (updateFields.UpdateRetailPrice)
            {
                var value = ResolveRetailPriceForUpdate(detail, updateFields);
                if (IsPositiveValue(value))
                {
                    price.H分店零售价 = value!.Value;
                    result.HqRetailPricesUpdated++;
                    updated = true;
                }
                else
                {
                    skippedFields.Add("零售价为空或为0");
                }
            }

            if (updateFields.UpdateIsAutoPricing)
            {
                var value = ResolveAutoPricingForUpdate(detail, updateFields);
                if (value.HasValue)
                {
                    price.H是否自动定价 = value.Value;
                    result.HqAutoPricingUpdated++;
                    updated = true;
                }
                else
                {
                    skippedFields.Add("自动定价为空");
                }
            }

            if (updateFields.UpdateIsSpecialProduct)
            {
                var value = ResolveSpecialProductForUpdate(detail, updateFields);
                if (value.HasValue)
                {
                    price.H是否特殊商品 = value.Value;
                    result.HqSpecialProductsUpdated++;
                    updated = true;
                }
                else
                {
                    skippedFields.Add("特殊商品为空");
                }
            }

            if (updateFields.UpdateDiscountRate)
            {
                var value = ResolveDiscountRateForUpdate(detail, updateFields);
                if (IsPositiveValue(value))
                {
                    price.H折扣率 = value!.Value;
                    result.HqDiscountRatesUpdated++;
                    updated = true;
                }
                else
                {
                    skippedFields.Add("折扣率为空或为0");
                }
            }

            if (!updated)
            {
                AddSkipped(result, detail.DetailGUID, storeCode, string.Join("，", skippedFields));
            }

            return updated;
        }

        private static bool ApplyAllHqFieldsForInsert(
            DIC_商品零售价表 price,
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            UpdateToStorePricesFields updateFields,
            UpdateHqProductsResult result,
            string storeCode
        )
        {
            // 新插入价格行没有旧值可保留，使用本次更新字段优先补齐整条HQ分店价格记录。
            return ApplySelectedHqFields(price, detail, product, updateFields, result, storeCode);
        }

        private static bool HasAnyUpdateField(UpdateToStorePricesFields updateFields)
        {
            return updateFields.UpdatePurchasePrice
                || updateFields.UpdateRetailPrice
                || updateFields.UpdateIsAutoPricing
                || updateFields.UpdateIsSpecialProduct
                || updateFields.UpdateDiscountRate;
        }

        private static decimal? ResolvePurchasePriceForUpdate(
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            UpdateToStorePricesFields updateFields
        )
        {
            return NormalizePositiveValue(updateFields.PurchasePrice ?? detail.PurchasePrice ?? product.PurchasePrice);
        }

        private static decimal? ResolveRetailPriceForUpdate(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return NormalizePositiveValue(updateFields.RetailPrice ?? detail.RetailPrice ?? detail.NewAutoRetailPrice);
        }

        private static bool? ResolveAutoPricingForUpdate(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.IsAutoPricing ?? detail.AutoPricing ?? false;
        }

        private static bool? ResolveSpecialProductForUpdate(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.IsSpecialProduct ?? detail.IsSpecialProduct;
        }

        private static decimal? ResolveDiscountRateForUpdate(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return NormalizePositiveValue(updateFields.DiscountRate ?? detail.DiscountRate);
        }

        private static decimal? NormalizePositiveValue(decimal? value)
        {
            return IsPositiveValue(value) ? value : null;
        }

        private static bool IsPositiveValue(decimal? value)
        {
            return value.HasValue && value.Value > 0;
        }

        private static decimal ResolvePurchasePriceForInsert(
            StoreLocalSupplierInvoiceDetails detail,
            Product product,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.PurchasePrice ?? detail.PurchasePrice ?? product.PurchasePrice ?? 0;
        }

        private static decimal ResolveRetailPriceForInsert(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.RetailPrice ?? ResolveRetailPrice(detail);
        }

        private static bool ResolveAutoPricingForInsert(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.IsAutoPricing ?? detail.AutoPricing ?? false;
        }

        private static bool ResolveSpecialProductForInsert(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.IsSpecialProduct ?? detail.IsSpecialProduct ?? false;
        }

        private static decimal ResolveDiscountRateForInsert(
            StoreLocalSupplierInvoiceDetails detail,
            UpdateToStorePricesFields updateFields
        )
        {
            return updateFields.DiscountRate ?? detail.DiscountRate ?? 0;
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

        private static string? NormalizeCaseInsensitiveValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return value.Trim().ToUpperInvariant();
        }

        private static string? NormalizeCode(string? value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static async Task<List<PreparedSyncItem>> AttachDomesticSupplierCodesAsync(
            ISqlSugarClient db,
            List<PreparedSyncItem> items
        )
        {
            if (items.Count == 0)
                return items;

            var supplierCodes = await ResolveDomesticSupplierCodesAsync(
                db,
                items.Select(item => item.Product.ProductCode)
            );

            return items
                .Select(item =>
                {
                    var productCode = NormalizeCode(item.Product.ProductCode);
                    // CBP 供应商编码只认国内商品主档供应商，批量预取避免明细循环内反复查库。
                    string? domesticSupplierCode = null;
                    if (productCode != null)
                    {
                        supplierCodes.TryGetValue(productCode, out domesticSupplierCode);
                    }

                    return item with { DomesticSupplierCode = domesticSupplierCode };
                })
                .ToList();
        }

        private static async Task<Dictionary<string, string>> ResolveDomesticSupplierCodesAsync(
            ISqlSugarClient db,
            IEnumerable<string?> productCodes
        )
        {
            var normalizedProductCodes = productCodes
                .Select(NormalizeCode)
                .OfType<string>()
                .Distinct()
                .ToList();
            if (normalizedProductCodes.Count == 0)
                return new Dictionary<string, string>();

            var products = await db.Queryable<DomesticProduct>()
                .Where(product =>
                    normalizedProductCodes.Contains(product.ProductCode)
                    && product.IsDeleted == false
                )
                .Select(product => new { product.ProductCode, product.SupplierCode })
                .ToListAsync();

            return products
                .Select(product => new
                {
                    ProductCode = NormalizeCode(product.ProductCode),
                    SupplierCode = NormalizeCode(product.SupplierCode),
                })
                .Where(product => product.ProductCode != null && product.SupplierCode != null)
                .GroupBy(product => product.ProductCode!)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().SupplierCode!
                );
        }

        private static async Task PatchHqCbpSupplierCodeIfNeededAsync(
            ISqlSugarClient hqDb,
            DIC_商品信息字典表 hqProduct,
            string? domesticSupplierCode,
            string updatedBy,
            DateTime now
        )
        {
            if (
                string.IsNullOrWhiteSpace(domesticSupplierCode)
                || !ShouldPatchHqCbpSupplierCode(hqProduct.CBP供应商编码)
            )
            {
                return;
            }

            await hqDb.Updateable<DIC_商品信息字典表>()
                .SetColumns(row => new DIC_商品信息字典表
                {
                    // CBP 供应商编码对应国内供应商，不能沿用本地默认供应商 200。
                    CBP供应商编码 = domesticSupplierCode,
                    FGC_LastModifier = updatedBy,
                    FGC_LastModifyDate = now,
                })
                .Where(row => row.ID == hqProduct.ID)
                .ExecuteCommandAsync();
        }

        private static bool ShouldPatchHqCbpSupplierCode(string? value)
        {
            var normalized = NormalizeCode(value);
            return normalized == null || normalized == "200";
        }

        private static ISugarQueryable<Product> ApplySupplierFilter(
            ISugarQueryable<Product> query,
            string? supplierCode
        )
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                return query;

            return query.Where(product => product.LocalSupplierCode == supplierCode);
        }

        private static ISugarQueryable<Product> ApplyCaseInsensitiveCodeFilter(
            ISugarQueryable<Product> query,
            string? itemNumber,
            string? barcode
        )
        {
            var normalizedItemNumber = NormalizeCaseInsensitiveValue(itemNumber);
            var normalizedBarcode = NormalizeCaseInsensitiveValue(barcode);

            if (normalizedItemNumber != null && normalizedBarcode != null)
            {
                return query.Where(product =>
                    SqlFunc.ToUpper(product.ItemNumber) == normalizedItemNumber
                    || SqlFunc.ToUpper(product.Barcode) == normalizedBarcode
                );
            }

            if (normalizedItemNumber != null)
            {
                return query.Where(product =>
                    SqlFunc.ToUpper(product.ItemNumber) == normalizedItemNumber
                );
            }

            return query.Where(product => SqlFunc.ToUpper(product.Barcode) == normalizedBarcode);
        }

        private static async Task<Product?> FindExistingProductAsync(
            ISqlSugarClient db,
            string? productCode,
            string? supplierCode,
            string? itemNumber,
            string? barcode
        )
        {
            if (!string.IsNullOrWhiteSpace(productCode))
            {
                var productByCode = await db.Queryable<Product>()
                    .Where(product => product.ProductCode == productCode && product.IsDeleted == false)
                    .FirstAsync();
                if (productByCode != null)
                    return productByCode;
            }

            if (string.IsNullOrWhiteSpace(itemNumber) && string.IsNullOrWhiteSpace(barcode))
                return null;

            var productQuery = ApplySupplierFilter(
                db.Queryable<Product>().Where(product => product.IsDeleted == false),
                supplierCode
            );

            productQuery = ApplyCaseInsensitiveCodeFilter(productQuery, itemNumber, barcode);
            return await productQuery.FirstAsync();
        }

        private static async Task<DIC_商品信息字典表?> FindExistingHqProductAsync(
            ISqlSugarClient hqDb,
            string? productCode,
            string? supplierCode,
            string? itemNumber,
            string? barcode
        )
        {
            if (!string.IsNullOrWhiteSpace(productCode))
            {
                var productByCode = await hqDb.Queryable<DIC_商品信息字典表>()
                    .Where(product => product.H商品编码 == productCode)
                    .FirstAsync();
                if (productByCode != null)
                    return productByCode;
            }

            if (string.IsNullOrWhiteSpace(itemNumber) && string.IsNullOrWhiteSpace(barcode))
                return null;

            var normalizedItemNumber = NormalizeCaseInsensitiveValue(itemNumber);
            var normalizedBarcode = NormalizeCaseInsensitiveValue(barcode);
            var query = hqDb.Queryable<DIC_商品信息字典表>();

            if (!string.IsNullOrWhiteSpace(supplierCode))
                query = query.Where(product => product.H供货商编码 == supplierCode);

            // HQ 商品编码可能与本地编码不同，插入前按业务唯一字段兜底避免大小写重复。
            if (normalizedItemNumber != null && normalizedBarcode != null)
            {
                query = query.Where(product =>
                    SqlFunc.ToUpper(product.H货号) == normalizedItemNumber
                    || SqlFunc.ToUpper(product.H主条形码) == normalizedBarcode
                );
            }
            else if (normalizedItemNumber != null)
            {
                query = query.Where(product => SqlFunc.ToUpper(product.H货号) == normalizedItemNumber);
            }
            else
            {
                query = query.Where(product => SqlFunc.ToUpper(product.H主条形码) == normalizedBarcode);
            }

            return await query.FirstAsync();
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

        private static void AddSkipped(
            EnsureHqProductsResult result,
            string detailGuid,
            string? storeCode,
            string message
        )
        {
            result.Skipped++;
            result.Errors.Add(new EnsureHqProductError
            {
                DetailGuid = detailGuid,
                StoreCode = storeCode,
                Message = $"{message}，已跳过",
            });
        }

        private sealed record PreparedSyncItem(
            StoreLocalSupplierInvoiceDetails Detail,
            Product Product,
            bool IsNewProduct,
            string? DomesticSupplierCode = null
        );
    }
}
