using BlazorApp.Api.Data;
using BlazorApp.Shared;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 商品分店同步服务
    /// 用于将商品的价格信息同步到指定分店的 StoreMultiCodeProduct 和 StoreRetailPrice 表
    /// </summary>
    public class ProductStoreSyncService : IProductStoreSyncService
    {
        private readonly SqlSugarContext _db;
        private readonly ILogger<ProductStoreSyncService> _logger;

        public ProductStoreSyncService(SqlSugarContext db, ILogger<ProductStoreSyncService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// 同步商品到分店
        /// 将选中的商品（进货价、零售价、是否自动定价、是否特殊商品、折扣率）同步到指定分店
        /// 同时同步 StoreMultiCodeProduct 和 StoreRetailPrice 两张表
        /// StoreMultiCodeProduct 数据来自 ProductSetCode（产品套装多码表）
        /// StoreRetailPrice 数据来自 Product（产品表）
        /// 如果目标分店不存在该商品记录，则创建包含所有字段的新记录
        /// 如果目标分店已存在该商品记录，则只更新选中的字段
        /// </summary>
        /// <param name="request">同步请求参数</param>
        /// <returns>同步结果</returns>
        public async Task<ApiResponse<SyncProductsToStoresResult>> SyncProductsToStoresAsync(
            SyncProductsToStoresRequest request
        )
        {
            try
            {
                // 验证：商品编码列表不能为空
                if (request.ProductCodes == null || request.ProductCodes.Count == 0)
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "商品编码列表不能为空",
                        "VALIDATION_ERROR"
                    );
                }

                // 验证：目标分店编码列表不能为空
                if (request.StoreCodes == null || request.StoreCodes.Count == 0)
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "目标分店编码列表不能为空",
                        "VALIDATION_ERROR"
                    );
                }

                // 验证：至少选择一个要同步的字段
                if (
                    !request.SyncPurchasePrice
                    && !request.SyncRetailPrice
                    && !request.SyncIsAutoPricing
                    && !request.SyncIsSpecialProduct
                    && !request.SyncDiscountRate
                )
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "请至少选择一个要同步的字段",
                        "VALIDATION_ERROR"
                    );
                }

                // 初始化同步结果
                var result = new SyncProductsToStoresResult
                {
                    TotalProducts = request.ProductCodes.Count,
                    TotalStores = request.StoreCodes.Count,
                };

                // 获取数据库表操作对象
                var productDb = _db.ProductDb;
                var storeMultiCodeProductDb = _db.StoreMultiCodeProductDb;
                var storeRetailPriceDb = _db.StoreRetailPriceDb;
                var productSetCodeDb = _db.ProductSetCodeDb;

                // 批量查询：获取所有选中的有效商品（未删除的）
                var products = await productDb
                    .AsQueryable()
                    .Where(p => request.ProductCodes.Contains(p.ProductCode))
                    .Where(p => p.IsDeleted == false)
                    .ToListAsync();

                // 验证：商品是否存在
                if (products.Count == 0)
                {
                    return ApiResponse<SyncProductsToStoresResult>.Error(
                        "未找到有效的商品",
                        "NOT_FOUND"
                    );
                }

                // 提取商品编码列表
                var productCodes = products.Select(p => p.ProductCode).ToList();

                // 过滤出需要同步 StoreMultiCodeProduct 的商品（套装或多码商品，ProductType != 0 && ProductType != null）
                var productsNeedMultiCodeSync = products
                    .Where(p => p.ProductType != null && p.ProductType != 0)
                    .ToList();

                // ==================== StoreMultiCodeProduct 同步（仅套装和多码商品，数据来自 ProductSetCode）====================

                // 如果没有套装或多码商品，跳过 StoreMultiCodeProduct 同步
                if (productsNeedMultiCodeSync.Count == 0)
                {
                    _logger.LogInformation(
                        "选中的商品均为普通商品，跳过 StoreMultiCodeProduct 同步"
                    );
                }
                else
                {
                    // 获取需要同步的商品编码列表
                    var productCodesNeedMultiCodeSync = productsNeedMultiCodeSync
                        .Select(p => p.ProductCode)
                        .ToList();

                    // 批量查询 ProductSetCode 获取所有套装/多码编码记录
                    var productSetCodes = await productSetCodeDb
                        .AsQueryable()
                        .Where(p => productCodesNeedMultiCodeSync.Contains(p.ProductCode))
                        .Where(p => p.IsActive == true && p.IsDeleted == false)
                        .ToListAsync();

                    // 批量查询目标分店中已存在的 StoreMultiCodeProduct 记录
                    var existingMultiCodeRecords = await storeMultiCodeProductDb
                        .AsQueryable()
                        .Where(p => request.StoreCodes.Contains(p.StoreCode))
                        .Where(p => productCodesNeedMultiCodeSync.Contains(p.ProductCode))
                        .ToListAsync();

                    // 准备新增和更新列表
                    var newMultiCodeRecords = new List<StoreMultiCodeProduct>();
                    var updateMultiCodeRecords = new List<StoreMultiCodeProduct>();

                    // 使用 SelectMany 展平循环：为每个 ProductSetCode 和每个分店创建或更新记录
                    var productStoreCombinations = productSetCodes.SelectMany(
                        psc => request.StoreCodes,
                        (psc, sc) => new { ProductSetCode = psc, StoreCode = sc }
                    );

                    foreach (var combination in productStoreCombinations)
                    {
                        var productSetCode = combination.ProductSetCode;
                        var storeCode = combination.StoreCode;

                        // 查找当前分店是否已存在该多码商品记录（根据 StoreCode + MultiCodeProductCode 匹配）
                        var existingRecord = existingMultiCodeRecords.FirstOrDefault(p =>
                            p.StoreCode == storeCode
                            && p.MultiCodeProductCode == productSetCode.SetProductCode
                        );

                        if (existingRecord == null)
                        {
                            // 不存在：创建新记录，数据来自 ProductSetCode
                            var newRecord = new StoreMultiCodeProduct
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = storeCode,
                                // 主商品编码
                                ProductCode = productSetCode.ProductCode,
                                // 多码商品编码
                                MultiCodeProductCode = productSetCode.SetProductCode,
                                // 分店多码商品编码（包含分店编码）
                                StoreMultiCodeProductCode =
                                    storeCode + productSetCode.SetProductCode,
                                // 条码
                                MultiBarcode = productSetCode.SetBarcode,
                                // 进货价（来自 ProductSetCode.SetPurchasePrice）
                                PurchasePrice = request.SyncPurchasePrice
                                    ? productSetCode.SetPurchasePrice
                                    : null,
                                // 零售价（来自 ProductSetCode.SetRetailPrice）
                                MultiCodeRetailPrice = request.SyncRetailPrice
                                    ? productSetCode.SetRetailPrice
                                    : null,
                                // 多码商品默认不自动定价
                                IsAutoPricing = false,
                                // 新记录默认启用
                                IsActive = true,

                                // 审计字段
                                CreatedBy = "System",
                                CreatedAt = DateTime.Now,
                            };
                            newMultiCodeRecords.Add(newRecord);
                        }
                        else
                        {
                            // 存在：只更新选中的字段
                            if (request.SyncPurchasePrice)
                                existingRecord.PurchasePrice = productSetCode.SetPurchasePrice;
                            if (request.SyncRetailPrice)
                                existingRecord.MultiCodeRetailPrice = productSetCode.SetRetailPrice;

                            existingRecord.MultiBarcode = productSetCode.SetBarcode;
                            // 审计字段
                            existingRecord.UpdatedBy = "System";
                            existingRecord.UpdatedAt = DateTime.Now;
                            updateMultiCodeRecords.Add(existingRecord);
                        }
                    }

                    // 批量插入 StoreMultiCodeProduct 新记录
                    if (newMultiCodeRecords.Count > 0)
                    {
                        await _db.Db
                            .Fastest<StoreMultiCodeProduct>()
                            .PageSize(1000)
                            .BulkCopyAsync(newMultiCodeRecords);
                        result.StoreMultiCodeProductCreatedCount = newMultiCodeRecords.Count;
                        result.CreatedCount += newMultiCodeRecords.Count;
                    }

                    // 批量更新 StoreMultiCodeProduct 已存在记录
                    if (updateMultiCodeRecords.Count > 0)
                    {
                        await _db.Db
                            .Fastest<StoreMultiCodeProduct>()
                            .BulkUpdateAsync(updateMultiCodeRecords);
                        result.StoreMultiCodeProductUpdatedCount = updateMultiCodeRecords.Count;
                        result.UpdatedCount += updateMultiCodeRecords.Count;
                    }
                }

                // ==================== StoreRetailPrice 同步（数据来自 Product）====================

                // 批量查询：获取目标分店中已存在的 StoreRetailPrice 记录
                var existingRetailPriceRecords = await storeRetailPriceDb
                    .AsQueryable()
                    .Where(p => request.StoreCodes.Contains(p.StoreCode))
                    .Where(p => productCodes.Contains(p.ProductCode))
                    .ToListAsync();

                // 准备 StoreRetailPrice 的新增和更新列表
                var newRetailPriceRecords = new List<StoreRetailPrice>();
                var updateRetailPriceRecords = new List<StoreRetailPrice>();

                // 使用 SelectMany 展平循环：为每个商品和每个分店创建或更新 StoreRetailPrice 记录
                var retailPriceCombinations = products.SelectMany(
                    p => request.StoreCodes,
                    (p, sc) => new { Product = p, StoreCode = sc }
                );

                foreach (var combination in retailPriceCombinations)
                {
                    var product = combination.Product;
                    var storeCode = combination.StoreCode;

                    // StoreProductCode = StoreCode + ProductCode (拼接)
                    var storeProductCode = storeCode + product.ProductCode;

                    // 查找当前分店是否已存在该商品记录
                    var existingRecord = existingRetailPriceRecords.FirstOrDefault(p =>
                        p.StoreCode == storeCode && p.ProductCode == product.ProductCode
                    );

                    if (existingRecord == null)
                    {
                        // 不存在：创建新记录，数据来自 Product
                        var newRecord = new StoreRetailPrice
                        {
                            UUID = UuidHelper.GenerateUuid7(),
                            StoreCode = storeCode,
                            ProductCode = product.ProductCode,
                            StoreProductCode = storeProductCode,
                            // 供应商编码
                            SupplierCode = product.LocalSupplierCode,

                            // 进货价（来自 Product）
                            PurchasePrice = request.SyncPurchasePrice
                                ? product.PurchasePrice
                                : null,
                            // 零售价（来自 Product）
                            StoreRetailPriceValue = request.SyncRetailPrice
                                ? product.RetailPrice
                                : null,
                            // 是否自动定价（来自 Product）
                            IsAutoPricing = request.SyncIsAutoPricing
                                ? product.IsAutoPricing
                                : false,
                            // 是否特殊商品（来自 Product）
                            IsSpecialProduct = request.SyncIsSpecialProduct
                                ? product.IsSpecialProduct
                                : false,
                            // 折扣率（Product 默认没有折扣率字段，设为 null）
                            DiscountRate = request.SyncDiscountRate ? null : null,
                            // 新记录默认启用
                            IsActive = true,

                            // 审计字段
                            CreatedBy = "System",
                            CreatedAt = DateTime.Now,
                        };
                        newRetailPriceRecords.Add(newRecord);
                    }
                    else
                    {
                        // 存在：只更新选中的字段，保留其他字段不变
                        if (request.SyncPurchasePrice)
                            existingRecord.PurchasePrice = product.PurchasePrice;
                        if (request.SyncRetailPrice)
                            existingRecord.StoreRetailPriceValue = product.RetailPrice;
                        if (request.SyncIsAutoPricing)
                            existingRecord.IsAutoPricing = product.IsAutoPricing;
                        if (request.SyncIsSpecialProduct)
                            existingRecord.IsSpecialProduct = product.IsSpecialProduct;
                        // 审计字段
                        existingRecord.UpdatedBy = "System";
                        existingRecord.UpdatedAt = DateTime.Now;
                        // Product 表没有 DiscountRate 字段，保持不变
                        updateRetailPriceRecords.Add(existingRecord);
                    }
                }

                // 批量插入 StoreRetailPrice 新记录
                if (newRetailPriceRecords.Count > 0)
                {
                    _logger.LogInformation(
                        "开始插入 StoreRetailPrice 新记录，数量: {Count}",
                        newRetailPriceRecords.Count
                    );
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await _db.Db
                        .Fastest<StoreRetailPrice>()
                        .PageSize(1000)
                        .BulkCopyAsync(newRetailPriceRecords);
                    sw.Stop();
                    _logger.LogInformation(
                        "StoreRetailPrice 新记录插入完成，数量: {Count}，耗时: {ElapsedMs}ms",
                        newRetailPriceRecords.Count,
                        sw.ElapsedMilliseconds
                    );
                    result.StoreRetailPriceCreatedCount = newRetailPriceRecords.Count;
                    result.CreatedCount += newRetailPriceRecords.Count;
                }

                // 批量更新 StoreRetailPrice 已存在记录
                if (updateRetailPriceRecords.Count > 0)
                {
                    _logger.LogInformation(
                        "开始更新 StoreRetailPrice 记录，数量: {Count}",
                        updateRetailPriceRecords.Count
                    );
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await _db.Db
                        .Fastest<StoreRetailPrice>()
                        .BulkUpdateAsync(updateRetailPriceRecords);
                    sw.Stop();
                    _logger.LogInformation(
                        "StoreRetailPrice 记录更新完成，数量: {Count}，耗时: {ElapsedMs}ms",
                        updateRetailPriceRecords.Count,
                        sw.ElapsedMilliseconds
                    );
                    result.StoreRetailPriceUpdatedCount = updateRetailPriceRecords.Count;
                    result.UpdatedCount += updateRetailPriceRecords.Count;
                }

                // 返回同步成功结果
                return ApiResponse<SyncProductsToStoresResult>.OK(result, "同步成功");
            }
            catch (Exception ex)
            {
                // 记录错误日志并返回失败结果
                _logger.LogError(ex, "同步商品到分店失败");
                return ApiResponse<SyncProductsToStoresResult>.Error(
                    "同步失败: " + ex.Message,
                    "DATABASE_ERROR"
                );
            }
        }
    }
}
