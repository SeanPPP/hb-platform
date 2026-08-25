using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Helper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
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
        private readonly IWarehouseProductChangeHistoryService _changeHistoryService;
        private readonly ICurrentUserService _currentUserService;

        public ProductSyncService(
            SqlSugarContext context,
            ILogger<ProductSyncService> logger,
            IWarehouseProductChangeHistoryService changeHistoryService,
            ICurrentUserService currentUserService
        )
        {
            _db = context.Db;
            _logger = logger;
            _changeHistoryService = changeHistoryService;
            _currentUserService = currentUserService;
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
        /// 1. WarehouseProduct表：国内价格、进口价格、零售价、单件体积、上架状态
        /// 2. Product表：进货价(PurchasePrice)
        /// 3. StoreRetailPrice表：进货价(PurchasePrice)
        /// 使用事务确保数据一致性，批量内存处理后一次性提交
        /// 🆕 支持商品编码和货号双重匹配：先匹配商品编码，匹配不到则使用货号匹配
        /// </summary>
        /// <param name="request">更新请求</param>
        /// <returns>更新结果</returns>
        public async Task<BatchProductOperationResponse> BatchUpdateWarehouseProductsAsync(BatchProductUpdateRequest request)
        {
            try
            {
                _logger.LogInformation("开始批量更新仓库商品，共 {Count} 个商品", request.Items.Count);

                var errors = new List<string>();
                var updateTime = DateTime.UtcNow;
                var batchGuid = Guid.NewGuid();
                var actorName = ResolveActorName();

                // 🔥 第一步：批量查询所有需要更新的WarehouseProduct（支持商品编码和货号双重查询）
                var productCodes = request.Items.Select(x => x.ProductCode).ToList();
                var itemNumbers = request.Items
                    .Where(x => !string.IsNullOrEmpty(x.ItemNumber))
                    .Select(x => x.ItemNumber!)
                    .ToList();

                var itemNumberToProductCodeDict = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );
                var ambiguousItemNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (itemNumbers.Any())
                {
                    // 货号兜底先查本地主档映射，避免在 WarehouseProduct where 中访问导航属性。
                    var productCodeRows = await _db.Queryable<Product>()
                        .Where(p =>
                            p.ItemNumber != null &&
                            p.ProductCode != null &&
                            !p.IsDeleted &&
                            itemNumbers.Contains(p.ItemNumber)
                        )
                        .Select(p => new { p.ItemNumber, p.ProductCode })
                        .ToListAsync();

                    var itemNumberGroups = productCodeRows
                        .Where(p => !string.IsNullOrEmpty(p.ItemNumber) && !string.IsNullOrEmpty(p.ProductCode))
                        .GroupBy(p => p.ItemNumber!, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    ambiguousItemNumbers = itemNumberGroups
                        .Where(group => group
                            .Select(row => row.ProductCode)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count() != 1)
                        .Select(group => group.Key)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    itemNumberToProductCodeDict = itemNumberGroups
                        .Where(group => !ambiguousItemNumbers.Contains(group.Key))
                        .ToDictionary(
                            group => group.Key,
                            group => group.Single().ProductCode!,
                            StringComparer.OrdinalIgnoreCase
                        );
                }

                var lookupProductCodes = productCodes
                    .Concat(itemNumberToProductCodeDict.Values)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct()
                    .ToList();

                var allWarehouseProducts = lookupProductCodes.Any()
                    ? await _db.Queryable<WarehouseProduct>()
                        .Where(w =>
                            w.ProductCode != null
                            && lookupProductCodes.Contains(w.ProductCode)
                            && !w.IsDeleted
                        )
                        .ToListAsync()
                    : new List<WarehouseProduct>();

                _logger.LogInformation("查询到 {Count} 个仓库商品", allWarehouseProducts.Count);

                // 转换为字典，方便快速查找
                var warehouseDictByCode = allWarehouseProducts.ToDictionary(w => w.ProductCode);

                // 先只确定本次请求实际命中的主商品，不能在获取业务锁前修改任何实体。
                var matchedItems = new List<(ProductUpdateItem Item, string ProductCode, string MatchType)>();
                var processedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in request.Items)
                {
                    WarehouseProduct? warehouse = null;
                    string matchType = "商品编码";

                    // 🔥 优先使用商品编码匹配
                    if (!warehouseDictByCode.TryGetValue(item.ProductCode, out warehouse))
                    {
                        if (
                            !string.IsNullOrWhiteSpace(item.ItemNumber)
                            && ambiguousItemNumbers.Contains(item.ItemNumber)
                        )
                        {
                            var error = $"货号 {item.ItemNumber} 同时映射到多个商品，未执行更新";
                            errors.Add(error);
                            _logger.LogWarning(error);
                            continue;
                        }

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

                    if (!processedProductCodes.Add(warehouse.ProductCode))
                    {
                        var duplicateError = $"批次内商品重复: {warehouse.ProductCode}";
                        errors.Add(duplicateError);
                        _logger.LogWarning(duplicateError);
                        continue;
                    }

                    matchedItems.Add((item, warehouse.ProductCode, matchType));
                }

                var matchedProductCodes = matchedItems
                    .Select(match => match.ProductCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var warehousesToUpdate = new List<WarehouseProduct>();
                IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> beforeSnapshots =
                    new Dictionary<string, WarehouseProductChangeSnapshotDto>();

                // 锁前解析只用于确定候选锁集合；事务从这里开始，后续目标和业务键必须锁内复读确认。
                _db.Ado.BeginTran();

                if (matchedProductCodes.Count > 0)
                {
                    // 事务已开启；按主商品稳定顺序取得 gate/product 锁后，锁内重新读取所有会写入的源行。
                    var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                        _db,
                        matchedProductCodes
                    );
                    // 快照可能读取外部状态；所有实际写入目标与货号映射都要在其后锁内复读。
                    beforeSnapshots = await CaptureChangeSnapshotsAsync(matchedProductCodes);
                    // 业务锁内只读取有效主档；软删除记录不能作为更新目标，也不能被本次价格写入复活。
                    var lockedWarehouseQuery = _db.Queryable<WarehouseProduct>()
                        .Where(w =>
                            w.ProductCode != null
                            && matchedProductCodes.Contains(w.ProductCode)
                            && !w.IsDeleted
                        );
                    if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                    {
                        lockedWarehouseQuery = lockedWarehouseQuery.With(SqlWith.UpdLock);
                    }
                    var lockedWarehouses = await lockedWarehouseQuery.ToListAsync();
                    var lockedWarehouseDict = lockedWarehouses.ToDictionary(w => w.ProductCode);
                    var lockedProductsByCodeQuery = _db.Queryable<Product>()
                        .Where(product =>
                            product.ProductCode != null
                            && matchedProductCodes.Contains(product.ProductCode)
                            && !product.IsDeleted
                        );
                    if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                    {
                        lockedProductsByCodeQuery = lockedProductsByCodeQuery.With(SqlWith.UpdLock);
                    }
                    var lockedProductsByCode = (await lockedProductsByCodeQuery.ToListAsync())
                        .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                        .ToDictionary(
                            product => product.ProductCode!,
                            product => product,
                            StringComparer.OrdinalIgnoreCase
                        );
                    var itemNumbersToVerify = matchedItems
                        .Where(match => !string.IsNullOrWhiteSpace(match.Item.ItemNumber))
                        .Select(match => match.Item.ItemNumber!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var lockedProductsByItemNumber = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    if (itemNumbersToVerify.Count > 0)
                    {
                        var itemNumberQuery = _db.Queryable<Product>()
                            .Where(product =>
                                product.ItemNumber != null
                                && product.ProductCode != null
                                && !product.IsDeleted
                                && itemNumbersToVerify.Contains(product.ItemNumber)
                            );
                        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
                        {
                            // ItemNumber 不是本次 applock 的资源键，SQL Server 需额外持有更新锁到事务结束。
                            itemNumberQuery = itemNumberQuery.With(SqlWith.UpdLock);
                        }
                        var lockedItemNumberGroups = (await itemNumberQuery.ToListAsync())
                            .Where(product =>
                                !string.IsNullOrWhiteSpace(product.ItemNumber)
                                && !string.IsNullOrWhiteSpace(product.ProductCode)
                            )
                            .GroupBy(product => product.ItemNumber!, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        lockedProductsByItemNumber = lockedItemNumberGroups
                            .Where(group => group
                                .Select(product => product.ProductCode)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count() == 1)
                            .ToDictionary(
                                group => group.Key,
                                group => group.First().ProductCode!,
                                StringComparer.OrdinalIgnoreCase
                            );
                    }
                    var productPurchasePrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                    foreach (var match in matchedItems)
                    {
                        if (!lockedProductsByCode.TryGetValue(match.ProductCode, out _))
                        {
                            var error = $"商品编码 {match.ProductCode} 在获取业务锁后已不存在或已删除";
                            errors.Add(error);
                            _logger.LogWarning(error);
                            continue;
                        }

                        if (
                            !string.IsNullOrWhiteSpace(match.Item.ItemNumber)
                            && (
                                !lockedProductsByItemNumber.TryGetValue(
                                    match.Item.ItemNumber,
                                    out var lockedMatchedProductCode
                                )
                                || !string.Equals(
                                    lockedMatchedProductCode,
                                    match.ProductCode,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                        )
                        {
                            // 直连 ProductCode 和货号兜底都必须锁内确认映射；变化时不能改写原目标或追加新锁。
                            var error = $"货号 {match.Item.ItemNumber} 在获取业务锁后映射已变化，未写入商品";
                            errors.Add(error);
                            _logger.LogWarning(error);
                            continue;
                        }

                        if (!lockedWarehouseDict.TryGetValue(match.ProductCode, out var warehouse))
                        {
                            var error = $"商品编码 {match.ProductCode} 在获取业务锁后已不存在";
                            errors.Add(error);
                            _logger.LogWarning(error);
                            continue;
                        }

                        warehouse.DomesticPrice = match.Item.DomesticPrice ?? warehouse.DomesticPrice;
                        warehouse.ImportPrice = match.Item.ImportPrice ?? warehouse.ImportPrice;
                        warehouse.OEMPrice = match.Item.OEMPrice ?? warehouse.OEMPrice;
                        warehouse.Volume = match.Item.Volume ?? warehouse.Volume;
                        // 价格类保存不应隐式改变上下架；只有请求明确带状态时才覆盖。
                        if (match.Item.IsActive.HasValue)
                        {
                            warehouse.IsActive = match.Item.IsActive.Value;
                        }
                        warehouse.UpdatedAt = updateTime;
                        warehouse.UpdatedBy = actorName;
                        warehousesToUpdate.Add(warehouse);

                        if (match.Item.ImportPrice.HasValue)
                        {
                            productPurchasePrices[warehouse.ProductCode] = match.Item.ImportPrice.Value;
                        }
                    }

                    if (warehousesToUpdate.Any())
                    {
                        await _db.Updateable(warehousesToUpdate)
                            .UpdateColumns(w => new { w.DomesticPrice, w.ImportPrice, w.OEMPrice, w.Volume, w.IsActive, w.UpdatedAt, w.UpdatedBy })
                            .ExecuteCommandAsync();
                    }

                    if (productPurchasePrices.Count > 0)
                    {
                        var productCodesWithImportPrice = productPurchasePrices.Keys.ToList();
                        var products = await _db.Queryable<Product>()
                            .Where(p =>
                                p.ProductCode != null
                                && productCodesWithImportPrice.Contains(p.ProductCode)
                                && !p.IsDeleted
                            )
                            .ToListAsync();
                        foreach (var product in products)
                        {
                            if (product.ProductCode != null && productPurchasePrices.TryGetValue(product.ProductCode, out var purchasePrice))
                            {
                                product.PurchasePrice = purchasePrice;
                                product.UpdatedAt = updateTime;
                                product.UpdatedBy = actorName;
                            }
                        }
                        if (products.Any())
                        {
                            await _db.Updateable(products)
                                .UpdateColumns(p => new { p.PurchasePrice, p.UpdatedAt, p.UpdatedBy })
                                .ExecuteCommandAsync();
                        }

                        var storeRetailPrices = await _db.Queryable<StoreRetailPrice>()
                            .Where(s =>
                                s.ProductCode != null
                                && productCodesWithImportPrice.Contains(s.ProductCode)
                                && !s.IsDeleted
                            )
                            .ToListAsync();
                        foreach (var storeRetailPrice in storeRetailPrices)
                        {
                            if (storeRetailPrice.ProductCode != null && productPurchasePrices.TryGetValue(storeRetailPrice.ProductCode, out var purchasePrice))
                            {
                                storeRetailPrice.PurchasePrice = purchasePrice;
                                storeRetailPrice.UpdatedAt = updateTime;
                                storeRetailPrice.UpdatedBy = actorName;
                            }
                        }
                        if (storeRetailPrices.Any())
                        {
                            await _db.Updateable(storeRetailPrices)
                                .UpdateColumns(s => new { s.PurchasePrice, s.UpdatedAt, s.UpdatedBy })
                                .ExecuteCommandAsync();
                        }

                        // 成本变化后必须在同一事务和同一业务锁内分摊子项成本；不可重算即整体回滚。
                        var recalculation = await new SetChildPurchasePriceService(_db)
                            .RecalculateLockedAsync(lockScope, productCodesWithImportPrice, null, actorName);
                        EnsureSetChildPurchasePriceRecalculated(recalculation, productCodesWithImportPrice);
                    }
                }

                var changedProductCodes = warehousesToUpdate
                    .Select(item => item.ProductCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var afterSnapshots = await CaptureChangeSnapshotsAsync(changedProductCodes);
                await RecordChangeHistoryAsync(
                    beforeSnapshots,
                    afterSnapshots,
                    "BatchUpdate",
                    "ProductSync",
                    batchGuid,
                    updateTime
                );

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

                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
                {
                    return BuildBusyBatchResponse(request.Items.Count);
                }

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
                var createTime = DateTime.UtcNow;
                var batchGuid = Guid.NewGuid();
                var actorName = ResolveActorName();

                // 事务开始后先按请求商品获取业务锁；后续所有源数据均在锁内重新读取。
                var productCodes = request.Items.Select(x => x.ProductCode).ToList();
                var itemNumbers = request.Items.Select(x => x.ItemNumber).ToList();
                var lockProductCodes = SetChildPurchasePriceMutationLock.NormalizeProductCodes(productCodes);
                var lockScope = lockProductCodes.Count > 0
                    ? await SetChildPurchasePriceMutationLock.AcquireProductsAsync(_db, lockProductCodes)
                    : null;

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

                // 锁内读取门店和国内套装关系，避免以锁外快照创建不完整的子项组。
                var activeStores = await _db.Queryable<Store>()
                    .Where(s => s.IsActive && !s.IsDeleted)
                    .ToListAsync();

                var domesticSets = await _db.Queryable<DomesticSetProduct>()
                    .Where(d => d.ProductCode != null && productCodes.Contains(d.ProductCode) && !d.IsDeleted)
                    .ToListAsync();
                var domesticSetGroups = domesticSets
                    .GroupBy(d => d.ProductCode, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("查询到活跃店铺数: {StoreCount}，套装商品数: {SetCount}",
                    activeStores.Count, domesticSets.Count);

                // 🔥 第三步：在内存中准备所有要创建的数据
                var productsToCreate = new List<Product>();
                var warehouseProductsToCreate = new List<WarehouseProduct>();
                var storeRetailPricesToCreate = new List<StoreRetailPrice>();
                var productSetCodesToCreate = new List<ProductSetCode>();
                var storeMultiCodesToCreate = new List<StoreMultiCodeProduct>();
                var processedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var processedItemNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in request.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.ProductCode) || string.IsNullOrWhiteSpace(item.ItemNumber))
                    {
                        errors.Add("商品编码和货号不能为空");
                        continue;
                    }
                    if (!processedProductCodes.Add(item.ProductCode.Trim()) || !processedItemNumbers.Add(item.ItemNumber.Trim()))
                    {
                        errors.Add($"{item.ItemNumber}: 批次内商品编码或货号重复");
                        continue;
                    }

                    // 二次检查：如果商品已存在，跳过
                    if (existingCodes.Contains(item.ProductCode) || existingItems.Contains(item.ItemNumber))
                    {
                        skippedItems.Add($"{item.ItemNumber} (商品编码或货号已存在)");
                        _logger.LogWarning("商品 {ItemNumber} 已存在，跳过创建", item.ItemNumber);
                        continue;
                    }

                    // 验证：零售价不能为空或小于等于0
                    if (item.OEMPrice <= 0)
                    {
                        errors.Add($"{item.ItemNumber}: 零售价必须大于0");
                        _logger.LogWarning("商品 {ItemNumber} 零售价无效: {OEMPrice}", item.ItemNumber, item.OEMPrice);
                        continue;
                    }

                    // 准备Product数据
                    productsToCreate.Add(new Product
                    {
                        ProductCode = item.ProductCode,
                        ItemNumber = item.ItemNumber,
                        Barcode = item.Barcode,
                        LocalSupplierCode = "200",//默认供应商是hotbargain 这个供应商code
                        ProductName = item.ChineseName ?? item.ItemNumber,
                        EnglishName = item.EnglishName,
                        PurchasePrice = item.ImportPrice,
                        ProductImage = item.ImageUrl,
                        IsAutoPricing = false,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = createTime,
                        UpdatedAt = createTime,
                        CreatedBy = actorName,
                        UpdatedBy = actorName,
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
                        UpdatedAt = createTime,
                        CreatedBy = actorName,
                        UpdatedBy = actorName,
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

                    // 同一主商品可对应多个国内套装子项，必须按 ProductCode 整组创建关系。
                    if (domesticSetGroups.TryGetValue(item.ProductCode, out var domesticSetRows))
                    {
                        EnsureDomesticSetGroupIsValid(item.ProductCode, domesticSetRows);

                        foreach (var domesticSet in domesticSetRows.OrderBy(row => row.SetProductCode, StringComparer.OrdinalIgnoreCase))
                        {
                            // SetType=1 的两张成本只能由统一分摊服务写入，先保持 null。
                            productSetCodesToCreate.Add(new ProductSetCode
                            {
                                SetCodeId = domesticSet.SetProductCode,
                                ProductCode = item.ProductCode,
                                SetProductCode = domesticSet.SetProductCode,
                                SetItemNumber = domesticSet.SetProductNo,
                                SetBarcode = domesticSet.SetBarcode,
                                SetPurchasePrice = null,
                                SetRetailPrice = domesticSet.OEMPrice,
                                SetQuantity = 1,
                                SetType = 1,
                                IsActive = true,
                                IsDeleted = false,
                                CreatedAt = createTime,
                                UpdatedAt = createTime,
                                CreatedBy = actorName,
                                UpdatedBy = actorName,
                            });

                            foreach (var store in activeStores)
                            {
                                storeMultiCodesToCreate.Add(new StoreMultiCodeProduct
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = store.StoreCode,
                                    ProductCode = item.ProductCode,
                                    MultiCodeProductCode = domesticSet.SetProductCode,
                                    StoreMultiCodeProductCode = domesticSet.SetProductCode,
                                    MultiBarcode = domesticSet.SetBarcode,
                                    PurchasePrice = null,
                                    MultiCodeRetailPrice = domesticSet.OEMPrice,
                                    IsActive = true,
                                    IsAutoPricing = false,
                                    IsSpecialProduct = false,
                                    IsDeleted = false,
                                    CreatedAt = createTime,
                                    UpdatedAt = createTime,
                                    CreatedBy = actorName,
                                    UpdatedBy = actorName,
                                });
                            }
                        }
                    }
                }

                var auditProductCodes = warehouseProductsToCreate
                    .Select(item => item.ProductCode)
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var beforeSnapshots = await CaptureChangeSnapshotsAsync(auditProductCodes);

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

                if (productSetCodesToCreate.Any())
                {
                    if (lockScope == null)
                    {
                        throw new InvalidOperationException("创建套装子项前未获取对应的产品业务锁");
                    }

                    var setParentProductCodes = productSetCodesToCreate
                        .Select(row => row.ProductCode)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    // 关系和零售价已经入库，提交前在同一锁与事务内统一分摊两张子项成本。
                    var recalculation = await new SetChildPurchasePriceService(_db)
                        .RecalculateLockedAsync(lockScope, setParentProductCodes, null, actorName);
                    EnsureSetChildPurchasePriceRecalculated(recalculation, setParentProductCodes);
                }

                var afterSnapshots = await CaptureChangeSnapshotsAsync(auditProductCodes);
                await RecordChangeHistoryAsync(
                    beforeSnapshots,
                    afterSnapshots,
                    "Create",
                    "ProductSync",
                    batchGuid,
                    createTime
                );

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

                if (SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out _))
                {
                    return BuildBusyBatchResponse(request.Items.Count);
                }

                return new BatchProductOperationResponse
                {
                    Success = false,
                    Message = "批量创建失败：" + ex.Message,
                    Errors = new List<string> { ex.Message }
                };
            }
        }

        #endregion

        private static BatchProductOperationResponse BuildBusyBatchResponse(int failedCount)
        {
            return new BatchProductOperationResponse
            {
                Success = false,
                Message = "套装商品正在被其他操作修改，请稍后重试",
                SuccessCount = 0,
                FailedCount = failedCount,
                Errors = new List<string>
                {
                    $"{SetChildPurchasePriceMutationLock.BusyErrorCode}: 套装商品正在被其他操作修改，请稍后重试",
                },
            };
        }

        private async Task<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>
            CaptureChangeSnapshotsAsync(IEnumerable<string> productCodes)
        {
            return await _changeHistoryService.CaptureSnapshotsAsync(productCodes);
        }

        private async Task RecordChangeHistoryAsync(
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> beforeSnapshots,
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> afterSnapshots,
            string action,
            string source,
            Guid batchGuid,
            DateTime? occurredAtUtc = null
        )
        {
            await _changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                new WarehouseProductChangeHistoryContextDto
                {
                    Action = action,
                    Source = source,
                    BatchGuid = batchGuid,
                    ActorName = ResolveActorName(),
                    ActorType = ResolveActorType(),
                    ActorUserGuid = ResolveActorGuid(),
                    OccurredAtUtc = occurredAtUtc ?? DateTime.UtcNow,
                }
            );
        }

        private static void EnsureDomesticSetGroupIsValid(
            string productCode,
            IEnumerable<DomesticSetProduct> domesticSetRows
        )
        {
            var rows = domesticSetRows.ToList();
            if (rows.Any(row => string.IsNullOrWhiteSpace(row.SetProductCode)))
            {
                throw new InvalidOperationException($"套装商品 {productCode} 存在空的 SetProductCode");
            }

            var duplicate = rows
                .GroupBy(row => row.SetProductCode.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    $"套装商品 {productCode} 存在重复的 SetProductCode: {duplicate.Key}"
                );
            }
        }

        private static void EnsureSetChildPurchasePriceRecalculated(
            SetChildPurchasePriceWritebackResultDto recalculation,
            IEnumerable<string> productCodes
        )
        {
            if (
                recalculation.ProductSetCode.SkippedGroupCount == 0
                && recalculation.StoreMultiCodeProduct.SkippedGroupCount == 0
            )
            {
                return;
            }

            // 被本次成本变化命中的套装组不能带着未分摊成本提交，必须由外层事务回滚。
            var affectedCodes = string.Join(
                ", ",
                productCodes.Where(code => !string.IsNullOrWhiteSpace(code)).Distinct(StringComparer.OrdinalIgnoreCase)
            );
            var reasons = string.Join(
                "；",
                recalculation.Errors.Select(error =>
                    $"{error.TableName}/{error.StoreCode ?? "总部"}/{error.ProductCode}: {error.Reason}"
                )
            );
            throw new InvalidOperationException(
                $"套装子项成本无法重算，主商品: {affectedCodes}。{reasons}"
            );
        }

        private string ResolveActorName()
        {
            var actorName = _currentUserService.GetCurrentUsername();
            return string.IsNullOrWhiteSpace(actorName) ? "System" : actorName;
        }

        private string ResolveActorGuid() => _currentUserService.GetCurrentUserGuid();

        private string ResolveActorType() =>
            !string.IsNullOrWhiteSpace(ResolveActorGuid())
            || !string.Equals(ResolveActorName(), "System", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "System";
    }
}
