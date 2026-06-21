using System.Linq.Expressions;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 仓库商品服务实现
    /// 提供仓库商品的增删改查、统计分析、库存管理等核心业务功能
    /// </summary>
    public class WarehouseProductService : IWarehouseProductService
    {
        // 数据库上下文，用于数据库操作
        private readonly SqlSugarContext _context;

        // AutoMapper对象映射器，用于实体与DTO之间的转换
        private readonly IMapper _mapper;

        // 日志记录器，用于记录操作日志和错误信息
        private readonly ILogger<WarehouseProductService> _logger;

        // 翻译服务，用于自动翻译商品名称
        private readonly ITranslationService _translationService;

        /// <summary>
        /// 构造函数：初始化仓库商品服务
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="mapper">对象映射器</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="translationService">翻译服务</param>
        public WarehouseProductService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<WarehouseProductService> logger,
            ITranslationService translationService
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _translationService = translationService;
        }

        /// <summary>
        /// 分页查询仓库商品
        /// 支持关键字搜索、分类筛选、库存预警、价格范围等多维度查询条件
        /// </summary>
        /// <param name="query">查询参数，包含分页、排序、筛选条件</param>
        /// <returns>分页结果，包含商品列表、总数、统计信息</returns>
        public async Task<WarehouseProductPagedResultDto> GetPagedProductsAsync(
            WarehouseProductQueryDto query
        )
        {
            try
            {
                _logger.LogInformation("开始分页查询仓库商品，查询条件：{@Query}", query);
                _logger.LogInformation("IsActive查询条件值: {IsActive}", query.IsActive);

                // 1. 构建复合查询条件（关键字、分类、品牌、价格、库存等）- 不包含排序
                var queryable = BuildWarehouseProductQuery(query);

                // 2. 获取查询结果的统计信息（总数、库存值、预警数量等）
                // 🔥 基于当前筛选条件的统计结果（不包含排序，避免聚合函数错误）
                var stats = await GetQueryStatsAsync(queryable, query);
                _logger.LogInformation(
                    "筛选后统计 - 商品总数: {TotalProducts}, 库存总量: {TotalStock}",
                    stats.TotalProducts,
                    stats.TotalStockQuantity
                );

                // 3. 在统计完成后添加排序逻辑
                var sortedQueryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

                // 4. 执行分页查询，包含关联表数据（分类、基础商品信息、仓库位置）
                RefAsync<int> totalCount = 0;

                var products = await sortedQueryable
                    .Includes(wp => wp.Product!.WarehouseCategory) // 🔥 二级导航：Product及其WarehouseCategory
                    .Includes(wp => wp.Locations) // 包含仓库位置信息
                    .ToPageListAsync(query.PageNumber, query.PageSize, totalCount);

                // 5. 将实体对象转换为DTO传输对象
                var productDtos = _mapper.Map<List<WarehouseProductListDto>>(products);

                // 5.5. 自动翻译商品名称
                await TranslateWarehouseProductNamesAsync(productDtos);

                // 6. 构建分页返回结果
                var result = new WarehouseProductPagedResultDto
                {
                    Items = productDtos, // 当前页商品列表
                    Total = totalCount.Value, // 总记录数
                    PageNumber = query.PageNumber, // 当前页码
                    PageSize = query.PageSize, // 每页大小
                    Stats = stats, // 统计信息
                };

                _logger.LogInformation(
                    "分页查询仓库商品完成，返回 {Count} 条记录，总计 {Total} 条",
                    productDtos.Count,
                    totalCount.Value
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分页查询仓库商品失败");
                throw;
            }
        }

        /// <summary>
        /// 根据商品编码获取商品详情
        /// 获取单个商品的完整信息，包括分类、基础信息、仓库位置等
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品详情DTO，未找到时返回null</returns>
        public async Task<WarehouseProductDto?> GetProductByCodeAsync(string productCode)
        {
            try
            {
                // 查询商品详情，包含所有关联信息
                var product = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Includes(wp => wp.Product!.WarehouseCategory) // 🔥 二级导航：Product及其WarehouseCategory
                    .Includes(wp => wp.Locations) // 仓库位置信息
                    .FirstAsync(wp => wp.ProductCode == productCode);

                var dto = _mapper.Map<WarehouseProductDto>(product);

                // 自动翻译商品名称
                await TranslateWarehouseProductNameAsync(dto);

                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败，商品编码：{ProductCode}", productCode);
                return null;
            }
        }

        /// <summary>
        /// 根据货号查询商品详情
        /// 支持通过ItemNumber、ProductCode、Barcode进行查询
        /// </summary>
        /// <param name="itemNumber">货号（可以是ItemNumber、ProductCode或Barcode）</param>
        /// <returns>商品详情DTO，未找到时返回null</returns>
        public async Task<WarehouseProductDto?> GetProductByItemNumberAsync(string itemNumber)
        {
            try
            {
                _logger.LogInformation("通过货号查询商品: {ItemNumber}", itemNumber);

                // 首先尝试通过WarehouseProduct的ProductCode查找
                var warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Includes(wp => wp.Product!.WarehouseCategory)
                    .Includes(wp => wp.Locations)
                    .Where(wp => wp.ProductCode == itemNumber)
                    .FirstAsync();

                if (warehouseProduct != null)
                {
                    _logger.LogInformation(
                        "通过ProductCode找到商品: {ProductCode}",
                        warehouseProduct.ProductCode
                    );
                    var dto = _mapper.Map<WarehouseProductDto>(warehouseProduct);
                    await TranslateWarehouseProductNameAsync(dto);
                    return dto;
                }

                // 如果没找到，尝试通过Product表的ItemNumber查找
                warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                    .Where((wp, p) => p.ItemNumber == itemNumber)
                    .Select((wp, p) => wp)
                    .Includes(wp => wp.Product!.WarehouseCategory)
                    .Includes(wp => wp.Locations)
                    .FirstAsync();

                if (warehouseProduct != null)
                {
                    _logger.LogInformation(
                        "通过ItemNumber找到商品: {ProductCode}",
                        warehouseProduct.ProductCode
                    );
                    var dto = _mapper.Map<WarehouseProductDto>(warehouseProduct);
                    await TranslateWarehouseProductNameAsync(dto);
                    return dto;
                }

                // 如果还没找到，尝试通过Product表的Barcode查找
                warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                    .Where((wp, p) => p.Barcode == itemNumber)
                    .Select((wp, p) => wp)
                    .Includes(wp => wp.Product!.WarehouseCategory)
                    .Includes(wp => wp.Locations)
                    .FirstAsync();

                if (warehouseProduct != null)
                {
                    _logger.LogInformation(
                        "通过Barcode找到商品: {ProductCode}",
                        warehouseProduct.ProductCode
                    );
                    var dto = _mapper.Map<WarehouseProductDto>(warehouseProduct);
                    await TranslateWarehouseProductNameAsync(dto);
                    return dto;
                }

                _logger.LogWarning("未找到货号为 {ItemNumber} 的商品", itemNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通过货号查询商品失败，货号：{ItemNumber}", itemNumber);
                return null;
            }
        }

        /// <summary>
        /// 批量通过货号查询商品（优化版本，只通过ItemNumber匹配）
        /// </summary>
        /// <param name="itemNumbers">货号列表</param>
        /// <returns>商品字典，Key为货号，Value为商品信息</returns>
        public async Task<
            Dictionary<string, WarehouseProductDto>
        > BatchGetProductsByItemNumbersAsync(List<string> itemNumbers)
        {
            try
            {
                if (itemNumbers == null || !itemNumbers.Any())
                {
                    _logger.LogWarning("批量查询商品：货号列表为空");
                    return new Dictionary<string, WarehouseProductDto>();
                }

                _logger.LogInformation("开始批量查询商品，货号数量: {Count}", itemNumbers.Count);

                var result = new Dictionary<string, WarehouseProductDto>();

                // 批量查询通过ItemNumber匹配的商品
                var itemNumberMatches = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                    .Where((wp, p) => p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber))
                    .Select((wp, p) => wp)
                    .Includes(wp => wp.Product!.WarehouseCategory)
                    .Includes(wp => wp.Locations)
                    .ToListAsync();

                foreach (var product in itemNumberMatches)
                {
                    if (product.Product?.ItemNumber != null)
                    {
                        var dto = _mapper.Map<WarehouseProductDto>(product);
                        await TranslateWarehouseProductNameAsync(dto);
                        result[product.Product.ItemNumber] = dto;
                    }
                }

                _logger.LogInformation(
                    "批量查询商品完成，找到商品数量: {FoundCount}, 总货号数量: {TotalCount}",
                    result.Count,
                    itemNumbers.Count
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量查询商品失败，货号数量: {Count}", itemNumbers.Count);
                throw;
            }
        }

        /// <summary>
        /// 创建新商品
        /// 自动生成唯一商品编码，设置创建和更新时间
        /// </summary>
        /// <param name="productDto">商品创建DTO</param>
        /// <returns>创建成功的商品详情DTO</returns>
        public async Task<WarehouseProductDto> CreateProductAsync(
            CreateWarehouseProductDto productDto
        )
        {
            try
            {
                var now = DateTime.UtcNow;
                var productCode = Guid.NewGuid().ToString();
                var purchasePrice = productDto.PurchasePrice ?? productDto.ImportPrice;
                var retailPrice = productDto.RetailPrice ?? productDto.OEMPrice;
                var packingQuantity = productDto.PackingQty ?? productDto.PackingQuantity;

                // 将DTO转换为实体对象
                var product = _mapper.Map<WarehouseProduct>(productDto);

                // 自动生成唯一商品编码
                product.ProductCode = productCode;
                product.ImportPrice = purchasePrice;
                product.OEMPrice = retailPrice;
                product.PackingQuantity = packingQuantity;
                product.CreatedAt = now; // 设置创建时间
                product.UpdatedAt = now; // 设置更新时间

                var productEntity = new Product
                {
                    ProductCode = productCode,
                    ProductName = productDto.ProductName ?? string.Empty,
                    Barcode = productDto.Barcode,
                    PurchasePrice = purchasePrice,
                    RetailPrice = retailPrice,
                    MiddlePackageQuantity = productDto.MiddlePackageQuantity,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                StoreRetailPrice? storeRetailPrice = null;
                if (!string.IsNullOrWhiteSpace(productDto.StoreCode))
                {
                    var storeCode = productDto.StoreCode.Trim();
                    storeRetailPrice = new StoreRetailPrice
                    {
                        StoreCode = storeCode,
                        ProductCode = productCode,
                        StoreProductCode = $"{storeCode}{productCode}",
                        PurchasePrice = purchasePrice,
                        StoreRetailPriceValue = retailPrice,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                }

                // 插入数据库并返回插入后的实体
                WarehouseProduct insertedProduct;
                _context.Db.Ado.BeginTran();
                try
                {
                    insertedProduct = await _context
                        .Db.Insertable(product)
                        .ExecuteReturnEntityAsync();
                    await _context.Db.Insertable(productEntity).ExecuteCommandAsync();
                    if (storeRetailPrice != null)
                    {
                        await _context.Db.Insertable(storeRetailPrice).ExecuteCommandAsync();
                    }

                    _context.Db.Ado.CommitTran();
                }
                catch
                {
                    _context.Db.Ado.RollbackTran();
                    throw;
                }

                insertedProduct.Product = productEntity;
                return _mapper.Map<WarehouseProductDto>(insertedProduct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品失败");
                throw;
            }
        }

        /// <summary>
        /// 更新商品信息
        /// 根据商品编码更新商品的可变属性，自动更新修改时间
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="productDto">商品更新DTO</param>
        /// <returns>更新后的商品详情DTO，商品不存在时返回null</returns>
        public async Task<WarehouseProductDto?> UpdateProductAsync(
            string productCode,
            UpdateWarehouseProductDto productDto
        )
        {
            try
            {
                var normalizedProductCode = productCode.Trim();
                var now = DateTime.UtcNow;

                // 查找要更新的商品
                var existingProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .FirstAsync(wp => wp.ProductCode == normalizedProductCode);

                if (existingProduct == null)
                    return null;

                var productEntity = await _context
                    .Db.Queryable<Product>()
                    .FirstAsync(p => p.ProductCode == normalizedProductCode);
                var shouldInsertProduct = productEntity == null;
                productEntity ??= new Product
                {
                    ProductCode = normalizedProductCode,
                    ProductName = productDto.ProductName ?? string.Empty,
                    Barcode = productDto.Barcode,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var storeRetailPrices = await GetStoreRetailPricesForWarehouseUpdateAsync(
                    normalizedProductCode,
                    productDto.StoreCode
                );

                WarehouseProductPricePersistenceMapper.ApplyUpdate(
                    productDto,
                    existingProduct,
                    productEntity,
                    storeRetailPrices,
                    now
                );

                var hasStoreCode = !string.IsNullOrWhiteSpace(productDto.StoreCode);
                var shouldInsertStoreRetailPrice = hasStoreCode && storeRetailPrices.Count == 0;
                if (shouldInsertStoreRetailPrice)
                {
                    var newStoreRetailPrice = new StoreRetailPrice
                    {
                        StoreCode = productDto.StoreCode!.Trim(),
                        ProductCode = normalizedProductCode,
                        SupplierCode = productEntity.LocalSupplierCode,
                        StoreProductCode = $"{productDto.StoreCode!.Trim()}{normalizedProductCode}",
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    WarehouseProductPricePersistenceMapper.ApplyUpdate(
                        productDto,
                        existingProduct,
                        productEntity,
                        new[] { newStoreRetailPrice },
                        now
                    );
                    storeRetailPrices.Add(newStoreRetailPrice);
                }
                else if (!hasStoreCode && storeRetailPrices.Count == 0)
                {
                    _logger.LogWarning(
                        "仓库商品价格已更新 Product，但未找到可同步的 StoreRetailPrice，且请求未提供 StoreCode。ProductCode={ProductCode}",
                        normalizedProductCode
                    );
                }

                _context.Db.Ado.BeginTran();
                try
                {
                    await _context.Db.Updateable(existingProduct).ExecuteCommandAsync();

                    if (shouldInsertProduct)
                    {
                        await _context.Db.Insertable(productEntity).ExecuteCommandAsync();
                    }
                    else
                    {
                        await _context.Db.Updateable(productEntity).ExecuteCommandAsync();
                    }

                    if (storeRetailPrices.Count > 0)
                    {
                        if (shouldInsertStoreRetailPrice)
                        {
                            await _context.Db.Insertable(storeRetailPrices[0]).ExecuteCommandAsync();
                        }
                        else
                        {
                            await _context.Db.Updateable(storeRetailPrices).ExecuteCommandAsync();
                        }
                    }

                    _context.Db.Ado.CommitTran();
                }
                catch
                {
                    _context.Db.Ado.RollbackTran();
                    throw;
                }

                existingProduct.Product = productEntity;
                return _mapper.Map<WarehouseProductDto>(existingProduct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品失败，商品编码：{ProductCode}", productCode);
                throw;
            }
        }

        private async Task<List<StoreRetailPrice>> GetStoreRetailPricesForWarehouseUpdateAsync(
            string productCode,
            string? storeCode
        )
        {
            var query = _context
                .Db.Queryable<StoreRetailPrice>()
                .Where(srp => srp.ProductCode == productCode && !srp.IsDeleted);

            if (!string.IsNullOrWhiteSpace(storeCode))
            {
                var normalizedStoreCode = storeCode.Trim();
                query = query.Where(srp => srp.StoreCode == normalizedStoreCode);
            }

            return await query.ToListAsync();
        }

        /// <summary>
        /// 删除商品
        /// 物理删除指定商品编码的商品记录
        /// </summary>
        /// <param name="productCode">要删除的商品编码</param>
        /// <returns>true表示删除成功，false表示删除失败或商品不存在</returns>
        public async Task<bool> DeleteProductAsync(string productCode)
        {
            try
            {
                // 执行物理删除操作
                var result = await _context
                    .Db.Deleteable<WarehouseProduct>()
                    .Where(wp => wp.ProductCode == productCode)
                    .ExecuteCommandAsync();

                return result > 0; // 返回是否有记录被删除
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品失败，商品编码：{ProductCode}", productCode);
                return false;
            }
        }

        /// <summary>
        /// 批量更新商品状态
        /// 同时更新多个商品的启用/禁用状态，提高批量操作效率
        /// </summary>
        /// <param name="productCodes">要更新的商品编码列表</param>
        /// <param name="isActive">新的状态值：true为启用，false为禁用</param>
        /// <returns>实际更新的记录数量</returns>
        public async Task<int> BatchUpdateProductStatusAsync(
            List<string> productCodes,
            bool isActive
        )
        {
            try
            {
                // 批量更新商品状态，同时更新修改时间
                var result = await _context
                    .Db.Updateable<WarehouseProduct>()
                    .SetColumns(wp => new WarehouseProduct
                    {
                        IsActive = isActive,
                        UpdatedAt = DateTime.UtcNow,
                    })
                    .Where(wp => productCodes.Contains(wp.ProductCode))
                    .ExecuteCommandAsync();

                return result; // 返回实际更新的记录数
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品状态失败");
                throw;
            }
        }

        /// <summary>
        /// 获取库存预警商品
        /// 查找库存数量低于或等于预警值的商品，支持按仓库位置筛选
        /// </summary>
        /// <param name="locationGuids">可选的仓库位置GUID列表，为空时查询所有位置</param>
        /// <returns>需要预警的商品列表，按库存数量从低到高排序</returns>
        public async Task<List<WarehouseProductListDto>> GetStockAlertProductsAsync(
            List<string>? locationGuids = null
        )
        {
            try
            {
                // 构建库存预警查询条件
                var queryable = _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(wp => wp.IsActive) // 只查询启用的商品
                    .Where(wp => wp.StockQuantity != null && wp.StockAlertQuantity != null) // 确保库存和预警值都有设置
                    .Where(wp => wp.StockQuantity <= wp.StockAlertQuantity); // 库存低于预警值

                // 如果指定了仓库位置，添加位置过滤
                if (locationGuids != null && locationGuids.Any())
                {
                    queryable = queryable
                        .LeftJoin<ProductLocation>((wp, pl) => wp.ProductCode == pl.ProductCode)
                        .Where(
                            (wp, pl) =>
                                pl.LocationGuid != null && locationGuids.Contains(pl.LocationGuid)
                        );
                }

                // 查询预警商品，按库存数量升序排列（最紧急的在前面）
                var alertProducts = await queryable
                    .Includes(wp => wp.Product!.WarehouseCategory) // 🔥 二级导航：Product及其WarehouseCategory
                    .Includes(wp => wp.Locations) // 包含仓库位置信息
                    .OrderBy(wp => wp.StockQuantity) // 按库存数量从低到高排序
                    .ToListAsync();

                return _mapper.Map<List<WarehouseProductListDto>>(alertProducts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取库存预警商品失败");
                throw;
            }
        }

        /// <summary>
        /// 更新商品库存
        /// 更新指定商品的库存数量和库存金额，用于库存调整、入库、出库等操作
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="stockQuantity">新的库存数量</param>
        /// <param name="stockValue">可选的库存金额，为空时不更新</param>
        /// <returns>true表示更新成功，false表示失败或商品不存在</returns>
        public async Task<bool> UpdateProductStockAsync(
            string productCode,
            int stockQuantity,
            decimal? stockValue = null
        )
        {
            try
            {
                // 构建要更新的字段
                var updateColumns = new WarehouseProduct
                {
                    StockQuantity = stockQuantity, // 更新库存数量
                    UpdatedAt = DateTime.UtcNow, // 更新修改时间
                };

                // 如果提供了库存金额，也一并更新
                if (stockValue.HasValue)
                    updateColumns.StockValue = stockValue.Value;

                // 执行更新操作
                var result = await _context
                    .Db.Updateable(updateColumns)
                    .Where(wp => wp.ProductCode == productCode)
                    .ExecuteCommandAsync();

                return result > 0; // 返回是否有记录被更新
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新商品库存失败，商品编码：{ProductCode}", productCode);
                return false;
            }
        }

        /// <summary>
        /// 根据条码搜索商品
        /// 通过商品条码精确查找商品，支持扫码枪等设备的快速查找需求
        /// </summary>
        /// <param name="barcode">商品条码</param>
        /// <returns>匹配的启用状态商品列表</returns>
        public async Task<List<WarehouseProductListDto>> SearchProductsByBarcodeAsync(
            string barcode
        )
        {
            try
            {
                // 根据条码精确查找启用的商品（改为使用Product表的Barcode字段）
                var products = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                    .Where((wp, p) => p.Barcode == barcode && wp.IsActive) // 条码匹配且商品已启用
                    .Select((wp, p) => wp)
                    .Includes(wp => wp.Product!.WarehouseCategory) // 🔥 二级导航：Product及其WarehouseCategory
                    .Includes(wp => wp.Locations) // 包含仓库位置信息
                    .ToListAsync();

                return _mapper.Map<List<WarehouseProductListDto>>(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据条码搜索商品失败，条码：{Barcode}", barcode);
                throw;
            }
        }

        /// <summary>
        /// 获取商品统计信息
        /// 统计商品总数、库存总量、总价值、预警数量等关键指标，支持按分类和位置筛选
        /// </summary>
        /// <param name="categoryGuid">可选的分类GUID，为空时统计所有分类</param>
        /// <param name="locationGuids">可选的仓库位置GUID列表，为空时统计所有位置</param>
        /// <returns>商品统计信息DTO</returns>
        public async Task<WarehouseProductStatsDto> GetProductStatsAsync(
            string? categoryGuid = null,
            List<string>? locationGuids = null
        )
        {
            try
            {
                // 构建基础查询
                var queryable = BuildBaseQuery();

                // 如果指定了分类，添加分类过滤（通过Product表的WarehouseCategoryGUID）
                if (!string.IsNullOrEmpty(categoryGuid))
                {
                    queryable = queryable
                        .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                        .Where((wp, p) => p.WarehouseCategoryGUID == categoryGuid);
                }

                // 如果指定了仓库位置，添加位置过滤
                if (locationGuids != null && locationGuids.Any())
                {
                    queryable = queryable
                        .LeftJoin<ProductLocation>((wp, pl) => wp.ProductCode == pl.ProductCode)
                        .Where(
                            (wp, pl) =>
                                pl.LocationGuid != null && locationGuids.Contains(pl.LocationGuid)
                        );
                }

                // 使用聚合函数统计各项指标
                var stats = await queryable
                    .Select(wp => new
                    {
                        TotalProducts = SqlFunc.AggregateCount(wp.ProductCode), // 商品总数
                        TotalStockQuantity = SqlFunc.AggregateSum(wp.StockQuantity), // 库存总量
                        TotalStockValue = SqlFunc.AggregateSum(wp.StockValue), // 库存总价值
                        StockAlertCount = SqlFunc.AggregateCount(
                            SqlFunc.IIF(
                                wp.StockQuantity <= wp.StockAlertQuantity,
                                wp.ProductCode,
                                null
                            )
                        ), // 库存预警数量
                        OutOfStockCount = SqlFunc.AggregateCount(
                            SqlFunc.IIF(wp.StockQuantity <= 0, wp.ProductCode, null)
                        ), // 缺货商品数量
                        ActiveProductCount = SqlFunc.AggregateCount(
                            SqlFunc.IIF(wp.IsActive, wp.ProductCode, null)
                        ), // 启用商品数量
                    })
                    .FirstAsync();

                // 构建统计结果DTO
                return new WarehouseProductStatsDto
                {
                    TotalProducts = stats.TotalProducts,
                    TotalStockQuantity = stats.TotalStockQuantity ?? 0,
                    TotalStockValue = stats.TotalStockValue ?? 0,
                    StockAlertCount = stats.StockAlertCount,
                    OutOfStockCount = stats.OutOfStockCount,
                    ActiveProductCount = stats.ActiveProductCount,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品统计信息失败");
                throw;
            }
        }

        /// <summary>
        /// 导出商品数据
        /// 导出满足查询条件的所有商品数据，不受分页限制，用于Excel导出等场景
        /// </summary>
        /// <param name="query">查询条件（分页参数将被忽略）</param>
        /// <returns>符合条件的所有商品列表</returns>
        public async Task<List<WarehouseProductListDto>> ExportProductsAsync(
            WarehouseProductQueryDto query
        )
        {
            try
            {
                // 复制查询条件但移除分页限制，用于导出全部数据
                var exportQuery = new WarehouseProductQueryDto
                {
                    PageNumber = 1, // 重置为第1页
                    PageSize = int.MaxValue, // 设置为最大值，取消分页限制
                    Keyword = query.Keyword, // 保持原有筛选条件
                    CategoryGUID = query.CategoryGUID,
                    IncludeSubCategories = query.IncludeSubCategories,
                    Volume = query.Volume,
                    IsActive = query.IsActive,
                    MinStockQuantity = query.MinStockQuantity,
                    MaxStockQuantity = query.MaxStockQuantity,
                    MinPrice = query.MinPrice,
                    MaxPrice = query.MaxPrice,
                    PriceType = query.PriceType,
                    HasStockAlert = query.HasStockAlert,
                    SortBy = query.SortBy, // 保持排序设置
                    SortDescending = query.SortDescending,
                    LocationGuids = query.LocationGuids,
                };

                // 构建查询并获取所有符合条件的商品
                var queryable = BuildWarehouseProductQuery(exportQuery);

                var products = await queryable
                    .Includes(wp => wp.Product!.WarehouseCategory) // 🔥 二级导航：Product及其WarehouseCategory
                    .Includes(wp => wp.Locations) // 包含仓库位置信息
                    .ToListAsync();

                return _mapper.Map<List<WarehouseProductListDto>>(products);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出商品数据失败");
                throw;
            }
        }

        /// <summary>
        /// 检查商品编码是否存在
        /// 验证指定的商品编码是否已被使用，用于创建商品前的唯一性检查
        /// </summary>
        /// <param name="productCode">要检查的商品编码</param>
        /// <returns>true表示编码已存在，false表示编码可用</returns>
        public async Task<bool> IsProductCodeExistsAsync(string productCode)
        {
            try
            {
                // 查询数据库中是否已存在该商品编码
                return await _context
                    .Db.Queryable<WarehouseProduct>()
                    .AnyAsync(wp => wp.ProductCode == productCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "检查商品编码是否存在失败，商品编码：{ProductCode}",
                    productCode
                );
                return false;
            }
        }

        /// <summary>
        /// 检查条码是否存在
        /// 验证指定的商品条码是否已被使用，支持排除特定商品（用于更新时检查）
        /// </summary>
        /// <param name="barcode">要检查的商品条码</param>
        /// <param name="excludeProductCode">要排除的商品编码（更新商品时排除自身）</param>
        /// <returns>true表示条码已存在，false表示条码可用</returns>
        public async Task<bool> IsBarcodeExistsAsync(
            string barcode,
            string? excludeProductCode = null
        )
        {
            try
            {
                // 构建条码唯一性检查查询（改为使用Product表的Barcode字段）
                var queryable = _context
                    .Db.Queryable<WarehouseProduct>()
                    .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                    .Where((wp, p) => p.Barcode == barcode);

                // 如果提供了排除的商品编码（通常用于更新操作），则排除该商品
                if (!string.IsNullOrEmpty(excludeProductCode))
                {
                    queryable = queryable.Where(wp => wp.ProductCode != excludeProductCode);
                }

                return await queryable.AnyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查条码是否存在失败，条码：{Barcode}", barcode);
                return false;
            }
        }

        #region 私有方法 - Private Methods

        /// <summary>
        /// 构建仓库商品查询
        /// 根据查询条件构建复杂的数据库查询，支持多维度筛选和搜索
        /// </summary>
        /// <param name="query">查询参数对象</param>
        /// <returns>构建好的查询对象</returns>
        private ISugarQueryable<WarehouseProduct> BuildWarehouseProductQuery(
            WarehouseProductQueryDto query
        )
        {
            // 🔍 调试：记录查询参数
            _logger.LogInformation(
                "构建查询条件 - IsActive: {IsActive}, HasStockAlert: {HasStockAlert}, MinStock: {MinStock}, MaxStock: {MaxStock}",
                query.IsActive,
                query.HasStockAlert,
                query.MinStockQuantity,
                query.MaxStockQuantity
            );

            // 从基础查询开始构建
            var queryable = BuildBaseQuery();

            // 1. 关键字搜索（支持多字段模糊匹配）
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                var keyword = query.Keyword.Trim();
                // 检查是否需要搜索Product表字段
                bool needsProductFields = true; // 总是检查Product字段以获得更全面的搜索结果

                if (needsProductFields)
                {
                    // 搜索WarehouseProduct和Product两个表的字段
                    queryable = queryable
                        .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                        .Where(
                            (wp, p) =>
                                // WarehouseProduct 自身字段搜索（移除冗余字段）
                                wp.ProductCode.Contains(keyword)
                                ||
                                // Product 表字段搜索
                                (p.ProductName != null && p.ProductName.Contains(keyword))
                                || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                                || (p.Barcode != null && p.Barcode.Contains(keyword))
                                || (
                                    p.LocalSupplierCode != null
                                    && p.LocalSupplierCode.Contains(keyword)
                                )
                        );
                }
                else
                {
                    // 只搜索WarehouseProduct字段（移除冗余字段）
                    queryable = queryable.Where(wp => wp.ProductCode.Contains(keyword));
                }
            }

            // 2. 分类过滤（支持包含子分类的递归查询）
            if (!string.IsNullOrEmpty(query.CategoryGUID))
            {
                // 检查是否已经Join了Product表
                bool hasExistingJoin = !string.IsNullOrWhiteSpace(query.Keyword);

                if (hasExistingJoin)
                {
                    // 已有Join，使用特殊的处理方式
                    // 由于SqlSugar的限制，我们需要重新构建查询以避免Delegate错误
                    if (query.IncludeSubCategories)
                    {
                        var categoryGuids = GetCategoryAndSubCategories(query.CategoryGUID);
                        // 使用子查询方式避免Delegate参数问题
                        queryable = queryable.Where(wp =>
                            SqlFunc
                                .Subqueryable<Product>()
                                .Where(p =>
                                    p.ProductCode == wp.ProductCode
                                    && p.WarehouseCategoryGUID != null
                                    && categoryGuids.Contains(p.WarehouseCategoryGUID)
                                )
                                .Any()
                        );
                    }
                    else
                    {
                        // 使用子查询方式避免Delegate参数问题
                        queryable = queryable.Where(wp =>
                            SqlFunc
                                .Subqueryable<Product>()
                                .Where(p =>
                                    p.ProductCode == wp.ProductCode
                                    && p.WarehouseCategoryGUID == query.CategoryGUID
                                )
                                .Any()
                        );
                    }
                }
                else
                {
                    // 没有Join，使用链式调用同时Join和过滤
                    if (query.IncludeSubCategories)
                    {
                        var categoryGuids = GetCategoryAndSubCategories(query.CategoryGUID);
                        queryable = queryable
                            .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                            .Where(
                                (wp, p) =>
                                    p.WarehouseCategoryGUID != null
                                    && categoryGuids.Contains(p.WarehouseCategoryGUID)
                            );
                    }
                    else
                    {
                        queryable = queryable
                            .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                            .Where((wp, p) => p.WarehouseCategoryGUID == query.CategoryGUID);
                    }
                }
            }

            // 4. 状态过滤（启用/禁用）
            if (query.IsActive.HasValue)
            {
                _logger.LogInformation("应用IsActive过滤条件: {IsActive}", query.IsActive.Value);

                // 🔧 修复：明确比较bool值，避免可空类型问题
                var isActiveValue = query.IsActive.Value;
                queryable = queryable.Where(wp => wp.IsActive == isActiveValue);

                // 🔍 调试：输出当前查询的SQL语句
                try
                {
                    var currentSql = queryable.ToSql();
                    _logger.LogInformation("IsActive过滤后的SQL: {Sql}", currentSql.Key);
                    _logger.LogInformation("SQL参数: {@Parameters}", currentSql.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "获取SQL语句失败");
                }
            }

            // 5. 库存数量范围过滤
            if (query.MinStockQuantity.HasValue)
            {
                queryable = queryable.Where(wp => wp.StockQuantity >= query.MinStockQuantity.Value);
            }
            if (query.MaxStockQuantity.HasValue)
            {
                queryable = queryable.Where(wp => wp.StockQuantity <= query.MaxStockQuantity.Value);
            }

            // 6. 价格范围过滤（支持多种价格类型：OEM价、进口价、国内价）
            if (query.MinPrice.HasValue || query.MaxPrice.HasValue)
            {
                switch (query.PriceType?.ToLower())
                {
                    case "oemprice": // OEM价格筛选
                        if (query.MinPrice.HasValue)
                            queryable = queryable.Where(wp => wp.OEMPrice >= query.MinPrice.Value);
                        if (query.MaxPrice.HasValue)
                            queryable = queryable.Where(wp => wp.OEMPrice <= query.MaxPrice.Value);
                        break;
                    case "importprice": // 进口价格筛选
                        if (query.MinPrice.HasValue)
                            queryable = queryable.Where(wp =>
                                wp.ImportPrice >= query.MinPrice.Value
                            );
                        if (query.MaxPrice.HasValue)
                            queryable = queryable.Where(wp =>
                                wp.ImportPrice <= query.MaxPrice.Value
                            );
                        break;
                    default: // 国内价格筛选（默认）
                        if (query.MinPrice.HasValue)
                            queryable = queryable.Where(wp =>
                                wp.DomesticPrice >= query.MinPrice.Value
                            );
                        if (query.MaxPrice.HasValue)
                            queryable = queryable.Where(wp =>
                                wp.DomesticPrice <= query.MaxPrice.Value
                            );
                        break;
                }
            }

            // 7. 库存预警过滤
            if (query.HasStockAlert.HasValue)
            {
                if (query.HasStockAlert.Value)
                {
                    // 查询需要预警的商品（库存 <= 预警值）
                    queryable = queryable.Where(wp =>
                        wp.StockQuantity != null
                        && wp.StockAlertQuantity != null
                        && wp.StockQuantity <= wp.StockAlertQuantity
                    );
                }
                else
                {
                    // 查询不需要预警的商品（库存 > 预警值或未设置预警值）
                    queryable = queryable.Where(wp =>
                        wp.StockQuantity == null
                        || wp.StockAlertQuantity == null
                        || wp.StockQuantity > wp.StockAlertQuantity
                    );
                }
            }

            // 8. 仓库位置过滤
            if (query.LocationGuids != null && query.LocationGuids.Any())
            {
                queryable = queryable
                    .LeftJoin<ProductLocation>((wp, pl) => wp.ProductCode == pl.ProductCode) // 左连接商品位置表
                    .Where(
                        (wp, pl) =>
                            pl.LocationGuid != null && query.LocationGuids.Contains(pl.LocationGuid)
                    ); // 筛选指定仓库位置
            }

            // 注意：排序逻辑已移到 GetPagedProductsAsync 中，在统计查询完成后添加
            return queryable; // 返回构建完成的查询对象（仅包含过滤条件）
        }

        /// <summary>
        /// 构建基础查询
        /// 创建WarehouseProduct表的基础查询对象，作为所有查询操作的起点
        /// 通过 EXISTS 子查询过滤掉没有对应Product记录的商品，确保数据完整性
        /// </summary>
        /// <returns>基础查询对象</returns>
        private ISugarQueryable<WarehouseProduct> BuildBaseQuery()
        {
            return _context
                .Db.Queryable<WarehouseProduct>()
                .Where(wp =>
                    SqlFunc
                        .Subqueryable<Product>()
                        .Where(p => p.ProductCode == wp.ProductCode)
                        .Any()
                ); // 🔧 使用EXISTS子查询过滤掉没有对应Product记录的商品
        }

        /// <summary>
        /// 将过滤条件应用到已排序的查询上
        /// </summary>
        /// <param name="sortedQueryable">已排序的查询对象</param>
        /// <param name="query">查询条件</param>
        /// <returns>应用了过滤条件的查询对象</returns>
        private ISugarQueryable<WarehouseProduct> ApplyFiltersToSortedQuery(
            ISugarQueryable<WarehouseProduct> sortedQueryable,
            WarehouseProductQueryDto query
        )
        {
            var queryable = sortedQueryable;

            // 1. 关键字搜索（需要检查是否已有 JOIN）
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                var keyword = query.Keyword.Trim();

                // 检查排序是否已经添加了 Product JOIN
                var needsProductJoin = !RequiresProductJoin(query.SortBy);

                if (needsProductJoin)
                {
                    queryable = queryable
                        .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                        .Where(
                            (wp, p) =>
                                // WarehouseProduct 自身字段搜索（移除冗余字段）
                                wp.ProductCode.Contains(keyword)
                                ||
                                // Product 字段搜索
                                (p.ProductName != null && p.ProductName.Contains(keyword))
                                || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                                || (p.Barcode != null && p.Barcode.Contains(keyword))
                                || (
                                    p.LocalSupplierCode != null
                                    && p.LocalSupplierCode.Contains(keyword)
                                )
                        );
                }
                else
                {
                    // 已有 Product JOIN，直接添加 WHERE 条件（移除冗余字段）
                    queryable = queryable.Where(wp =>
                        // WarehouseProduct 自身字段搜索
                        wp.ProductCode.Contains(keyword)
                    );
                }
            }

            // 2. 分类过滤
            if (!string.IsNullOrEmpty(query.CategoryGUID))
            {
                var needsProductJoin =
                    !RequiresProductJoin(query.SortBy) && string.IsNullOrWhiteSpace(query.Keyword);

                if (query.IncludeSubCategories)
                {
                    var categoryGuids = GetCategoryAndSubCategories(query.CategoryGUID);
                    if (needsProductJoin)
                    {
                        queryable = queryable
                            .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                            .Where(
                                (wp, p) =>
                                    p.WarehouseCategoryGUID != null
                                    && categoryGuids.Contains(p.WarehouseCategoryGUID)
                            );
                    }
                    else
                    {
                        // 已有 JOIN，直接使用 WHERE
                        queryable = queryable.Where(wp => true); // 需要通过其他方式实现分类过滤
                    }
                }
                else
                {
                    if (needsProductJoin)
                    {
                        queryable = queryable
                            .LeftJoin<Product>((wp, p) => wp.ProductCode == p.ProductCode)
                            .Where((wp, p) => p.WarehouseCategoryGUID == query.CategoryGUID);
                    }
                    else
                    {
                        // 已有 JOIN，直接使用 WHERE
                        queryable = queryable.Where(wp => true); // 需要通过其他方式实现分类过滤
                    }
                }
            }

            // 3. 其他过滤条件（不涉及 JOIN）

            if (query.IsActive.HasValue)
            {
                var isActiveValue = query.IsActive.Value;
                queryable = queryable.Where(wp => wp.IsActive == isActiveValue);
            }

            if (query.HasStockAlert.HasValue && query.HasStockAlert.Value)
            {
                queryable = queryable.Where(wp =>
                    wp.StockQuantity != null
                    && wp.StockAlertQuantity != null
                    && wp.StockQuantity <= wp.StockAlertQuantity
                );
            }

            if (query.MinStockQuantity.HasValue)
            {
                queryable = queryable.Where(wp => wp.StockQuantity >= query.MinStockQuantity.Value);
            }

            if (query.MaxStockQuantity.HasValue)
            {
                queryable = queryable.Where(wp => wp.StockQuantity <= query.MaxStockQuantity.Value);
            }

            if (query.MinPrice.HasValue || query.MaxPrice.HasValue)
            {
                var priceField = GetPriceField(query.PriceType);
                if (query.MinPrice.HasValue)
                {
                    queryable = priceField switch
                    {
                        "DomesticPrice" => queryable.Where(wp =>
                            wp.DomesticPrice >= query.MinPrice.Value
                        ),
                        "OEMPrice" => queryable.Where(wp => wp.OEMPrice >= query.MinPrice.Value),
                        "ImportPrice" => queryable.Where(wp =>
                            wp.ImportPrice >= query.MinPrice.Value
                        ),
                        _ => queryable,
                    };
                }
                if (query.MaxPrice.HasValue)
                {
                    queryable = priceField switch
                    {
                        "DomesticPrice" => queryable.Where(wp =>
                            wp.DomesticPrice <= query.MaxPrice.Value
                        ),
                        "OEMPrice" => queryable.Where(wp => wp.OEMPrice <= query.MaxPrice.Value),
                        "ImportPrice" => queryable.Where(wp =>
                            wp.ImportPrice <= query.MaxPrice.Value
                        ),
                        _ => queryable,
                    };
                }
            }

            if (query.LocationGuids != null && query.LocationGuids.Any())
            {
                queryable = queryable
                    .LeftJoin<ProductLocation>((wp, pl) => wp.ProductCode == pl.ProductCode)
                    .Where(
                        (wp, pl) =>
                            pl.LocationGuid != null && query.LocationGuids.Contains(pl.LocationGuid)
                    );
            }

            return queryable;
        }

        /// <summary>
        /// 检查排序字段是否需要 Product JOIN
        /// </summary>
        /// <param name="sortBy">排序字段</param>
        /// <returns>是否需要 Product JOIN</returns>
        private bool RequiresProductJoin(string? sortBy)
        {
            if (string.IsNullOrEmpty(sortBy))
                return false;

            var sortByLower = sortBy.ToLower();
            return sortByLower == "itemnumber"
                || sortByLower == "货号"
                || sortByLower == "productname"
                || sortByLower == "商品名称"
                || sortByLower == "categoryname"
                || sortByLower == "分类名称";
        }

        /// <summary>
        /// 根据价格类型获取对应的字段名
        /// </summary>
        /// <param name="priceType">价格类型</param>
        /// <returns>字段名</returns>
        private string GetPriceField(string? priceType)
        {
            return priceType?.ToLower() switch
            {
                "domestic" or "国内" => "DomesticPrice",
                "oem" or "贴牌" => "OEMPrice",
                "import" or "进口" => "ImportPrice",
                _ => "DomesticPrice", // 默认使用国内价格
            };
        }

        /// <summary>
        /// 检查查询是否已包含 Product 表的 JOIN
        /// 简单实现：假设如果有关键字搜索或分类过滤，则已包含 Product JOIN
        /// </summary>
        /// <param name="queryable">查询对象</param>
        /// <returns>是否已包含 Product JOIN</returns>
        private bool HasProductJoin(ISugarQueryable<WarehouseProduct> queryable)
        {
            // 简单判断：通过当前查询条件来推断是否已有 Product JOIN
            // 实际使用中，可以通过查询的 SQL 字符串来判断
            return false; // 暂时总是添加 JOIN，确保排序正常
        }

        /// <summary>
        /// 应用排序规则到查询
        /// 支持按产品代码、产品名称、货号、价格、库存数量等字段排序
        /// </summary>
        /// <param name="queryable">查询对象</param>
        /// <param name="sortBy">排序字段</param>
        /// <param name="sortDescending">是否降序</param>
        /// <returns>已应用排序的查询对象</returns>
        private ISugarQueryable<WarehouseProduct> ApplySorting(
            ISugarQueryable<WarehouseProduct> queryable,
            string? sortBy,
            bool sortDescending
        )
        {
            if (string.IsNullOrEmpty(sortBy))
            {
                // 默认按ItemNumber排序
                return queryable.OrderBy(
                    wp =>
                        SqlFunc
                            .Subqueryable<Product>()
                            .Where(p => p.ProductCode == wp.ProductCode)
                            .Select(p => p.ItemNumber ?? wp.ProductCode),
                    OrderByType.Asc
                );
            }

            var sortByLower = sortBy.ToLower();
            var orderType = sortDescending ? OrderByType.Desc : OrderByType.Asc;

            return sortByLower switch
            {
                // 产品代码 - WarehouseProduct表字段
                "productcode" or "货号" => queryable.OrderBy(wp => wp.ProductCode, orderType),

                // 产品名称 - 需要通过子查询访问Product表
                "productname" or "商品名称" => queryable.OrderBy(
                    wp =>
                        SqlFunc
                            .Subqueryable<Product>()
                            .Where(p => p.ProductCode == wp.ProductCode)
                            .Select(p => p.ProductName),
                    orderType
                ),

                // 货号/料号 - 需要通过子查询访问Product表
                "itemnumber" => queryable.OrderBy(
                    wp =>
                        SqlFunc
                            .Subqueryable<Product>()
                            .Where(p => p.ProductCode == wp.ProductCode)
                            .Select(p => p.ItemNumber ?? wp.ProductCode),
                    orderType
                ),

                // 零售价格 - 需要通过子查询访问Product表
                "retailprice" or "价格" => queryable.OrderBy(
                    wp =>
                        SqlFunc
                            .Subqueryable<Product>()
                            .Where(p => p.ProductCode == wp.ProductCode)
                            .Select(p => p.RetailPrice ?? 0),
                    orderType
                ),

                // 成本价格 - WarehouseProduct表字段
                "domesticprice" or "国内价" => queryable.OrderBy(
                    wp => wp.DomesticPrice ?? 0,
                    orderType
                ),

                "oemprice" or "贴牌价" => queryable.OrderBy(wp => wp.OEMPrice ?? 0, orderType),

                "importprice" or "进口价" => queryable.OrderBy(
                    wp => wp.ImportPrice ?? 0,
                    orderType
                ),

                // 库存数量 - WarehouseProduct表字段
                "stockquantity" or "库存" => queryable.OrderBy(
                    wp => wp.StockQuantity ?? 0,
                    orderType
                ),

                // 最小订购量 - WarehouseProduct表字段
                "minorderquantity" or "最小订购量" => queryable.OrderBy(
                    wp => wp.MinOrderQuantity ?? 0,
                    orderType
                ),

                // 创建时间 - WarehouseProduct表字段
                "createdat" or "创建时间" => queryable.OrderBy(wp => wp.CreatedAt, orderType),

                // 更新时间 - WarehouseProduct表字段
                "updatedat" or "更新时间" => queryable.OrderBy(wp => wp.UpdatedAt, orderType),

                // 默认排序
                _ => queryable.OrderBy(
                    wp =>
                        SqlFunc
                            .Subqueryable<Product>()
                            .Where(p => p.ProductCode == wp.ProductCode)
                            .Select(p => p.ItemNumber ?? wp.ProductCode),
                    OrderByType.Asc
                ),
            };
        }

        /// <summary>
        /// 获取分类及其所有子分类
        /// 递归获取指定分类下的所有子分类GUID，用于分类树形结构的查询
        /// </summary>
        /// <param name="categoryGuid">父分类GUID</param>
        /// <returns>包含父分类及所有子分类的GUID列表</returns>
        private List<string> GetCategoryAndSubCategories(string categoryGuid)
        {
            try
            {
                // 一次性查询所有分类，避免多次数据库访问
                var allCategories = _context.Db.Queryable<WarehouseCategory>().ToList();
                var result = new List<string> { categoryGuid }; // 结果列表，先添加父分类
                //获取指定分类的父分类
                var parentCategory = allCategories.FirstOrDefault(c =>
                    c.CategoryGUID == categoryGuid
                );
                if (parentCategory != null && parentCategory.ParentGUID != null)
                {
                    result.Add(parentCategory.ParentGUID);
                }

                // 递归获取所有子分类
                GetSubCategoriesRecursive(categoryGuid, allCategories, result);

                // 过滤掉空值并返回
                return result.Where(guid => !string.IsNullOrEmpty(guid)).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取子分类失败，分类GUID：{CategoryGuid}", categoryGuid);
                return new List<string> { categoryGuid };
            }
        }

        /// <summary>
        /// 递归获取子分类
        /// 深度优先遍历分类树，获取指定父分类下的所有启用的子分类
        /// </summary>
        /// <param name="parentGuid">父分类GUID</param>
        /// <param name="allCategories">所有分类的缓存列表</param>
        /// <param name="result">结果收集列表（引用传递）</param>
        private void GetSubCategoriesRecursive(
            string parentGuid,
            List<WarehouseCategory> allCategories,
            List<string> result
        )
        {
            // 查找当前父分类下的所有启用子分类
            var children = allCategories
                .Where(c => c.ParentGUID == parentGuid && c.IsActive)
                .ToList();

            foreach (var child in children)
            {
                // 避免重复添加（防止循环引用）
                if (!result.Contains(child.CategoryGUID))
                {
                    result.Add(child.CategoryGUID);
                    // 递归处理子分类的子分类
                    GetSubCategoriesRecursive(child.CategoryGUID, allCategories, result);
                }
            }
        }

        /// <summary>
        /// 获取查询统计信息
        /// 基于已构建的查询条件，分别统计各项关键指标
        /// 注意：所有统计都基于当前查询条件的结果范围
        /// </summary>
        /// <param name="queryable">已构建的查询对象（包含筛选条件）</param>
        /// <param name="query">原始查询参数</param>
        /// <returns>统计信息DTO</returns>
        private async Task<WarehouseProductStatsDto> GetQueryStatsAsync(
            ISugarQueryable<WarehouseProduct> queryable,
            WarehouseProductQueryDto query
        )
        {
            try
            {
                // 🔥 重要说明：以下所有统计都基于当前查询条件的过滤结果
                // 如果需要全库统计，应该传入BuildBaseQuery()而不是带条件的queryable

                // 🔧 修复：直接使用传入的查询对象（已经不包含排序），因为聚合函数不能与ORDER BY一起使用
                var statsQueryable = queryable;

                /*    // 当前查询条件下的商品总数
                   var totalProducts = await statsQueryable.CountAsync();
                   // 当前查询条件下的库存总量
                   var totalStockQuantity = await statsQueryable.SumAsync(wp => wp.StockQuantity ?? 0);
                   // 当前查询条件下的库存总价值
                   var totalStockValue = await statsQueryable.SumAsync(wp => wp.StockValue ?? 0);
   
                   // 🔧 修复：使用Clone()避免修改原始查询对象，并移除排序条件
                   // 当前查询条件下的库存预警商品数量（库存 <= 预警值）
                   var stockAlertCount = await statsQueryable.Clone()
                       .Where(wp => wp.StockQuantity != null && wp.StockAlertQuantity != null && wp.StockQuantity <= wp.StockAlertQuantity)
                       .CountAsync();
   
                   // 当前查询条件下的缺货商品数量（库存 <= 0）
                   var outOfStockCount = await statsQueryable.Clone()
                       .Where(wp => wp.StockQuantity != null && wp.StockQuantity <= 0)
                       .CountAsync();
    */
                // 当前查询条件下的启用商品数量
                var activeProductCount = await statsQueryable
                    .Clone()
                    .Where(wp => wp.IsActive == true)
                    .CountAsync();

                // 🔍 调试信息：记录两种查询方式的结果对比
                var globalActiveCount = await BuildBaseQuery()
                    .Where(wp => wp.IsActive == true)
                    .CountAsync();

                // 构建统计结果（基于当前查询范围）
                return new WarehouseProductStatsDto
                {
                    /*  TotalProducts = totalProducts,
                     TotalStockQuantity = totalStockQuantity,
                     TotalStockValue = totalStockValue,
                     StockAlertCount = stockAlertCount,
                     OutOfStockCount = outOfStockCount, */
                    ActiveProductCount = activeProductCount, // 使用当前查询条件的结果
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取查询统计信息失败，返回默认值");
                // 发生异常时返回空的统计对象，避免整个查询失败
                return new WarehouseProductStatsDto();
            }
        }

        #endregion

        #region 翻译辅助方法

        /// <summary>
        /// 自动翻译单个仓库商品名称（如果包含中文）
        /// </summary>
        private async Task TranslateWarehouseProductNameAsync(WarehouseProductDto? product)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.ProductName))
                return;

            try
            {
                // 检测是否包含中文
                var containsChinese = await _translationService.DetectChineseAsync(
                    product.ProductName
                );
                if (containsChinese)
                {
                    // 翻译为英文
                    var translatedName = await _translationService.TranslateToEnglishAsync(
                        product.ProductName
                    );
                    if (!string.IsNullOrWhiteSpace(translatedName))
                    {
                        var originalName = product.ProductName;
                        product.ProductName = translatedName;
                        _logger.LogDebug(
                            "仓库商品名称已翻译: {OriginalName} -> {TranslatedName}",
                            originalName,
                            translatedName
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "翻译仓库商品名称失败，使用原名称: {ProductName}",
                    product.ProductName
                );
                // 翻译失败时继续使用原名称，不抛出异常
            }
        }

        /// <summary>
        /// 批量翻译仓库商品名称
        /// </summary>
        private async Task TranslateWarehouseProductNamesAsync(
            List<WarehouseProductListDto> products
        )
        {
            if (products == null || !products.Any())
                return;

            try
            {
                // 收集需要翻译的产品名称
                var namesToTranslate = products
                    .Where(p => !string.IsNullOrWhiteSpace(p.ProductBaseName))
                    .Select(p => p.ProductBaseName!)
                    .Distinct()
                    .ToList();

                if (!namesToTranslate.Any())
                    return;

                // 批量翻译
                var translations = await _translationService.BatchTranslateToEnglishAsync(
                    namesToTranslate
                );

                // 应用翻译结果
                foreach (var product in products)
                {
                    if (
                        !string.IsNullOrWhiteSpace(product.ProductBaseName)
                        && translations.ContainsKey(product.ProductBaseName!)
                    )
                    {
                        var translatedName = translations[product.ProductBaseName!];
                        if (
                            !string.IsNullOrWhiteSpace(translatedName)
                            && translatedName != product.ProductBaseName
                        )
                        {
                            _logger.LogDebug(
                                "仓库商品名称已翻译: {OriginalName} -> {TranslatedName}",
                                product.ProductBaseName,
                                translatedName
                            );
                            product.ProductBaseName = translatedName;
                        }
                    }
                }

                _logger.LogInformation("成功翻译 {Count} 个仓库商品名称", products.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "批量翻译仓库商品名称失败，使用原名称");
                // 翻译失败时继续使用原名称，不抛出异常
            }
        }

        #endregion
    }
}
