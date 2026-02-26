using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Helper;
using BlazorApp.Api.Data;
using SqlSugar;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 商品同步服务实现类
    /// 负责处理商品检测、批量创建、批量更新等业务逻辑
    /// </summary>
    public class ProductSyncService : IProductSyncService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<ProductSyncService> _logger;

        public ProductSyncService(SqlSugarContext context, ILogger<ProductSyncService> logger)
        {
            _db = context.Db;
            _logger = logger;
        }

        #region 检测商品

        /// <summary>
        /// 批量检测商品是否存在
        /// 根据商品编码(ProductCode)和货号(ItemNumber)检测商品是否在WarehouseProduct表中存在
        /// 如果存在，返回仓库商品的价格、体积等信息
        /// </summary>
        /// <param name="request">检测请求</param>
        /// <returns>检测结果</returns>
        public async Task<BatchProductOperationResponse> DetectProductsAsync(BatchProductDetectionRequest request)
        {
            try
            {
                _logger.LogInformation("开始批量检测商品，共 {Count} 个商品", request.Items.Count);

                // 提取所有商品编码用于批量查询
                var productCodes = request.Items.Select(x => x.ProductCode).ToList();
                var itemNumbers = request.Items.Select(x => x.ItemNumber).ToList();

                // 批量查询WarehouseProduct，同时关联查询Product信息
                var warehouseProducts = await _db.Queryable<WarehouseProduct>()
                    .LeftJoin<Product>((w, p) => w.ProductCode == p.ProductCode)
                    .Where((w, p) => (w.ProductCode != null && productCodes.Contains(w.ProductCode)) || (p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber)))
                    .Select((w, p) => new
                    {
                        w.ProductCode,
                        p.ItemNumber,
                        p.Barcode,
                        w.OEMPrice,
                        w.ImportPrice,
                        w.DomesticPrice,
                        w.Volume,
                        w.IsActive,
                        p.EnglishName
                    })
                    .ToListAsync();

                // 构建检测结果列表
                var results = new List<ProductDetectionResultDto>();

                foreach (var item in request.Items)
                {
                    // 查找匹配的仓库商品（通过ProductCode或ItemNumber匹配）
                    var warehouse = warehouseProducts.FirstOrDefault(w =>
                        w.ProductCode == item.ProductCode ||
                        w.ItemNumber == item.ItemNumber);

                    // 判断商品是否存在
                    bool exists = warehouse != null;

                    // 构建检测结果
                    results.Add(new ProductDetectionResultDto
                    {
                        ProductCode = item.ProductCode,
                        ItemNumber = item.ItemNumber,
                        Barcode = item.Barcode,
                        Exists = exists,
                        DetectionResult = exists ? "已存在" : "新商品",
                        // 如果商品存在，填充仓库商品信息
                        WarehouseOEMPrice = warehouse?.OEMPrice,
                        WarehouseImportPrice = warehouse?.ImportPrice,
                        WarehouseDomesticPrice = warehouse?.DomesticPrice,
                        WarehouseVolume = warehouse?.Volume,
                        WarehouseIsActive = warehouse?.IsActive,
                        WarehouseEnglishName = warehouse?.EnglishName
                    });
                }

                _logger.LogInformation("商品检测完成，新商品: {NewCount}，已存在: {ExistCount}",
                    results.Count(r => !r.Exists),
                    results.Count(r => r.Exists));

                return new BatchProductOperationResponse
                {
                    Success = true,
                    Message = $"检测完成，新商品: {results.Count(r => !r.Exists)}，已存在: {results.Count(r => r.Exists)}",
                    Data = results
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量检测商品失败");
                return new BatchProductOperationResponse
                {
                    Success = false,
                    Message = "检测失败：" + ex.Message,
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        #endregion

        #region 批量更新

        /// <summary>
        /// 批量更新仓库商品信息
        /// 更新范围：
        /// 1. WarehouseProduct表：国内价格、进口价格、贴牌价格、单件体积、上架状态
        /// 2. Product表：进货价(PurchasePrice)
        /// 3. StoreRetailPrice表：进货价(PurchasePrice)
        /// 使用事务确保数据一致性，批量内存处理后一次性提交
        /// 🆕 支持商品编码和货号双重匹配：先匹配商品编码，匹配不到则使用货号匹配
        /// </summary>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        public async Task<BatchProductOperationResponse> BatchUpdateWarehouseProductsAsync(BatchProductUpdateRequest request)
        {
            _db.Ado.BeginTran();
            try
            {
                _logger.LogInformation("开始批量更新仓库商品，共 {Count} 个商品", request.Items.Count);

                var errors = new List<string>();
                var updateTime = DateTime.Now;

                // 🔥 第一步：批量查询所有需要更新的WarehouseProduct（支持商品编码和货号双重查询）
                var productCodes = request.Items.Select(x => x.ProductCode).ToList();
                var itemNumbers = request.Items
                    .Where(x => !string.IsNullOrEmpty(x.ItemNumber))
                    .Select(x => x.ItemNumber!)
                    .ToList();

                // 🔥 使用导航属性一次性查询 WarehouseProduct 和关联的 Product
                var allWarehouseProducts = await _db.Queryable<WarehouseProduct>()
                    .Includes(w => w.Product) // 使用导航属性加载关联的 Product
                    .Where(w => productCodes.Contains(w.ProductCode) || 
                               (itemNumbers.Any() && w.Product != null && w.Product.ItemNumber != null && itemNumbers.Contains(w.Product.ItemNumber)))
                    .ToListAsync();

                _logger.LogInformation("查询到 {Count} 个仓库商品", allWarehouseProducts.Count);

                // 转换为字典，方便快速查找
                var warehouseDictByCode = allWarehouseProducts.ToDictionary(w => w.ProductCode);

                // 建立货号到商品编码的映射字典（直接从导航属性获取）
                var itemNumberToProductCodeDict = allWarehouseProducts
                    .Where(w => w.Product != null && !string.IsNullOrEmpty(w.Product.ItemNumber))
                    .GroupBy(w => w.Product!.ItemNumber!)
                    .ToDictionary(g => g.Key, g => g.First().ProductCode);

                // 🔥 第二步：在内存中准备要更新的数据
                var warehousesToUpdate = new List<WarehouseProduct>();
                var productsToUpdate = new List<Product>();
                var productCodesWithImportPrice = new List<string>();

                foreach (var item in request.Items)
                {
                    WarehouseProduct? warehouse = null;
                    string matchType = "商品编码";

                    // 🔥 优先使用商品编码匹配
                    if (!warehouseDictByCode.TryGetValue(item.ProductCode, out warehouse))
                    {
                        // 🔥 如果商品编码匹配不到，尝试使用货号匹配
                        if (!string.IsNullOrEmpty(item.ItemNumber) && 
                            itemNumberToProductCodeDict.TryGetValue(item.ItemNumber, out var matchedProductCode) &&
                            warehouseDictByCode.TryGetValue(matchedProductCode, out warehouse))
                        {
                            matchType = "货号";
                            _logger.LogInformation("商品编码 {RequestProductCode} 未找到，使用货号 {ItemNumber} 匹配到仓库商品 {WarehouseProductCode}", 
                                item.ProductCode, item.ItemNumber, warehouse.ProductCode);
                        }
                    }

                    // 检查商品是否存在
                    if (warehouse == null)
                    {
                        var errorMsg = !string.IsNullOrEmpty(item.ItemNumber)
                            ? $"商品编码 {item.ProductCode} 和货号 {item.ItemNumber} 在仓库中都不存在"
                            : $"商品编码 {item.ProductCode} 在仓库中不存在";
                        errors.Add(errorMsg);
                        _logger.LogWarning(errorMsg);
                        continue;
                    }

                    // 更新仓库商品字段
                    warehouse.DomesticPrice = item.DomesticPrice ?? warehouse.DomesticPrice;
                    warehouse.ImportPrice = item.ImportPrice ?? warehouse.ImportPrice;
                    warehouse.OEMPrice = item.OEMPrice ?? warehouse.OEMPrice;
                    warehouse.Volume = item.Volume ?? warehouse.Volume;
                    warehouse.IsActive = item.IsActive;
                    warehouse.UpdatedAt = updateTime;

                    warehousesToUpdate.Add(warehouse);

                    // 如果有进口价格，记录需要更新Product和StoreRetailPrice的商品编码（使用实际匹配到的商品编码）
                    if (item.ImportPrice.HasValue)
                    {
                        // 🔥 使用仓库中实际的商品编码（因为可能是通过货号匹配的）
                        productCodesWithImportPrice.Add(warehouse.ProductCode);
                        
                        // 准备Product更新数据
                        productsToUpdate.Add(new Product
                        {
                            ProductCode = warehouse.ProductCode, // 使用实际商品编码
                            PurchasePrice = item.ImportPrice.Value,
                            UpdatedAt = updateTime
                        });
                        
                        _logger.LogDebug("准备更新商品 {ProductCode}（通过{MatchType}匹配）的进货价为 {PurchasePrice}", 
                            warehouse.ProductCode, matchType, item.ImportPrice.Value);
                    }
                }

                // 🔥 第三步：批量更新WarehouseProduct
                if (warehousesToUpdate.Any())
                {
                    await _db.Updateable(warehousesToUpdate)
                        .UpdateColumns(w => new { w.DomesticPrice, w.ImportPrice, w.OEMPrice, w.Volume, w.IsActive, w.UpdatedAt })
                        .ExecuteCommandAsync();
                    _logger.LogDebug("批量更新WarehouseProduct完成，共 {Count} 条", warehousesToUpdate.Count);
                }

                // 🔥 第四步：批量更新Product的PurchasePrice
                if (productCodesWithImportPrice.Any())
                {
                    // 批量查询Product
                    var products = await _db.Queryable<Product>()
                        .Where(p => p.ProductCode != null && productCodesWithImportPrice.Contains(p.ProductCode))
                        .ToListAsync();

                    // 在内存中更新
                    var productUpdateDict = productsToUpdate
                        .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                        .ToDictionary(p => p.ProductCode!);
                    foreach (var product in products)
                    {
                        if (product.ProductCode != null && productUpdateDict.TryGetValue(product.ProductCode, out var updateData))
                        {
                            product.PurchasePrice = updateData.PurchasePrice;
                            product.UpdatedAt = updateTime;
                        }
                    }

                    // 批量更新
                    if (products.Any())
                    {
                        await _db.Updateable(products)
                            .UpdateColumns(p => new { p.PurchasePrice, p.UpdatedAt })
                            .ExecuteCommandAsync();
                        _logger.LogDebug("批量更新Product完成，共 {Count} 条", products.Count);
                    }

                    // 🔥 第五步：批量更新StoreRetailPrice
                    var storeRetailPrices = await _db.Queryable<StoreRetailPrice>()
                        .Where(s => s.ProductCode != null && productCodesWithImportPrice.Contains(s.ProductCode))
                        .ToListAsync();

                    // 在内存中更新
                    foreach (var storeRetailPrice in storeRetailPrices)
                    {
                        if (productUpdateDict.TryGetValue(storeRetailPrice.ProductCode!, out var updateData))
                        {
                            storeRetailPrice.PurchasePrice = updateData.PurchasePrice;
                            storeRetailPrice.UpdatedAt = updateTime;
                        }
                    }

                    // 批量更新
                    if (storeRetailPrices.Any())
                    {
                        await _db.Updateable(storeRetailPrices)
                            .UpdateColumns(s => new { s.PurchasePrice, s.UpdatedAt })
                            .ExecuteCommandAsync();
                        _logger.LogDebug("批量更新StoreRetailPrice完成，共 {Count} 条", storeRetailPrices.Count);
                    }
                }

                // 提交事务
                _db.Ado.CommitTran();

                var successCount = warehousesToUpdate.Count;
                _logger.LogInformation("批量更新完成，成功: {SuccessCount}，失败: {FailedCount}",
                    successCount, errors.Count);

                return new BatchProductOperationResponse
                {
                    Success = errors.Count == 0,
                    Message = $"更新完成，成功: {successCount}，失败: {errors.Count}",
                    SuccessCount = successCount,
                    FailedCount = errors.Count,
                    Errors = errors
                };
            }
            catch (Exception ex)
            {
                try
                {
                    _db.Ado.RollbackTran();
                }
                catch
                {
                    // 忽略回滚错误（事务可能已经提交或回滚）
                }
                _logger.LogError(ex, "批量更新仓库商品失败");

                return new BatchProductOperationResponse
                {
                    Success = false,
                    Message = "批量更新失败：" + ex.Message,
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        #endregion

        #region 批量创建

        /// <summary>
        /// 批量创建商品信息（含二次检查和套装商品处理）
        /// 
        /// 创建流程：
        /// 1. 二次检查商品是否已存在（防止并发创建重复）
        /// 2. 在内存中准备所有数据
        /// 3. 批量创建Product记录
        /// 4. 批量创建WarehouseProduct记录
        /// 5. 批量创建StoreRetailPrice记录（所有活跃Store）
        /// 6. 批量检测套装商品并创建ProductSetCode记录
        /// 7. 批量创建StoreMultiCodeProduct记录（套装商品）
        /// 
        /// 使用事务确保所有操作原子性，批量内存处理后一次性提交
        /// </summary>
        /// <param name="request">创建请求</param>
        /// <returns>创建结果</returns>
        public async Task<BatchProductOperationResponse> BatchCreateProductsAsync(BatchProductCreateRequest request)
        {
            _db.Ado.BeginTran();
            try
            {
                _logger.LogInformation("开始批量创建商品，共 {Count} 个商品", request.Items.Count);

                var errors = new List<string>();
                var skippedItems = new List<string>();
                var createTime = DateTime.Now;

                // 🔥 第一步：二次检查 - 批量查询商品是否已存在
                var productCodes = request.Items.Select(x => x.ProductCode).ToList();
                var itemNumbers = request.Items.Select(x => x.ItemNumber).ToList();

                _logger.LogDebug("执行二次检查，商品编码数: {CodeCount}，货号数: {ItemCount}",
                    productCodes.Count, itemNumbers.Count);

                var existingProducts = await _db.Queryable<Product>()
                    .Where(p => (p.ProductCode != null && productCodes.Contains(p.ProductCode)) || (p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber)))
                    .Select(p => new { p.ProductCode, p.ItemNumber })
                    .ToListAsync();

                // 将已存在的商品编码和货号转换为HashSet，便于快速查找
                var existingCodes = existingProducts.Select(p => p.ProductCode).ToHashSet();
                var existingItems = existingProducts.Select(p => p.ItemNumber).ToHashSet();

                _logger.LogInformation("二次检查完成，发现已存在商品: {Count}", existingProducts.Count);

                // 🔥 第二步：查询所有活跃的Store和套装商品信息
                var activeStores = await _db.Queryable<Store>()
                    .Where(s => s.IsActive && !s.IsDeleted)
                    .ToListAsync();

                // 查询所有与请求中商品编码相关的套装商品（DomesticSetProduct）
                var domesticSets = await _db.Queryable<DomesticSetProduct>()
                    .Where(d => d.ProductCode != null && productCodes.Contains(d.ProductCode))
                    .ToListAsync();
                var domesticSetDict = domesticSets.ToDictionary(d => d.ProductCode);

                _logger.LogInformation("查询到活跃店铺数: {StoreCount}，套装商品数: {SetCount}",
                    activeStores.Count, domesticSets.Count);

                // 🔥 第三步：在内存中准备所有要创建的数据
                var productsToCreate = new List<Product>();
                var warehouseProductsToCreate = new List<WarehouseProduct>();
                var storeRetailPricesToCreate = new List<StoreRetailPrice>();
                var productSetCodesToCreate = new List<ProductSetCode>();
                var storeMultiCodesToCreate = new List<StoreMultiCodeProduct>();

                foreach (var item in request.Items)
                {
                    // 二次检查：如果商品已存在，跳过
                    if (existingCodes.Contains(item.ProductCode) || existingItems.Contains(item.ItemNumber))
                    {
                        skippedItems.Add($"{item.ItemNumber} (商品编码或货号已存在)");
                        _logger.LogWarning("商品 {ItemNumber} 已存在，跳过创建", item.ItemNumber);
                        continue;
                    }

                    // 验证：贴牌价格不能为空或小于等于0
                    if (item.OEMPrice <= 0)
                    {
                        errors.Add($"{item.ItemNumber}: 贴牌价格必须大于0");
                        _logger.LogWarning("商品 {ItemNumber} 贴牌价格无效: {OEMPrice}", item.ItemNumber, item.OEMPrice);
                        continue;
                    }

                    // 准备Product数据
                    productsToCreate.Add(new Product
                    {
                        ProductCode = item.ProductCode,
                        ItemNumber = item.ItemNumber,
                        Barcode = item.Barcode,
                        LocalSupplierCode ="200",//默认供应商是hotbargain 这个供应商code
                        ProductName = item.ChineseName ?? item.ItemNumber,
                        EnglishName = item.EnglishName,
                        PurchasePrice = item.ImportPrice,
                        ProductImage = item.ImageUrl,
                        IsAutoPricing = false,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = createTime,
                        UpdatedAt = createTime
                    });

                    // 准备WarehouseProduct数据
                    warehouseProductsToCreate.Add(new WarehouseProduct
                    {
                        ProductCode = item.ProductCode,
                        DomesticPrice = item.DomesticPrice,
                        ImportPrice = item.ImportPrice,
                        OEMPrice = item.OEMPrice,
                        Volume = item.Volume,
                        StockQuantity = 0,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = createTime,
                        UpdatedAt = createTime
                    });

                    // 为每个活跃店铺准备StoreRetailPrice数据
                    foreach (var store in activeStores)
                    {
                        storeRetailPricesToCreate.Add(new StoreRetailPrice
                        {
                            UUID = UuidHelper.GenerateUuid7(),
                            StoreCode = store.StoreCode,
                            ProductCode = item.ProductCode,
                            PurchasePrice = item.ImportPrice,
                            StoreRetailPriceValue = item.OEMPrice,
                            IsActive = true,
                            IsAutoPricing = false,
                            IsDeleted = false,
                            CreatedAt = createTime,
                            UpdatedAt = createTime
                        });
                    }

                    // 如果是套装商品，准备套装相关数据
                    if (domesticSetDict.ContainsKey(item.ItemNumber))
                    {
                        _logger.LogDebug("检测到套装商品: {ItemNumber}", item.ItemNumber);

                        // 准备ProductSetCode数据
                        productSetCodesToCreate.Add(new ProductSetCode
                        {
                            SetCodeId = UuidHelper.GenerateUuid7(),
                            ProductCode = item.ProductCode,
                            SetItemNumber = item.ItemNumber,
                            SetBarcode = item.Barcode,
                            SetPurchasePrice = item.ImportPrice,
                            SetRetailPrice = item.OEMPrice,
                            SetQuantity = 1,
                            SetType = 1, // 1=组合套装
                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = createTime,
                            UpdatedAt = createTime
                        });

                        // 为每个活跃店铺准备StoreMultiCodeProduct数据
                        foreach (var store in activeStores)
                        {
                            storeMultiCodesToCreate.Add(new StoreMultiCodeProduct
                            {
                                UUID = UuidHelper.GenerateUuid7(),
                                StoreCode = store.StoreCode,
                                ProductCode = item.ProductCode,
                                MultiCodeProductCode = item.ProductCode,
                                MultiBarcode = item.Barcode,
                                PurchasePrice = item.ImportPrice,
                                MultiCodeRetailPrice = item.OEMPrice,
                                IsActive = true,
                                IsAutoPricing = false,
                                IsSpecialProduct = false,
                                IsDeleted = false,
                                CreatedAt = createTime,
                                UpdatedAt = createTime
                            });
                        }
                    }
                }

                // 🔥 第四步：批量插入所有数据
                var successCount = 0;

                if (productsToCreate.Any())
                {
                    await _db.Insertable(productsToCreate).ExecuteCommandAsync();
                    _logger.LogDebug("批量插入Product完成，共 {Count} 条", productsToCreate.Count);
                    successCount = productsToCreate.Count;
                }

                if (warehouseProductsToCreate.Any())
                {
                    await _db.Insertable(warehouseProductsToCreate).ExecuteCommandAsync();
                    _logger.LogDebug("批量插入WarehouseProduct完成，共 {Count} 条", warehouseProductsToCreate.Count);
                }

                if (storeRetailPricesToCreate.Any())
                {
                    await _db.Insertable(storeRetailPricesToCreate).ExecuteCommandAsync();
                    _logger.LogDebug("批量插入StoreRetailPrice完成，共 {Count} 条", storeRetailPricesToCreate.Count);
                }

                if (productSetCodesToCreate.Any())
                {
                    await _db.Insertable(productSetCodesToCreate).ExecuteCommandAsync();
                    _logger.LogDebug("批量插入ProductSetCode完成，共 {Count} 条（套装商品）", productSetCodesToCreate.Count);
                }

                if (storeMultiCodesToCreate.Any())
                {
                    await _db.Insertable(storeMultiCodesToCreate).ExecuteCommandAsync();
                    _logger.LogDebug("批量插入StoreMultiCodeProduct完成，共 {Count} 条（套装商品）", storeMultiCodesToCreate.Count);
                }

                // 提交事务
                _db.Ado.CommitTran();

                var message = $"创建完成，成功: {successCount}";
                if (skippedItems.Any())
                {
                    message += $"，跳过已存在: {skippedItems.Count}";
                }
                if (errors.Any())
                {
                    message += $"，失败: {errors.Count}";
                }

                _logger.LogInformation("批量创建完成，{Message}", message);

                return new BatchProductOperationResponse
                {
                    Success = errors.Count == 0,
                    Message = message,
                    SuccessCount = successCount,
                    FailedCount = errors.Count,
                    SkippedCount = skippedItems.Count,
                    Errors = errors,
                    SkippedItems = skippedItems
                };
            }
            catch (Exception ex)
            {
                try
                {
                    _db.Ado.RollbackTran();
                }
                catch
                {
                    // 忽略回滚错误（事务可能已经提交或回滚）
                }
                _logger.LogError(ex, "批量创建商品失败");

                return new BatchProductOperationResponse
                {
                    Success = false,
                    Message = "批量创建失败：" + ex.Message,
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        #endregion
    }
}

