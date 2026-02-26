using System.Collections.Concurrent;
using System.Data; // 其他方法仍需要DataTable
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 数据同步服务 - 从HQ数据库同步供应商、商品分类、商品信息和库存信息到本地数据库
    /// </summary>
    public class DataSyncService
    {
        private readonly SqlSugarContext _localContext;
        private readonly HqSqlSugarContext _hqContext;
        private readonly HBSalesSqlSugarContext _hbSalesContext;
        private readonly ILogger<DataSyncService> _logger;
        private readonly IMapper _mapper;
        private readonly ITranslationService _translationService;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 构造函数，初始化数据同步服务
        /// </summary>
        /// <param name="localContext">本地数据库上下文</param>
        /// <param name="hqContext">HQ数据库上下文</param>
        /// <param name="hbSalesContext">HBSales数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="mapper">AutoMapper实例</param>
        /// <param name="translationService">翻译服务</param>
        /// <param name="configuration">配置对象</param>
        public DataSyncService(
            SqlSugarContext localContext,
            HqSqlSugarContext hqContext,
            HBSalesSqlSugarContext hbSalesContext,
            ILogger<DataSyncService> logger,
            IMapper mapper,
            ITranslationService translationService,
            IConfiguration configuration
        )
        {
            _localContext = localContext;
            _hqContext = hqContext;
            _hbSalesContext = hbSalesContext;
            _logger = logger;
            _mapper = mapper;
            _translationService = translationService;
            _configuration = configuration;
        }

        /// <summary>
        /// 同步货位信息数据从HQ到本地数据库（使用BulkMerge优化大数据量处理）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncLocationsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation("开始从HQ数据库同步货位信息数据...");

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 从HQ数据库获取货位信息数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    var hqLocationsBatch = await _hqContext
                        .CPT_DIC_货位编码信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqLocationsBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批货位信息，共 {hqLocationsBatch.Count} 条"
                    );

                    // 使用AutoMapper转换HQ货位数据到本地Location实体
                    var localLocations = hqLocationsBatch
                        .Select(hqLocation => _mapper.Map<Location>(hqLocation))
                        .ToList();

                    // 设置创建和更新时间
                    foreach (var location in localLocations)
                    {
                        location.CreatedAt = DateTime.Now;
                        location.UpdatedAt = DateTime.Now;
                    }

                    // 执行BulkMerge操作
                    try
                    {
                        await _localContext
                            .Db.Fastest<Location>()
                            .AS("Location")
                            .BulkMergeAsync(localLocations, new string[] { "LocationGuid" });

                        _logger.LogInformation($"第 {pageNumber} 批货位信息BulkMerge操作完成");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"第 {pageNumber} 批货位信息BulkMerge操作失败");
                        totalErrors += hqLocationsBatch.Count;
                    }

                    totalProcessed += hqLocationsBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = true;
                result.Message =
                    $"货位信息同步完成！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步货位信息数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 🚀 同步货位商品关联数据从HQ到本地数据库（使用AutoMapper + 合并存货/配货信息）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncProductLocationsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation(
                    "🚀 开始从HQ数据库同步货位商品关联数据（AutoMapper模式）..."
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 从HQ数据库获取货位存货信息和配货信息数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    // 获取货位存货信息
                    var hqStockLocationsBatch = await _hqContext
                        .CPT_RED_货位存货信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize / 2)
                        .Take(batchSize / 2)
                        .ToListAsync();

                    // 获取货位配货信息
                    var hqPickLocationsBatch = await _hqContext
                        .CPT_RED_货位配货信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize / 2)
                        .Take(batchSize / 2)
                        .ToListAsync();

                    if (!hqStockLocationsBatch.Any() && !hqPickLocationsBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批货位商品关联信息 - 存货: {hqStockLocationsBatch.Count}条, 配货: {hqPickLocationsBatch.Count}条"
                    );

                    // 🚀 收集所有需要查询的货位编码（从两个实体中）
                    var locationCodes = new HashSet<string>();

                    // 收集货位存货信息中的货位编码
                    foreach (
                        var hqStockLocation in hqStockLocationsBatch.Where(x => x?.货位编码 != null)
                    )
                    {
                        locationCodes.Add(hqStockLocation.货位编码!);
                    }

                    // 收集货位配货信息中的货位编码
                    foreach (
                        var hqPickLocation in hqPickLocationsBatch.Where(x => x?.货位编码 != null)
                    )
                    {
                        locationCodes.Add(hqPickLocation.货位编码!);
                    }

                    // 🚀 批量查询所有需要的货位信息，建立编码到GUID的映射
                    var locationDict = new Dictionary<string, string>();
                    if (locationCodes.Any())
                    {
                        var locations = await _localContext
                            .LocationDb.AsQueryable()
                            .Where(l =>
                                l.LocationCode != null && locationCodes.Contains(l.LocationCode)
                            )
                            .ToListAsync();

                        // 创建货位编码到货位GUID的字典，方便快速查找
                        locationDict = locations
                            .Where(l =>
                                !string.IsNullOrEmpty(l.LocationCode)
                                && !string.IsNullOrEmpty(l.LocationGuid)
                            )
                            .ToDictionary(l => l.LocationCode!, l => l.LocationGuid!);
                    }

                    // 🚀 使用AutoMapper将HQ货位存货信息转换为本地ProductLocation实体
                    var stockProductLocations = _mapper.Map<List<ProductLocation>>(
                        hqStockLocationsBatch
                    );

                    // 🚀 使用AutoMapper将HQ货位配货信息转换为本地ProductLocation实体
                    var pickProductLocations = _mapper.Map<List<ProductLocation>>(
                        hqPickLocationsBatch
                    );

                    // 🚀 合并两个列表，并更新LocationGuid（从货位编码转换为GUID）
                    var allProductLocations = new List<ProductLocation>();

                    // 处理存货信息转换的ProductLocation
                    foreach (
                        var productLocation in stockProductLocations.Where(pl =>
                            !string.IsNullOrEmpty(pl.ProductCode)
                        )
                    )
                    {
                        // 根据货位编码从字典中查找真实的LocationGuid
                        if (
                            locationDict.TryGetValue(
                                productLocation.LocationGuid ?? "",
                                out var realLocationGuid
                            )
                        )
                        {
                            productLocation.LocationGuid = realLocationGuid;
                            allProductLocations.Add(productLocation);
                        }
                        else
                        {
                            _logger.LogWarning(
                                $"⚠️ 存货记录中找不到货位编码 {productLocation.LocationGuid} 对应的Location"
                            );
                        }
                    }

                    // 处理配货信息转换的ProductLocation
                    foreach (
                        var productLocation in pickProductLocations.Where(pl =>
                            !string.IsNullOrEmpty(pl.ProductCode)
                        )
                    )
                    {
                        // 根据货位编码从字典中查找真实的LocationGuid
                        if (
                            locationDict.TryGetValue(
                                productLocation.LocationGuid ?? "",
                                out var realLocationGuid
                            )
                        )
                        {
                            productLocation.LocationGuid = realLocationGuid;
                            allProductLocations.Add(productLocation);
                        }
                        else
                        {
                            _logger.LogWarning(
                                $"⚠️ 配货记录中找不到货位编码 {productLocation.LocationGuid} 对应的Location"
                            );
                        }
                    }

                    _logger.LogInformation(
                        $"AutoMapper转换完成，生成 {allProductLocations.Count} 个ProductLocation对象（存货: {stockProductLocations.Count}, 配货: {pickProductLocations.Count}）"
                    );

                    // 🚀 使用Storageable处理Insert/Update逻辑
                    try
                    {
                        if (allProductLocations.Any())
                        {
                            var storageResult = await _localContext
                                .Db.Storageable(allProductLocations)
                                .WhereColumns(x => x.Guid) // 基于GUID进行判断
                                .ToStorageAsync();

                            // 执行插入和更新
                            var insertResult = storageResult.AsInsertable.ExecuteCommand();
                            var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                            totalAdded += insertResult;
                            totalUpdated += updateResult;

                            _logger.LogInformation(
                                $"第 {pageNumber} 批货位关联AutoMapper处理完成 - 新增: {insertResult}, 更新: {updateResult}"
                            );
                        }
                        else
                        {
                            _logger.LogInformation(
                                $"第 {pageNumber} 批货位关联AutoMapper转换后无有效数据"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"第 {pageNumber} 批货位商品关联AutoMapper处理失败");
                        totalErrors += hqStockLocationsBatch.Count + hqPickLocationsBatch.Count;
                    }

                    totalProcessed += hqStockLocationsBatch.Count + hqPickLocationsBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"货位商品关联信息同步完成（使用AutoMapper转换）！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步货位商品关联数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 🚀 同步供应商数据从HQ到本地数据库（使用AutoMapper + 批量插入更新操作）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncSuppliersFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation("🚀 开始从HQ数据库同步供应商数据（AutoMapper模式）...");

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 从HQ数据库获取供应商数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    var hqSuppliersBatch = await _hqContext
                        .CBP_DIC_国内供应商信息表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqSuppliersBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批供应商，共 {hqSuppliersBatch.Count} 条"
                    );

                    // 🚀 使用AutoMapper将HQ实体转换为本地实体列表
                    var chinaSuppliers = _mapper.Map<List<ChinaSupplier>>(hqSuppliersBatch);

                    _logger.LogInformation(
                        $"AutoMapper转换完成，生成 {chinaSuppliers.Count} 个ChinaSupplier对象"
                    );

                    // 🚀 使用Storageable处理Insert/Update逻辑
                    try
                    {
                        var storageResult = await _localContext
                            .Db.Storageable(chinaSuppliers)
                            .WhereColumns(x => x.SupplierCode) // 基于供应商编码进行判断
                            .ToStorageAsync();

                        // 执行插入和更新
                        var insertResult = storageResult.AsInsertable.ExecuteCommand();
                        var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                        totalAdded += insertResult;
                        totalUpdated += updateResult;

                        _logger.LogInformation(
                            $"第 {pageNumber} 批供应商AutoMapper处理完成 - 新增: {insertResult}, 更新: {updateResult}"
                        );

                        // 🚀 输出前3个转换结果的示例（用于调试）
                        foreach (var supplier in chinaSuppliers.Take(3))
                        {
                            _logger.LogDebug(
                                $"   示例供应商: {supplier.SupplierCode} - {supplier.SupplierName} (状态: {supplier.Status})"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"第 {pageNumber} 批供应商AutoMapper处理失败");
                        totalErrors += hqSuppliersBatch.Count;
                    }

                    totalProcessed += hqSuppliersBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"供应商同步完成（使用AutoMapper转换）！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步供应商数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 从HQ数据库获取供应商数据
        /// </summary>
        /// <returns>供应商数据列表</returns>
        private async Task<List<CBP_DIC_国内供应商信息表>> GetHqSuppliersAsync()
        {
            try
            {
                // 从HQ数据库查询供应商信息
                var hqSuppliers = await _hqContext.CBP_DIC_国内供应商信息表Db.GetListAsync();

                _logger.LogInformation($"成功从HQ数据库获取 {hqSuppliers.Count} 个供应商");

                return hqSuppliers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从HQ数据库获取供应商数据失败");
                throw;
            }
        }

        /// <summary>
        /// 同步单个供应商数据
        /// </summary>
        /// <param name="hqSupplier">HQ供应商数据</param>
        /// <param name="localSuppliers">本地供应商数据列表</param>
        /// <param name="result">同步结果</param>
        private async Task SyncSingleSupplierAsync(
            CBP_DIC_国内供应商信息表 hqSupplier,
            List<ChinaSupplier> localSuppliers,
            SyncResult result
        )
        {
            // 查找本地是否已存在该供应商（根据供应商编码）
            var existingSupplier = localSuppliers.FirstOrDefault(s =>
                s.SupplierCode == hqSupplier.H供应商编码
            );

            if (existingSupplier == null)
            {
                // 新增供应商
                var newSupplier = new ChinaSupplier
                {
                    Guid = hqSupplier.HGUID ?? UuidHelper.GenerateUuid7(),
                    SupplierCode = hqSupplier.H供应商编码,
                    SupplierName = hqSupplier.H供应商名称,
                    ShopNumber = hqSupplier.H商铺编号,
                    ContactPerson = hqSupplier.H联系人,
                    Phone = hqSupplier.H电话,
                    Email = hqSupplier.HEMAIL地址,
                    StorefrontPhoto = hqSupplier.H商户门头照片,
                    Remarks = hqSupplier.备注,
                    Status = hqSupplier.状态,
                    FGC_Creator = hqSupplier.FGC_Creator,
                    FGC_CreateDate = hqSupplier.FGC_CreateDate,
                    FGC_LastModifier = hqSupplier.FGC_LastModifier,
                    FGC_LastModifyDate = hqSupplier.FGC_LastModifyDate,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await _localContext.ChinaSupplierDb.InsertAsync(newSupplier);
                result.AddedCount++;
                _logger.LogInformation(
                    $"新增供应商: {newSupplier.SupplierCode} - {newSupplier.SupplierName}"
                );
            }
            else
            {
                // 更新现有供应商（只更新关键信息）
                bool needUpdate = false;

                if (existingSupplier.SupplierName != hqSupplier.H供应商名称)
                {
                    existingSupplier.SupplierName = hqSupplier.H供应商名称;
                    needUpdate = true;
                }

                if (existingSupplier.ShopNumber != hqSupplier.H商铺编号)
                {
                    existingSupplier.ShopNumber = hqSupplier.H商铺编号;
                    needUpdate = true;
                }

                if (existingSupplier.ContactPerson != hqSupplier.H联系人)
                {
                    existingSupplier.ContactPerson = hqSupplier.H联系人;
                    needUpdate = true;
                }

                if (existingSupplier.Phone != hqSupplier.H电话)
                {
                    existingSupplier.Phone = hqSupplier.H电话;
                    needUpdate = true;
                }

                if (existingSupplier.Email != hqSupplier.HEMAIL地址)
                {
                    existingSupplier.Email = hqSupplier.HEMAIL地址;
                    needUpdate = true;
                }

                if (existingSupplier.Remarks != hqSupplier.备注)
                {
                    existingSupplier.Remarks = hqSupplier.备注;
                    needUpdate = true;
                }

                if (existingSupplier.Status != hqSupplier.状态)
                {
                    existingSupplier.Status = hqSupplier.状态;
                    needUpdate = true;
                }

                if (needUpdate)
                {
                    existingSupplier.FGC_LastModifier = hqSupplier.FGC_LastModifier;
                    existingSupplier.FGC_LastModifyDate = hqSupplier.FGC_LastModifyDate;
                    existingSupplier.UpdatedAt = DateTime.Now;
                    await _localContext.ChinaSupplierDb.UpdateAsync(existingSupplier);
                    result.UpdatedCount++;
                    _logger.LogInformation(
                        $"更新供应商: {existingSupplier.SupplierCode} - {existingSupplier.SupplierName}"
                    );
                }
            }
        }

        /// <summary>
        /// 🚀 同步商品分类数据从HQ到本地数据库（使用AutoMapper + 批量插入更新操作）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncCategoriesFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation("🚀 开始从HQ数据库同步商品分类数据（AutoMapper模式）...");

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 从HQ数据库获取商品分类数据 (使用批量操作)
                const int batchSize = 5000; // 每批处理5000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    var hqCategoriesBatch = await _hqContext
                        .CBP_DIC_商品分类码表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqCategoriesBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批商品分类，共 {hqCategoriesBatch.Count} 条"
                    );

                    // 🚀 使用AutoMapper将HQ实体转换为本地实体列表
                    var warehouseCategories = _mapper.Map<List<WarehouseCategory>>(
                        hqCategoriesBatch
                    );

                    _logger.LogInformation(
                        $"AutoMapper转换完成，生成 {warehouseCategories.Count} 个WarehouseCategory对象"
                    );

                    // 🚀 使用Storageable处理Insert/Update逻辑
                    try
                    {
                        var storageResult = await _localContext
                            .Db.Storageable(warehouseCategories)
                            .WhereColumns(x => x.CategoryGUID) // 基于分类GUID进行判断
                            .ToStorageAsync();

                        // 执行插入和更新
                        var insertResult = storageResult.AsInsertable.ExecuteCommand();
                        var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                        totalAdded += insertResult;
                        totalUpdated += updateResult;

                        _logger.LogInformation(
                            $"第 {pageNumber} 批商品分类AutoMapper处理完成 - 新增: {insertResult}, 更新: {updateResult}"
                        );

                        // 🚀 输出前3个转换结果的示例（用于调试）
                        foreach (var category in warehouseCategories.Take(3))
                        {
                            _logger.LogDebug(
                                $"   示例分类: {category.CategoryName} - {category.ChineseName} (父级: {category.ParentGUID})"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"第 {pageNumber} 批商品分类AutoMapper处理失败");
                        totalErrors += hqCategoriesBatch.Count;
                    }

                    totalProcessed += hqCategoriesBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"商品分类同步完成（使用AutoMapper转换）！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品分类数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 同步单个商品分类数据
        /// </summary>
        /// <param name="hqCategory">HQ商品分类数据</param>
        /// <param name="localCategories">本地商品分类数据列表</param>
        /// <param name="result">同步结果</param>
        private async Task SyncSingleCategoryAsync(
            CBP_DIC_商品分类码表 hqCategory,
            List<WarehouseCategory> localCategories,
            SyncResult result
        )
        {
            // 查找本地是否已存在该商品分类（根据GUID）
            var existingCategory = localCategories.FirstOrDefault(c =>
                c.CategoryGUID == hqCategory.HGUID
            );

            if (existingCategory == null)
            {
                // 新增商品分类
                var newCategory = new WarehouseCategory
                {
                    CategoryGUID = hqCategory.HGUID ?? UuidHelper.GenerateUuid7(),
                    ParentGUID = hqCategory.H父级GUID,
                    CategoryName = hqCategory.H类别名称 ?? "",
                    ChineseName = hqCategory.H中文名称,
                    IsActive = true, // 默认启用
                    CreatedBy = hqCategory.FGC_Creator,
                    UpdatedBy = hqCategory.FGC_LastModifier,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };

                await _localContext.WarehouseCategoryDb.InsertAsync(newCategory);
                result.AddedCount++;
                _logger.LogInformation($"新增商品分类: {newCategory.CategoryName}");
            }
            else
            {
                // 更新现有商品分类（只更新关键信息）
                bool needUpdate = false;

                if (existingCategory.ParentGUID != hqCategory.H父级GUID)
                {
                    existingCategory.ParentGUID = hqCategory.H父级GUID;
                    needUpdate = true;
                }

                if (existingCategory.CategoryName != hqCategory.H类别名称)
                {
                    existingCategory.CategoryName = hqCategory.H类别名称 ?? "";
                    needUpdate = true;
                }

                if (existingCategory.ChineseName != hqCategory.H中文名称)
                {
                    existingCategory.ChineseName = hqCategory.H中文名称;
                    needUpdate = true;
                }

                if (needUpdate)
                {
                    existingCategory.UpdatedBy = hqCategory.FGC_LastModifier;
                    existingCategory.UpdatedAt = DateTime.Now;
                    await _localContext.WarehouseCategoryDb.UpdateAsync(existingCategory);
                    result.UpdatedCount++;
                    _logger.LogInformation($"更新商品分类: {existingCategory.CategoryName}");
                }
            }
        }

        /// <summary>
        /// 同步商品信息数据从HQ到本地数据库（使用BulkCopy优化大数据量处理）
        /// 🚀 同时同步商品字典和一品多码表到ProductSetCode表
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncProductsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation(
                    "🚀 开始从HQ数据库同步商品信息数据（包括商品字典和一品多码表到ProductSetCode）..."
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 🔄 开启事务，确保数据一致性
                await _localContext.Db.Ado.BeginTranAsync();

                try
                {
                    // 2. 先清空相关表数据
                    _logger.LogInformation("清空本地ProductSetCode表和Product表数据...");
                    await _localContext.Db.Deleteable<ProductSetCode>().ExecuteCommandAsync();
                    await _localContext
                        .Db.Deleteable<Product>()
                        .AS("Product")
                        .ExecuteCommandAsync();
                    _logger.LogInformation("已清空本地ProductSetCode表和Product表数据");

                    // 3. 从HQ数据库获取商品信息数据 (使用批量操作)
                    const int batchSize = 50000; // 每批处理50000条记录，避免超时和内存问题
                    var totalProcessed = 0;
                    var totalProductAdded = 0;
                    var totalErrors = 0;
                    var pageNumber = 1;

                    _logger.LogInformation("开始同步商品字典表数据...");

                    while (true)
                    {
                        var hqProductsBatch = await _hqContext
                            .DIC_商品信息字典表Db.AsQueryable()
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();

                        if (!hqProductsBatch.Any())
                            break; // 没有更多数据

                        _logger.LogInformation(
                            $"从HQ数据库获取到第 {pageNumber} 批商品信息，共 {hqProductsBatch.Count} 条"
                        );

                        try
                        {
                            // 🚀 处理Product数据
                            // 1. 转换为Product实体
                            var localProducts = hqProductsBatch
                                .Select(hqProduct => _mapper.Map<Product>(hqProduct))
                                .ToList();

                            // 2. 批量插入Product数据
                            await _localContext
                                .Db.Fastest<Product>()
                                .AS("Product")
                                .PageSize(10000) // 减小页面大小，避免超时
                                .BulkCopyAsync(localProducts);

                            totalProductAdded += localProducts.Count;
                            totalProcessed += hqProductsBatch.Count;

                            _logger.LogInformation(
                                $"第 {pageNumber} 批处理完成 - Product: {localProducts.Count} 条"
                            );

                            // 每处理一批后稍微延迟，避免数据库压力过大
                            if (pageNumber % 5 == 0)
                            {
                                await Task.Delay(1000); // 每5批延迟1秒
                                _logger.LogInformation(
                                    $"已处理 {pageNumber} 批数据，总计 {totalProductAdded} 条Product记录"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"第 {pageNumber} 批商品数据处理失败");
                            totalErrors += hqProductsBatch.Count;

                            // 出现错误后延迟更长时间再继续
                            await Task.Delay(2000);
                        }

                        pageNumber++;
                    }

                    _logger.LogInformation($"商品字典表同步完成 - Product: {totalProductAdded} 条");

                    // 🚀 4. 使用JOIN连接查询同步一品多码表数据到ProductSetCode（只同步与已同步商品相关的记录）
                    _logger.LogInformation(
                        "开始使用JOIN连接查询同步一品多码表数据到ProductSetCode..."
                    );

                    var totalMultiCodeAdded = 0;
                    pageNumber = 1;

                    while (true)
                    {
                        // 🔧 优化策略：使用JOIN连接查询，直接关联商品信息表和一品多码表
                        // 这样可以避免生成超长的IN语句，提高查询性能
                        var hqMultiCodesBatch = await _hqContext
                            .Db.Queryable<
                                BlazorApp.Shared.Models.HqEntities.DIC_一品多码表,
                                BlazorApp.Shared.Models.HqEntities.DIC_商品信息字典表
                            >(
                                (multiCode, product) =>
                                    new JoinQueryInfos(
                                        JoinType.Inner,
                                        multiCode.H商品编码 == product.H商品编码
                                    )
                            )
                            .Where(
                                (multiCode, product) =>
                                    !string.IsNullOrEmpty(multiCode.H多码商品编号)
                                    && !string.IsNullOrEmpty(multiCode.H商品编码)
                            )
                            .Select((multiCode, product) => multiCode)
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();

                        if (!hqMultiCodesBatch.Any())
                            break; // 没有更多数据

                        _logger.LogInformation(
                            $"从HQ数据库获取到第 {pageNumber} 批一品多码数据，共 {hqMultiCodesBatch.Count} 条"
                        );

                        try
                        {
                            // 转换为ProductSetCode实体（从一品多码表）
                            var multiCodeSetCodes = hqMultiCodesBatch
                                .Select(hqMultiCode => _mapper.Map<ProductSetCode>(hqMultiCode))
                                .ToList();

                            // 批量插入ProductSetCode数据（多码信息）
                            await _localContext
                                .Db.Insertable(multiCodeSetCodes)
                                .PageSize(2000) // 进一步减小页面大小，避免超时
                                .ExecuteCommandAsync();

                            totalMultiCodeAdded += multiCodeSetCodes.Count;
                            _logger.LogInformation(
                                $"第 {pageNumber} 批一品多码数据处理完成 - ProductSetCode: {multiCodeSetCodes.Count} 条"
                            );

                            // 每处理一批后稍微延迟，避免数据库压力过大
                            if (pageNumber % 3 == 0)
                            {
                                await Task.Delay(1500); // 每3批延迟1.5秒
                                _logger.LogInformation(
                                    $"已处理 {pageNumber} 批一品多码数据，总计 {totalMultiCodeAdded} 条ProductSetCode记录"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"第 {pageNumber} 批一品多码数据处理失败");
                            totalErrors += hqMultiCodesBatch.Count;

                            // 出现错误后延迟更长时间再继续
                            await Task.Delay(3000);
                        }

                        pageNumber++;
                    }

                    _logger.LogInformation(
                        $"一品多码表同步完成 - ProductSetCode: {totalMultiCodeAdded} 条"
                    );

                    // 🎉 提交事务
                    await _localContext.Db.Ado.CommitTranAsync();

                    result.AddedCount = totalProductAdded + totalMultiCodeAdded;
                    result.UpdatedCount = 0; // 由于是先删除再插入，所以没有更新操作
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = true;
                    result.Message =
                        $"🎉 商品信息同步完成！Product表: {totalProductAdded} 条，ProductSetCode表: {totalMultiCodeAdded} 条（多码），错误: {totalErrors} 条";
                    _logger.LogInformation(result.Message);
                }
                catch (Exception)
                {
                    // 🔙 回滚事务
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品信息数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 异步处理商品信息批次
        /// </summary>
        /// <param name="localProducts">本地产品列表</param>
        /// <param name="pageNumber">页码</param>
        /// <param name="semaphore">并发控制信号量</param>
        /// <returns>批次处理结果</returns>
        private async Task<BatchResult> ProcessProductBatchAsync(
            List<Product> localProducts,
            int pageNumber,
            SemaphoreSlim? semaphore
        )
        {
            // 如果使用了并发控制，则等待信号量
            if (semaphore != null)
            {
                await semaphore.WaitAsync();
            }

            try
            {
                // 执行BulkCopy操作，添加重试机制
                const int maxRetries = 3;
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        // 检查并确保数据库连接是打开的
                        if (_localContext.Db.Ado.Transaction == null)
                        {
                            // 如果没有事务，确保连接是可用的
                            if (
                                _localContext.Db.Ado.Connection.State
                                != System.Data.ConnectionState.Open
                            )
                            {
                                var dbConnection =
                                    _localContext.Db.Ado.Connection
                                    as System.Data.Common.DbConnection;
                                if (dbConnection != null)
                                {
                                    await dbConnection.OpenAsync();
                                }
                                else
                                {
                                    _localContext.Db.Ado.Connection.Open();
                                }
                            }
                        }
                        else
                        {
                            // 即使在事务中，也要确保连接是打开的
                            if (
                                _localContext.Db.Ado.Connection.State
                                != System.Data.ConnectionState.Open
                            )
                            {
                                var dbConnection =
                                    _localContext.Db.Ado.Connection
                                    as System.Data.Common.DbConnection;
                                if (dbConnection != null)
                                {
                                    await dbConnection.OpenAsync();
                                }
                                else
                                {
                                    _localContext.Db.Ado.Connection.Open();
                                }
                            }
                        }

                        // 再次检查连接状态
                        if (
                            _localContext.Db.Ado.Connection.State
                            != System.Data.ConnectionState.Open
                        )
                        {
                            throw new InvalidOperationException("无法打开数据库连接");
                        }

                        // 使用BulkCopy插入数据
                        await _localContext
                            .Db.Fastest<Product>()
                            .AS("Product")
                            .PageSize(50000)
                            .BulkCopyAsync(localProducts);

                        _logger.LogInformation(
                            $"第 {pageNumber} 批商品信息BulkCopy操作完成，插入 {localProducts.Count} 条记录"
                        );

                        return new BatchResult
                        {
                            IsSuccess = true,
                            ProcessedCount = localProducts.Count,
                            PageNumber = pageNumber,
                        };
                    }
                    catch (Exception ex)
                        when (attempt < maxRetries
                            && (
                                ex.Message.Contains("连接被关闭")
                                || ex.Message.Contains("connection is closed")
                                || ex.Message.Contains("Invalid operation")
                                || ex is System.InvalidOperationException
                                || ex.Message.Contains("Connection closed")
                                || ex.Message.Contains("closed connection")
                            )
                        )
                    {
                        _logger.LogWarning(
                            $"第 {pageNumber} 批商品信息BulkCopy操作失败 (尝试 {attempt}/{maxRetries}): {ex.Message}"
                        );
                        // 等待一段时间后重试
                        await Task.Delay(1000 * attempt);
                    }
                }

                // 如果所有重试都失败了
                _logger.LogError($"第 {pageNumber} 批商品信息BulkCopy操作最终失败");
                return new BatchResult
                {
                    IsSuccess = false,
                    ProcessedCount = localProducts.Count,
                    PageNumber = pageNumber,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"第 {pageNumber} 批商品信息BulkCopy操作失败");
                return new BatchResult
                {
                    IsSuccess = false,
                    ProcessedCount = localProducts.Count,
                    PageNumber = pageNumber,
                };
            }
            finally
            {
                // 如果使用了并发控制，则释放信号量
                if (semaphore != null)
                {
                    semaphore.Release();
                }
            }
        }

        /// <summary>
        /// 🚀 同步商品库存数据从HQ到本地数据库（使用AutoMapper + 导航查询 + 批量插入）
        /// 特性：清空重建策略、导航查询关联商品信息、AutoMapper类型安全转换
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncProductStocksFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation(
                    "🚀 开始从HQ数据库同步商品库存数据（AutoMapper + 导航查询模式）..."
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 先清空本地WarehouseProduct表
                _logger.LogInformation("清空本地WarehouseProduct表...");
                var deletedCount = await _localContext
                    .Db.Deleteable<WarehouseProduct>()
                    .ExecuteCommandAsync();
                _logger.LogInformation($"已清空 {deletedCount} 条WarehouseProduct记录");

                // 3. 从HQ数据库获取所有商品库存数据
                var totalInserted = 0;
                var totalErrors = 0;
                const int batchSize = 5000; // 每批处理5000条记录
                var pageNumber = 1;

                while (true)
                {
                    // 🚀 使用导航查询，同时获取库存信息和关联的商品信息
                    var hqStocksBatch = await _hqContext
                        .CBP_DIC_商品库存表Db.AsQueryable()
                        .Includes(x => x.商品信息) // 使用导航属性加载关联的商品信息
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqStocksBatch.Any())
                        break; // 没有更多数据

                    var withProductInfoCount = hqStocksBatch.Count(x => x.商品信息 != null);
                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批商品库存，共 {hqStocksBatch.Count} 条，其中 {withProductInfoCount} 条有关联的商品信息"
                    );

                    // 🚀 使用AutoMapper将HQ实体转换为本地实体列表
                    var warehouseProducts = _mapper.Map<List<WarehouseProduct>>(hqStocksBatch);

                    _logger.LogInformation(
                        $"AutoMapper转换完成，生成 {warehouseProducts.Count} 个WarehouseProduct对象"
                    );

                    // 🚀 执行批量插入操作（使用对象列表而非DataTable）
                    try
                    {
                        await _localContext.Db.Insertable(warehouseProducts).ExecuteCommandAsync();

                        totalInserted += warehouseProducts.Count;
                        _logger.LogInformation(
                            $"第 {pageNumber} 批商品库存AutoMapper批量插入完成，已插入 {warehouseProducts.Count} 条"
                        );

                        // 🚀 输出前3个转换结果的示例（用于调试）
                        foreach (var product in warehouseProducts.Take(3))
                        {
                            _logger.LogDebug(
                                $"   示例商品: {product.ProductCode} (库存: {product.StockQuantity}, 价格: {product.OEMPrice:C2})"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"第 {pageNumber} 批商品库存AutoMapper批量插入失败");
                        totalErrors += hqStocksBatch.Count;
                    }

                    pageNumber++;
                }

                result.AddedCount = totalInserted;
                result.UpdatedCount = 0; // 清空重建模式下没有更新操作
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"商品库存同步完成（使用AutoMapper转换）！清空: {deletedCount} 条，新增: {totalInserted} 条，错误: {totalErrors} 条";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步商品库存数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 🚀 导航查询示例：根据商品编码查询商品及其库存信息
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品信息及关联的库存信息</returns>
        public async Task<CPT_DIC_商品信息字典表?> GetProductWithStockInfoAsync(string productCode)
        {
            try
            {
                _logger.LogInformation($"🔍 使用导航查询获取商品 {productCode} 的完整信息...");

                // 🚀 从商品信息表出发，使用导航属性查询关联的库存信息
                var productWithStock = await _hqContext
                    .CPT_DIC_商品信息字典表_HQDb.AsQueryable()
                    .Includes(x => x.库存信息) // 使用导航属性加载关联的库存信息
                    .Where(x => x.商品编码 == productCode)
                    .FirstAsync();

                if (productWithStock?.库存信息 != null)
                {
                    _logger.LogInformation($"✅ 商品 {productCode} 查询成功：");
                    _logger.LogInformation(
                        $"   - 商品名称: {productWithStock.中文名称 ?? productWithStock.英文名称}"
                    );
                    _logger.LogInformation($"   - 当前库存: {productWithStock.库存信息.H库存}");
                    _logger.LogInformation(
                        $"   - 最小订货量: {productWithStock.库存信息.H最小订货量}"
                    );
                    _logger.LogInformation(
                        $"   - 贴牌价格: {productWithStock.库存信息.H贴牌价格:C2}"
                    );
                }
                else
                {
                    _logger.LogWarning($"⚠️ 商品 {productCode} 没有关联的库存信息");
                }

                return productWithStock;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ 查询商品 {productCode} 信息时发生错误");
                return null;
            }
        }

        /// <summary>
        /// 🚀 导航查询示例：根据库存状态查询商品列表
        /// </summary>
        /// <param name="minStockThreshold">最小库存阈值</param>
        /// <returns>库存不足的商品列表</returns>
        public async Task<List<CBP_DIC_商品库存表>> GetLowStockProductsAsync(
            decimal minStockThreshold = 100
        )
        {
            try
            {
                _logger.LogInformation(
                    $"🔍 使用导航查询获取库存低于 {minStockThreshold} 的商品..."
                );

                // 🚀 从库存表出发，使用导航属性查询关联的商品信息
                var lowStockProducts = await _hqContext
                    .CBP_DIC_商品库存表Db.AsQueryable()
                    .Includes(x => x.商品信息) // 使用导航属性加载关联的商品信息
                    .Where(x => x.H库存 < minStockThreshold && x.H使用状态 == 1)
                    .OrderBy(x => x.H库存) // 按库存从低到高排序
                    .Take(50) // 限制返回50条
                    .ToListAsync();

                _logger.LogInformation($"✅ 找到 {lowStockProducts.Count} 个库存不足的商品");

                foreach (var product in lowStockProducts.Take(5)) // 只记录前5个
                {
                    var productName =
                        product.商品信息?.中文名称 ?? product.商品信息?.英文名称 ?? "未知商品";
                    _logger.LogInformation(
                        $"   - {product.H商品编码}: {productName}, 库存: {product.H库存}"
                    );
                }

                return lowStockProducts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 查询库存不足商品时发生错误");
                return new List<CBP_DIC_商品库存表>();
            }
        }

        /// <summary>
        /// 🚀 AutoMapper转换示例：将HQ商品库存数据转换为本地WarehouseProduct对象
        /// </summary>
        /// <param name="productCodes">要转换的商品编码列表</param>
        /// <returns>转换后的WarehouseProduct列表</returns>
        public async Task<List<WarehouseProduct>> ConvertHqStocksToWarehouseProductsAsync(
            List<string> productCodes
        )
        {
            try
            {
                _logger.LogInformation(
                    $"🔄 开始使用AutoMapper转换 {productCodes.Count} 个商品的库存数据..."
                );

                // 🚀 使用导航查询获取HQ数据
                var hqStocks = await _hqContext
                    .CBP_DIC_商品库存表Db.AsQueryable()
                    .Includes(x => x.商品信息) // 使用导航属性
                    .Where(x =>
                        !string.IsNullOrEmpty(x.H商品编码) && productCodes.Contains(x.H商品编码)
                    )
                    .ToListAsync();

                _logger.LogInformation($"📊 从HQ获取到 {hqStocks.Count} 条库存记录");

                // 🚀 使用AutoMapper进行批量转换
                var warehouseProducts = _mapper.Map<List<WarehouseProduct>>(hqStocks);

                _logger.LogInformation(
                    $"✅ AutoMapper转换完成，生成 {warehouseProducts.Count} 个WarehouseProduct对象"
                );

                // 🚀 输出转换统计信息
                var withProductInfo = warehouseProducts.Count(x =>
                    !string.IsNullOrEmpty(x.ProductCode)
                );
                var totalValue = warehouseProducts.Sum(x => x.StockValue ?? 0);
                var totalStock = warehouseProducts.Sum(x => x.StockQuantity ?? 0);

                _logger.LogInformation(
                    $"📈 转换统计: 有详细信息: {withProductInfo}个, 总库存值: {totalValue:C2}, 总库存量: {totalStock}"
                );

                return warehouseProducts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ AutoMapper转换HQ库存数据时发生错误");
                return new List<WarehouseProduct>();
            }
        }

        /// <summary>
        /// 🚀 增量同步商品信息数据从HQ到本地数据库（基于更新时间筛选）
        /// 🔄 同时增量同步商品字典和一品多码表到ProductSetCode表
        /// </summary>
        /// <param name="lastUpdateDate">上次更新日期，只同步此日期之后更新的商品</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncProductsIncrementalFromHqAsync(DateTime lastUpdateDate)
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation(
                    $"🚀 开始从HQ数据库增量同步商品信息数据（包括商品字典和一品多码表到ProductSetCode）（上次更新时间: {lastUpdateDate:yyyy-MM-dd HH:mm:ss}）..."
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 🔄 开启事务，确保数据一致性
                await _localContext.Db.Ado.BeginTranAsync();

                try
                {
                    // 2. 从HQ数据库获取指定日期后更新的商品信息数据
                    const int batchSize = 5000; // 每批处理5000条记录，增量同步不需要太大批次
                    var totalProcessed = 0;
                    var totalProductAdded = 0;
                    var totalProductUpdated = 0;
                    var totalErrors = 0;
                    var pageNumber = 1;

                    _logger.LogInformation("开始增量同步商品字典表数据...");

                    while (true)
                    {
                        var hqProductsBatch = await _hqContext
                            .DIC_商品信息字典表Db.AsQueryable()
                            .Where(x => x.FGC_LastModifyDate >= lastUpdateDate) // 只获取指定日期后更新的商品
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();

                        if (!hqProductsBatch.Any())
                            break; // 没有更多数据

                        _logger.LogInformation(
                            $"从HQ数据库获取到第 {pageNumber} 批更新的商品信息，共 {hqProductsBatch.Count} 条"
                        );

                        try
                        {
                            // 🚀 处理Product数据的增量同步
                            // 1. 转换为Product实体
                            var localProducts = hqProductsBatch
                                .Select(hqProduct => _mapper.Map<Product>(hqProduct))
                                .ToList();

                            // 2. 处理Product增量同步
                            var productStorageResult = await _localContext
                                .Db.Storageable(localProducts)
                                .WhereColumns(x => x.ProductCode) // 基于商品编码进行判断
                                .ToStorageAsync();

                            var productInsertResult =
                                productStorageResult.AsInsertable.ExecuteCommand();
                            var productUpdateResult =
                                productStorageResult.AsUpdateable.ExecuteCommand();

                            totalProductAdded += productInsertResult;
                            totalProductUpdated += productUpdateResult;

                            _logger.LogInformation(
                                $"第 {pageNumber} 批商品字典增量同步完成 - Product新增: {productInsertResult}, 更新: {productUpdateResult}"
                            );

                            // 🚀 输出前3个处理结果的示例（用于调试）
                            foreach (var product in localProducts.Take(3))
                            {
                                _logger.LogDebug(
                                    $"   示例商品: {product.ProductCode} (更新时间: {product.UpdatedAt:yyyy-MM-dd HH:mm:ss})"
                                );
                            }

                            // 每处理几批后稍微延迟，避免数据库压力过大
                            if (pageNumber % 10 == 0)
                            {
                                await Task.Delay(500); // 每10批延迟0.5秒
                                _logger.LogInformation(
                                    $"增量同步进度: 已处理 {pageNumber} 批，总计新增/更新 {totalProductAdded + totalProductUpdated} 条Product记录"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"第 {pageNumber} 批商品信息增量同步失败");
                            totalErrors += hqProductsBatch.Count;

                            // 出现错误后延迟再继续
                            await Task.Delay(1000);
                        }

                        totalProcessed += hqProductsBatch.Count;
                        pageNumber++;
                    }

                    _logger.LogInformation(
                        $"商品字典表增量同步完成 - Product新增: {totalProductAdded}, 更新: {totalProductUpdated}"
                    );

                    // 🚀 5. 使用JOIN连接查询增量同步一品多码表数据到ProductSetCode
                    _logger.LogInformation(
                        "开始使用JOIN连接查询增量同步一品多码表数据到ProductSetCode..."
                    );

                    var totalMultiCodeAdded = 0;
                    var totalMultiCodeUpdated = 0;
                    pageNumber = 1;

                    while (true)
                    {
                        // 🔧 优化策略：使用JOIN连接查询，直接关联商品信息表和一品多码表
                        // 同时添加增量同步的时间条件
                        var hqMultiCodesBatch = await _hqContext
                            .Db.Queryable<
                                BlazorApp.Shared.Models.HqEntities.DIC_一品多码表,
                                BlazorApp.Shared.Models.HqEntities.DIC_商品信息字典表
                            >(
                                (multiCode, product) =>
                                    new JoinQueryInfos(
                                        JoinType.Inner,
                                        multiCode.H商品编码 == product.H商品编码
                                    )
                            )
                            .Where(
                                (multiCode, product) =>
                                    multiCode.FGC_LastModifyDate >= lastUpdateDate
                                    && !string.IsNullOrEmpty(multiCode.H多码商品编号)
                                    && !string.IsNullOrEmpty(multiCode.H商品编码)
                            )
                            .Select((multiCode, product) => multiCode)
                            .Skip((pageNumber - 1) * batchSize)
                            .Take(batchSize)
                            .ToListAsync();

                        if (!hqMultiCodesBatch.Any())
                            break; // 没有更多数据

                        _logger.LogInformation(
                            $"从HQ数据库获取到第 {pageNumber} 批更新的一品多码数据，共 {hqMultiCodesBatch.Count} 条"
                        );

                        try
                        {
                            // 转换为ProductSetCode实体（从一品多码表）
                            var multiCodeSetCodes = hqMultiCodesBatch
                                .Select(hqMultiCode => _mapper.Map<ProductSetCode>(hqMultiCode))
                                .ToList();

                            // 处理ProductSetCode增量同步（基于ProductCode + SetItemNumber进行判断）
                            var multiCodeStorageResult = await _localContext
                                .Db.Storageable(multiCodeSetCodes)
                                .WhereColumns(x => new { x.ProductCode, x.SetItemNumber }) // 基于商品编码和套装货号进行判断
                                .ToStorageAsync();

                            var multiCodeInsertResult =
                                multiCodeStorageResult.AsInsertable.ExecuteCommand();
                            var multiCodeUpdateResult =
                                multiCodeStorageResult.AsUpdateable.ExecuteCommand();

                            totalMultiCodeAdded += multiCodeInsertResult;
                            totalMultiCodeUpdated += multiCodeUpdateResult;

                            _logger.LogInformation(
                                $"第 {pageNumber} 批一品多码增量同步完成 - ProductSetCode新增: {multiCodeInsertResult}, 更新: {multiCodeUpdateResult}"
                            );

                            // 每处理几批后稍微延迟，避免数据库压力过大
                            if (pageNumber % 8 == 0)
                            {
                                await Task.Delay(800); // 每8批延迟0.8秒
                                _logger.LogInformation(
                                    $"一品多码增量同步进度: 已处理 {pageNumber} 批，总计新增/更新 {totalMultiCodeAdded + totalMultiCodeUpdated} 条ProductSetCode记录"
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"第 {pageNumber} 批一品多码数据增量同步失败");
                            totalErrors += hqMultiCodesBatch.Count;

                            // 出现错误后延迟再继续
                            await Task.Delay(1500);
                        }

                        pageNumber++;
                    }

                    _logger.LogInformation(
                        $"一品多码表增量同步完成 - ProductSetCode新增: {totalMultiCodeAdded}, 更新: {totalMultiCodeUpdated}"
                    );

                    // 🎉 提交事务
                    await _localContext.Db.Ado.CommitTranAsync();

                    result.AddedCount = totalProductAdded + totalMultiCodeAdded;
                    result.UpdatedCount = totalProductUpdated + totalMultiCodeUpdated;
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        $"🎉 商品信息增量同步完成！总共处理: {totalProcessed}, Product表新增: {totalProductAdded}, 更新: {totalProductUpdated}; ProductSetCode表新增: {totalMultiCodeAdded}, 更新: {totalMultiCodeUpdated}，错误: {totalErrors}";
                    _logger.LogInformation(result.Message);
                }
                catch (Exception)
                {
                    // 🔙 回滚事务
                    await _localContext.Db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增量同步商品信息数据时发生错误");
                result.Message = $"增量同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 🚀 增量同步商品库存数据从HQ到本地数据库（基于更新时间筛选）
        /// </summary>
        /// <param name="lastUpdateDate">上次更新日期，只同步此日期之后更新的库存</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncProductStocksIncrementalFromHqAsync(
            DateTime lastUpdateDate
        )
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation(
                    $"🚀 开始从HQ数据库增量同步商品库存数据（上次更新时间: {lastUpdateDate:yyyy-MM-dd HH:mm:ss}）..."
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 从HQ数据库获取指定日期后更新的库存信息数据
                const int batchSize = 10000; // 每批处理10000条记录
                var totalProcessed = 0;
                var totalAdded = 0;
                var totalUpdated = 0;
                var totalErrors = 0;
                var pageNumber = 1;

                while (true)
                {
                    // 🚀 使用导航查询，同时获取库存信息和关联的商品信息，筛选指定日期后更新的记录
                    var hqStocksBatch = await _hqContext
                        .CBP_DIC_商品库存表Db.AsQueryable()
                        .Includes(x => x.商品信息) // 使用导航属性获取商品信息
                        .Where(x =>
                            !string.IsNullOrEmpty(x.H商品编码)
                            && x.FGC_LastModifyDate >= lastUpdateDate
                        ) // 只获取指定日期后更新的库存
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqStocksBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批更新的库存信息，共 {hqStocksBatch.Count} 条"
                    );

                    // 🚀 使用AutoMapper进行批量转换
                    var warehouseProducts = _mapper.Map<List<WarehouseProduct>>(hqStocksBatch);

                    // 🚀 使用Storageable处理Insert/Update逻辑（根据商品编码判断）
                    try
                    {
                        var storageResult = await _localContext
                            .Db.Storageable(warehouseProducts)
                            .WhereColumns(x => x.ProductCode) // 基于商品编码进行判断
                            .ToStorageAsync();

                        // 执行插入和更新
                        var insertResult = storageResult.AsInsertable.ExecuteCommand();
                        var updateResult = storageResult.AsUpdateable.ExecuteCommand();

                        totalAdded += insertResult;
                        totalUpdated += updateResult;

                        _logger.LogInformation(
                            $"第 {pageNumber} 批库存信息增量同步完成 - 新增: {insertResult}, 更新: {updateResult}"
                        );

                        // 🚀 输出前3个处理结果的示例（用于调试）
                        foreach (var stock in warehouseProducts.Take(3))
                        {
                            _logger.LogDebug(
                                $"   示例库存: {stock.ProductCode} (库存量: {stock.StockQuantity})"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"第 {pageNumber} 批库存信息增量同步失败");
                        totalErrors += hqStocksBatch.Count;
                    }

                    totalProcessed += hqStocksBatch.Count;
                    pageNumber++;
                }

                result.AddedCount = totalAdded;
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"库存信息增量同步完成！总共处理: {totalProcessed}, 新增: {totalAdded}, 更新: {totalUpdated}, 错误: {totalErrors}";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增量同步商品库存数据时发生错误");
                result.Message = $"库存增量同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 批量翻译所有仓库商品名称
        /// </summary>
        /// <returns>翻译结果</returns>
        public async Task<SyncResult> TranslateAllProductNamesAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
            };

            try
            {
                _logger.LogInformation("开始批量翻译所有仓库商品名称");

                // 获取所有需要翻译的商品（现在从 Product 表获取）
                // 注意：WarehouseProduct 中的 ProductName 和 EnglishProductName 字段已移除
                var productsToTranslate = await _localContext
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductName != null && p.ProductName != "")
                    .Where(p => SqlFunc.IsNull(p.EnglishName, "") == "")
                    .ToListAsync();

                if (!productsToTranslate.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有需要翻译的仓库商品";
                    return result;
                }

                // 过滤包含中文的商品名称
                var chineseProducts = new List<Product>();
                foreach (var product in productsToTranslate)
                {
                    if (_translationService.ContainsChinese(product.ProductName))
                    {
                        chineseProducts.Add(product);
                    }
                }

                if (!chineseProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有包含中文的仓库商品需要翻译";
                    return result;
                }

                _logger.LogInformation("找到 {Count} 个需要翻译的仓库商品", chineseProducts.Count);

                // 提取中文名称进行批量翻译
                var chineseNames = chineseProducts.Select(p => p.ProductName).ToList();
                var translations = await _translationService.BatchTranslateToEnglishAsync(
                    chineseNames
                );

                // 更新商品英文名称（现在更新 Product 表）
                int updatedCount = 0;
                foreach (var product in chineseProducts)
                {
                    if (translations.ContainsKey(product.ProductName))
                    {
                        var englishName = translations[product.ProductName];
                        if (
                            !string.IsNullOrEmpty(englishName)
                            && englishName != product.ProductName
                        )
                        {
                            product.EnglishName = englishName;
                            updatedCount++;
                        }
                    }
                }

                // 批量更新数据库（更新 Product 表）
                if (updatedCount > 0)
                {
                    await _localContext
                        .Db.Updateable(chineseProducts)
                        .UpdateColumns(p => new { p.EnglishName })
                        .ExecuteCommandAsync();
                }

                result.UpdatedCount = updatedCount;
                result.IsSuccess = true;
                result.Message = $"批量翻译完成，成功翻译 {updatedCount} 个仓库商品名称";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量翻译仓库商品名称时发生错误");
                result.Message = $"翻译失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 选择性翻译仓库商品名称
        /// </summary>
        /// <param name="mode">翻译模式：untranslated, all_chinese, force_all</param>
        /// <param name="productCodeFilter">商品编码过滤器（可选）</param>
        /// <returns>翻译结果</returns>
        public async Task<SyncResult> TranslateProductNamesAsync(
            string mode,
            string? productCodeFilter = null
        )
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
            };

            try
            {
                _logger.LogInformation(
                    "开始选择性翻译仓库商品名称，模式: {Mode}, 过滤器: {Filter}",
                    mode,
                    productCodeFilter ?? "无"
                );

                // 构建查询条件（修改为从 Product 表查询）
                var query = _localContext
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductName != null && p.ProductName != "");

                // 添加商品编码过滤
                if (!string.IsNullOrWhiteSpace(productCodeFilter))
                {
                    query = query.Where(p =>
                        p.ProductCode != null && p.ProductCode.Contains(productCodeFilter)
                    );
                }

                // 根据翻译模式添加条件
                switch (mode.ToLower())
                {
                    case "untranslated":
                        // 只翻译未翻译的商品
                        query = query.Where(p => SqlFunc.IsNull(p.EnglishName, "") == "");
                        break;
                    case "all_chinese":
                        // 翻译所有包含中文的商品（无论是否已有英文名称）
                        // 在后面过滤中文
                        break;
                    case "force_all":
                        // 强制重新翻译所有商品
                        break;
                    default:
                        result.Message = $"无效的翻译模式: {mode}";
                        return result;
                }

                var productsToCheck = await query.ToListAsync();

                if (!productsToCheck.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有符合条件的仓库商品需要翻译";
                    return result;
                }

                // 根据模式过滤需要翻译的商品
                var productsToTranslate = new List<Product>();
                foreach (var product in productsToCheck)
                {
                    bool shouldTranslate = false;

                    switch (mode.ToLower())
                    {
                        case "untranslated":
                            shouldTranslate = _translationService.ContainsChinese(
                                product.ProductName
                            );
                            break;
                        case "all_chinese":
                            shouldTranslate = _translationService.ContainsChinese(
                                product.ProductName
                            );
                            break;
                        case "force_all":
                            shouldTranslate = true;
                            break;
                    }

                    if (shouldTranslate)
                    {
                        productsToTranslate.Add(product);
                    }
                }

                if (!productsToTranslate.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "没有需要翻译的仓库商品";
                    return result;
                }

                _logger.LogInformation(
                    "找到 {Count} 个需要翻译的仓库商品",
                    productsToTranslate.Count
                );

                // 提取商品名称进行批量翻译
                var namesToTranslate = productsToTranslate.Select(p => p.ProductName).ToList();
                var translations = await _translationService.BatchTranslateToEnglishAsync(
                    namesToTranslate
                );

                // 更新仓库商品英文名称
                int updatedCount = 0;
                foreach (var product in productsToTranslate)
                {
                    if (translations.ContainsKey(product.ProductName))
                    {
                        var englishName = translations[product.ProductName];
                        if (!string.IsNullOrEmpty(englishName))
                        {
                            product.EnglishName = englishName;
                            updatedCount++;
                        }
                    }
                }

                // 批量更新数据库（更新 Product 表）
                if (updatedCount > 0)
                {
                    await _localContext
                        .Db.Updateable(productsToTranslate)
                        .UpdateColumns(p => new { p.EnglishName })
                        .ExecuteCommandAsync();
                }

                result.UpdatedCount = updatedCount;
                result.IsSuccess = true;
                result.Message = $"选择性翻译完成，成功翻译 {updatedCount} 个仓库商品名称";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "选择性翻译仓库商品名称时发生错误");
                result.Message = $"翻译失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 从HQ同步国内商品数据（新版本：先从HBSales获取，再从HQ获取，根据商品编码更新）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncDomesticProductsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "开始同步国内商品数据",
            };

            try
            {
                _logger.LogInformation("开始从HQ同步国内商品数据（新版本：增量更新模式）");

                // 第1步：获取旧版DIC_商品信息字典表数据，建立货号到商品编码的映射
                var dicProducts = await _hqContext
                    .DIC_商品信息字典表Db.AsQueryable()
                    .Where(p =>
                        !string.IsNullOrEmpty(p.H货号) && !string.IsNullOrEmpty(p.H商品编码)
                    )
                    .ToListAsync();
                _logger.LogInformation("从HQ获取到旧版DIC表 {Count} 条商品数据", dicProducts.Count);

                // 建立货号到商品编码的映射表（用于处理货号相同但商品编码不同的情况）
                var itemNumberToProductCodeMap = dicProducts
                    .GroupBy(p => p.H货号)
                    .ToDictionary(
                        g => g.Key,
                        g => g.First().H商品编码 // 如果同一货号有多条记录，取第一条
                    );
                _logger.LogInformation(
                    "建立货号到商品编码映射表，共 {Count} 个货号",
                    itemNumberToProductCodeMap.Count
                );

                // 第2步：先获取HBSales数据库的商品数据（CPT_DIC_商品信息字典表）
                var hbSalesProducts = await _hbSalesContext
                    .CPT_DIC_商品信息字典表Db.AsQueryable()
                    .Where(p => !string.IsNullOrEmpty(p.商品编码))
                    .ToListAsync();
                _logger.LogInformation("从HBSales获取到 {Count} 条商品数据", hbSalesProducts.Count);

                // 第3步：再获取HQ的商品数据（CPT_DIC_商品信息字典表）
                var hqProducts = await _hqContext
                    .CPT_DIC_商品信息字典表_HQDb.AsQueryable()
                    .Where(p => !string.IsNullOrEmpty(p.商品编码))
                    .ToListAsync();
                _logger.LogInformation("从HQ获取到 {Count} 条商品数据", hqProducts.Count);

                if (!hbSalesProducts.Any() && !hqProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "HBSales和HQ中都没有商品数据需要同步";
                    return result;
                }

                // 第4步：合并数据，HQ数据优先级更高，但商品编码以DIC表为准
                var mergedProducts = new Dictionary<string, CPT_DIC_商品信息字典表>();
                int productCodeCorrectionCount = 0; // 统计商品编码修正次数

                // 先添加HBSales数据
                foreach (var product in hbSalesProducts)
                {
                    if (!string.IsNullOrEmpty(product.商品编码))
                    {
                        // 🔥 检查是否需要根据货号修正商品编码
                        var correctProductCode = product.商品编码;
                        if (
                            !string.IsNullOrEmpty(product.HB货号)
                            && itemNumberToProductCodeMap.TryGetValue(
                                product.HB货号,
                                out var dicProductCode
                            )
                        )
                        {
                            if (dicProductCode != product.商品编码)
                            {
                                _logger.LogInformation(
                                    "商品编码修正: 货号 {ItemNumber} 的商品编码从 {OldCode} 修正为 {NewCode}（DIC表）",
                                    product.HB货号,
                                    product.商品编码,
                                    dicProductCode
                                );
                                correctProductCode = dicProductCode;
                                product.商品编码 = dicProductCode; // 修正商品编码
                                productCodeCorrectionCount++;
                            }
                        }

                        mergedProducts[correctProductCode] = product;
                    }
                }

                // 再用HQ数据覆盖（HQ数据优先级更高），但保留HBSales的装箱数和体积数据
                foreach (var product in hqProducts)
                {
                    if (!string.IsNullOrEmpty(product.商品编码))
                    {
                        // 🔥 检查是否需要根据货号修正商品编码
                        var correctProductCode = product.商品编码;
                        if (
                            !string.IsNullOrEmpty(product.HB货号)
                            && itemNumberToProductCodeMap.TryGetValue(
                                product.HB货号,
                                out var dicProductCode
                            )
                        )
                        {
                            if (dicProductCode != product.商品编码)
                            {
                                _logger.LogInformation(
                                    "商品编码修正: 货号 {ItemNumber} 的商品编码从 {OldCode} 修正为 {NewCode}（DIC表）",
                                    product.HB货号,
                                    product.商品编码,
                                    dicProductCode
                                );
                                correctProductCode = dicProductCode;
                                product.商品编码 = dicProductCode; // 修正商品编码
                                productCodeCorrectionCount++;
                            }
                        }

                        // 如果已存在HBSales数据，保留其装箱数和体积字段
                        if (mergedProducts.ContainsKey(correctProductCode))
                        {
                            var existingProduct = mergedProducts[correctProductCode];
                            var preservedPackingQuantity = existingProduct.单件装箱数;
                            var preservedUnitVolume = existingProduct.单件体积;

                            // 用HQ数据覆盖
                            mergedProducts[correctProductCode] = product;

                            // 恢复HBSales的装箱数和体积数据（如果有值的话）
                            if (
                                preservedPackingQuantity.HasValue
                                && preservedPackingQuantity.Value > 0
                            )
                            {
                                mergedProducts[correctProductCode].单件装箱数 =
                                    preservedPackingQuantity;
                                _logger.LogDebug(
                                    "商品 {ProductCode} 保留HBSales装箱数: {PackingQuantity}",
                                    correctProductCode,
                                    preservedPackingQuantity
                                );
                            }
                            if (preservedUnitVolume.HasValue && preservedUnitVolume.Value > 0)
                            {
                                mergedProducts[correctProductCode].单件体积 = preservedUnitVolume;
                                _logger.LogDebug(
                                    "商品 {ProductCode} 保留HBSales体积: {UnitVolume}",
                                    correctProductCode,
                                    preservedUnitVolume
                                );
                            }
                        }
                        else
                        {
                            // 如果不存在HBSales数据，直接使用HQ数据
                            mergedProducts[correctProductCode] = product;
                        }
                    }
                }

                if (productCodeCorrectionCount > 0)
                {
                    _logger.LogInformation(
                        "✅ 根据DIC表货号映射，成功修正 {Count} 个商品的商品编码",
                        productCodeCorrectionCount
                    );
                }

                _logger.LogInformation(
                    "合并后共有 {Count} 条唯一商品数据，已保留HBSales的装箱数和体积数据",
                    mergedProducts.Count
                );

                // 第5步：使用AutoMapper批量转换数据
                var sourceProducts = mergedProducts.Values.ToList();
                var localProducts = new List<DomesticProduct>();
                var errorCount = 0;

                try
                {
                    // 批量映射转换（包含商品图片的智能处理）
                    localProducts = _mapper.Map<List<DomesticProduct>>(sourceProducts);
                    _logger.LogInformation(
                        "AutoMapper批量转换完成，共转换 {Count} 个商品（包含图片URL智能处理）",
                        localProducts.Count
                    );

                    // 修复可能存在的重复URL（从源数据库带来的）
                    int fixedUrlCount = 0;
                    foreach (var product in localProducts)
                    {
                        if (!string.IsNullOrWhiteSpace(product.ProductImage))
                        {
                            var originalUrl = product.ProductImage;
                            var fixedUrl = BlazorApp.Api.Utils.ImageUrlHelper.FixDuplicateUrl(
                                originalUrl
                            );

                            if (!string.IsNullOrWhiteSpace(fixedUrl) && fixedUrl != originalUrl)
                            {
                                product.ProductImage = fixedUrl;
                                fixedUrlCount++;
                                _logger.LogDebug(
                                    "修复重复URL: {ProductCode} - {Original} => {Fixed}",
                                    product.ProductCode,
                                    originalUrl,
                                    fixedUrl
                                );
                            }
                        }
                    }

                    if (fixedUrlCount > 0)
                    {
                        _logger.LogInformation(
                            "数据同步时自动修复了 {Count} 个重复的图片URL",
                            fixedUrlCount
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoMapper批量转换失败");
                    errorCount = sourceProducts.Count;
                    localProducts = new List<DomesticProduct>();
                }

                if (localProducts.Any())
                {
                    // 第6步：获取现有的本地商品数据
                    var existingProducts = await _localContext
                        .DomesticProductDb.AsQueryable()
                        .Where(p => !string.IsNullOrEmpty(p.ProductCode))
                        .ToListAsync();

                    var existingProductDict = existingProducts.ToDictionary(
                        p => p.ProductCode!,
                        p => p
                    );

                    var toInsert = new List<DomesticProduct>();
                    var toUpdate = new List<DomesticProduct>();

                    // 第7步：根据商品编码分类需要插入和更新的数据
                    foreach (var localProduct in localProducts)
                    {
                        if (!string.IsNullOrEmpty(localProduct.ProductCode))
                        {
                            if (
                                existingProductDict.TryGetValue(
                                    localProduct.ProductCode,
                                    out var existingProduct
                                )
                            )
                            {
                                toUpdate.Add(localProduct);
                            }
                            else
                            {
                                toInsert.Add(localProduct);
                            }
                        }
                    }

                    // 第8步：执行数据库操作
                    await _localContext.Db.Ado.BeginTranAsync();
                    try
                    {
                        int insertedCount = 0;
                        int updatedCount = 0;

                        // 使用大数据方法批量插入新数据
                        if (toInsert.Any())
                        {
                            insertedCount = _localContext
                                .Db.Fastest<DomesticProduct>()
                                .BulkCopy(toInsert);
                            _logger.LogInformation(
                                "大数据批量插入 {Count} 条新商品数据",
                                insertedCount
                            );
                        }

                        // 使用大数据方法批量更新现有数据
                        if (toUpdate.Any())
                        {
                            updatedCount = _localContext
                                .Db.Fastest<DomesticProduct>()
                                .BulkUpdate(toUpdate);
                            _logger.LogInformation(
                                "大数据批量更新 {Count} 条商品数据",
                                updatedCount
                            );
                        }

                        await _localContext.Db.Ado.CommitTranAsync();

                        result.AddedCount = insertedCount;
                        result.UpdatedCount = updatedCount;
                        result.ErrorCount = errorCount;
                        result.IsSuccess = true;
                        result.Message =
                            $"国内商品数据同步成功，新增 {insertedCount} 个商品，更新 {updatedCount} 个商品，{errorCount} 个错误";
                        _logger.LogInformation(
                            "国内商品数据同步完成：新增 {AddedCount} 个商品，更新 {UpdatedCount} 个商品，{ErrorCount} 个错误",
                            insertedCount,
                            updatedCount,
                            errorCount
                        );
                    }
                    catch (Exception)
                    {
                        await _localContext.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "没有有效的商品数据可以同步";
                    result.ErrorCount = errorCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步国内商品数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 同步货号前缀数据从HQ到本地数据库（使用批量操作优化性能）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncProductPrefixCodesFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "开始同步货号前缀数据",
            };

            try
            {
                _logger.LogInformation("开始从HQ同步货号前缀数据（使用批量操作）");

                // 获取HBSales的货号前缀数据
                var hqPrefixCodes = await _hbSalesContext.CPT_DIC_货号前缀信息表Db.GetListAsync();
                _logger.LogInformation("从HQ获取到 {Count} 条货号前缀数据", hqPrefixCodes.Count);

                if (!hqPrefixCodes.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "HQ中没有货号前缀数据需要同步";
                    return result;
                }

                // 批量映射数据
                var localPrefixes = new List<ProductPrefixCode>();
                var errorCount = 0;

                foreach (var hqPrefix in hqPrefixCodes)
                {
                    try
                    {
                        var localPrefix = new ProductPrefixCode
                        {
                            PrefixCode = UuidHelper.GenerateUuid7(),
                            SupplierCode = hqPrefix.供应商编码,
                            PrefixName = hqPrefix.HB货号前缀码,
                            PrefixDescription = hqPrefix.前缀描述,
                            IsActive = true,
                            SortOrder = hqPrefix.ID,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                        };

                        localPrefixes.Add(localPrefix);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "映射货号前缀 {PrefixCode} 时发生错误",
                            hqPrefix.HB货号前缀码
                        );
                        errorCount++;
                    }
                }

                if (localPrefixes.Any())
                {
                    // 开启事务进行批量操作
                    await _localContext.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 批量清空本地货号前缀表
                        await _localContext
                            .Db.Deleteable<ProductPrefixCode>()
                            .ExecuteCommandAsync();
                        _logger.LogInformation("已清空本地货号前缀表");

                        // 批量插入新数据
                        var insertedCount = await _localContext
                            .Db.Insertable(localPrefixes)
                            .PageSize(1000) // 分页批量插入
                            .ExecuteCommandAsync();

                        await _localContext.Db.Ado.CommitTranAsync();

                        result.AddedCount = insertedCount;
                        result.ErrorCount = errorCount;
                        result.IsSuccess = true;
                        result.Message =
                            $"货号前缀数据同步成功，批量新增 {insertedCount} 个前缀，{errorCount} 个错误";
                        _logger.LogInformation(
                            "货号前缀数据批量同步完成：新增 {AddedCount} 个前缀，{ErrorCount} 个错误",
                            insertedCount,
                            errorCount
                        );
                    }
                    catch (Exception)
                    {
                        await _localContext.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "没有有效的货号前缀数据可以同步";
                    result.ErrorCount = errorCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步货号前缀数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 同步套装商品数据从HQ到本地数据库（使用批量操作优化性能）
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncDomesticSetProductsFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "开始同步套装商品数据",
            };

            try
            {
                _logger.LogInformation("开始从HQ同步套装商品数据（使用批量操作）");

                // 获取HBSales的套装商品数据
                var hqSetProducts = await _hbSalesContext.CPT_DIC_商品套装信息表Db.GetListAsync();
                _logger.LogInformation("从HQ获取到 {Count} 条套装商品数据", hqSetProducts.Count);

                if (!hqSetProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message = "HQ中没有套装商品数据需要同步";
                    return result;
                }

                // 批量映射数据
                var localSetProducts = new List<DomesticSetProduct>();
                var errorCount = 0;

                foreach (var hqSetProduct in hqSetProducts)
                {
                    try
                    {
                        var localSetProduct = new DomesticSetProduct
                        {
                            SetProductCode = UuidHelper.GenerateUuid7(),
                            ProductCode = hqSetProduct.商品编码 ?? string.Empty,
                            ProductNo = hqSetProduct.商品小货号,
                            SetProductNo = hqSetProduct.商品小货号 ?? string.Empty,
                            SetBarcode = hqSetProduct.条形码,
                            DomesticPrice = hqSetProduct.国内价格,
                            ImportPrice = hqSetProduct.进口价格,
                            OEMPrice = hqSetProduct.贴牌价格,
                            Remarks = hqSetProduct.备注,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                        };

                        localSetProducts.Add(localSetProduct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "映射套装商品 {ProductCode} 时发生错误",
                            hqSetProduct.商品编码
                        );
                        errorCount++;
                    }
                }

                if (localSetProducts.Any())
                {
                    // 开启事务进行批量操作
                    await _localContext.Db.Ado.BeginTranAsync();
                    try
                    {
                        // 批量清空本地套装商品表
                        await _localContext
                            .Db.Deleteable<DomesticSetProduct>()
                            .ExecuteCommandAsync();
                        _logger.LogInformation("已清空本地套装商品表");

                        // 批量插入新数据
                        var insertedCount = await _localContext
                            .Db.Insertable(localSetProducts)
                            .PageSize(1000) // 分页批量插入
                            .ExecuteCommandAsync();

                        await _localContext.Db.Ado.CommitTranAsync();

                        result.AddedCount = insertedCount;
                        result.ErrorCount = errorCount;
                        result.IsSuccess = true;
                        result.Message =
                            $"套装商品数据同步成功，批量新增 {insertedCount} 个套装商品，{errorCount} 个错误";
                        _logger.LogInformation(
                            "套装商品数据批量同步完成：新增 {AddedCount} 个套装商品，{ErrorCount} 个错误",
                            insertedCount,
                            errorCount
                        );
                    }
                    catch (Exception)
                    {
                        await _localContext.Db.Ado.RollbackTranAsync();
                        throw;
                    }
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "没有有效的套装商品数据可以同步";
                    result.ErrorCount = errorCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步套装商品数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 从HQ数据库同步货柜主表和明细表数据到本地数据库（全量同步）
        /// 🚢 完整同步所有货柜信息，包括主表和明细表
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncContainersFromHqAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation("开始从HQ数据库全量同步货柜数据...");

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 清空本地货柜数据（全量同步）
                _logger.LogInformation("清空本地货柜数据...");
                await _localContext.Db.Deleteable<ContainerDetail>().ExecuteCommandAsync();
                await _localContext.Db.Deleteable<Container>().ExecuteCommandAsync();

                // 3. 同步货柜主表数据
                const int batchSize = 5000; // 每批处理5000条记录
                var totalContainers = 0;
                var totalDetails = 0;
                var pageNumber = 1;

                _logger.LogInformation("开始同步货柜主表数据...");

                while (true)
                {
                    var hqContainersBatch = await _hqContext
                        .CPT_RED_货柜单主表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqContainersBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批货柜主表数据，共 {hqContainersBatch.Count} 条"
                    );

                    // 使用AutoMapper批量转换HQ货柜主表数据到本地Container实体
                    List<Container> localContainers;
                    try
                    {
                        localContainers = _mapper.Map<List<Container>>(hqContainersBatch);
                        _logger.LogDebug(
                            $"AutoMapper成功转换 {localContainers.Count} 条货柜主表数据"
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            $"AutoMapper批量转换货柜主表数据失败: {ex.Message}，回退到逐条处理"
                        );

                        // 回退到逐条处理，以便记录具体的错误
                        localContainers = new List<Container>();
                        foreach (var hqContainer in hqContainersBatch)
                        {
                            try
                            {
                                var localContainer = _mapper.Map<Container>(hqContainer);
                                localContainers.Add(localContainer);
                            }
                            catch (Exception containerEx)
                            {
                                _logger.LogWarning(
                                    $"转换货柜主表数据失败，跳过记录 {hqContainer.HGUID}: {containerEx.Message}"
                                );
                                result.ErrorCount++;
                            }
                        }
                    }

                    // 批量插入货柜主表数据
                    if (localContainers.Any())
                    {
                        await _localContext.Db.Insertable(localContainers).ExecuteCommandAsync();
                        totalContainers += localContainers.Count;
                        result.AddedCount += localContainers.Count;
                    }

                    pageNumber++;
                }

                _logger.LogInformation($"货柜主表同步完成，共同步 {totalContainers} 条记录");

                // 4. 同步货柜明细表数据
                _logger.LogInformation("开始同步货柜明细表数据...");
                pageNumber = 1;

                while (true)
                {
                    var hqDetailsBatch = await _hqContext
                        .CPT_RED_货柜单详情表Db.AsQueryable()
                        .Skip((pageNumber - 1) * batchSize * 10)
                        .Take(batchSize * 10)
                        .ToListAsync();

                    if (!hqDetailsBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批货柜明细数据，共 {hqDetailsBatch.Count} 条"
                    );

                    // 使用AutoMapper批量转换HQ货柜明细数据到本地ContainerDetail实体
                    List<ContainerDetail> localDetails;
                    try
                    {
                        localDetails = _mapper.Map<List<ContainerDetail>>(hqDetailsBatch);
                        _logger.LogDebug($"AutoMapper成功转换 {localDetails.Count} 条货柜明细数据");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            $"AutoMapper批量转换货柜明细数据失败: {ex.Message}，回退到逐条处理"
                        );

                        // 回退到逐条处理，以便记录具体的错误
                        localDetails = new List<ContainerDetail>();
                        foreach (var hqDetail in hqDetailsBatch)
                        {
                            try
                            {
                                var localDetail = _mapper.Map<ContainerDetail>(hqDetail);
                                localDetails.Add(localDetail);
                            }
                            catch (Exception detailEx)
                            {
                                _logger.LogWarning(
                                    $"转换货柜明细数据失败，跳过记录 {hqDetail.HGUID}: {detailEx.Message}"
                                );
                                result.ErrorCount++;
                            }
                        }
                    }

                    // 批量插入货柜明细数据
                    if (localDetails.Any())
                    {
                        await _localContext.Db.Insertable(localDetails).ExecuteCommandAsync();
                        totalDetails += localDetails.Count;
                    }

                    pageNumber++;
                }

                _logger.LogInformation($"货柜明细同步完成，共同步 {totalDetails} 条记录");

                // 5. 设置同步结果
                result.IsSuccess = true;
                result.Message =
                    $"货柜数据全量同步成功，主表: {totalContainers} 条，明细: {totalDetails} 条";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步货柜数据时发生错误");
                result.Message = $"同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 从HQ数据库增量同步货柜数据到本地数据库
        /// 🔄 基于指定日期，只同步此日期之后更新的货柜信息
        /// </summary>
        /// <param name="lastUpdateDate">上次更新日期</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncContainersIncrementalFromHqAsync(DateTime lastUpdateDate)
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "",
                AddedCount = 0,
                UpdatedCount = 0,
                ErrorCount = 0,
            };

            try
            {
                _logger.LogInformation(
                    $"开始从HQ数据库增量同步货柜数据，上次更新时间: {lastUpdateDate:yyyy-MM-dd HH:mm:ss}"
                );

                // 1. 检查HQ数据库连接
                _hqContext.CheckConnection();

                // 2. 增量同步货柜主表数据
                const int batchSize = 1000;
                var totalContainers = 0;
                var totalDetails = 0;
                var pageNumber = 1;

                _logger.LogInformation("开始增量同步货柜主表数据...");

                // 转换日期格式用于HQ数据库查询
                var lastUpdateDateStr = lastUpdateDate.ToString("yyyy-MM-dd HH:mm:ss");

                while (true)
                {
                    // 查询HQ数据库中更新时间大于指定日期的货柜主表数据
                    var hqContainersBatch = await _hqContext
                        .CPT_RED_货柜单主表Db.AsQueryable()
                        .Where(c =>
                            c.FGC_LastModifyDate != null && c.FGC_LastModifyDate > lastUpdateDate
                        )
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqContainersBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批增量货柜主表数据，共 {hqContainersBatch.Count} 条"
                    );

                    // 获取现有的本地货柜数据
                    var hqGuids = hqContainersBatch
                        .Select(c => c.HGUID)
                        .Where(g => !string.IsNullOrEmpty(g))
                        .ToList();
                    var existingContainers = await _localContext
                        .Db.Queryable<Container>()
                        .Where(c => hqGuids.Contains(c.ContainerCode))
                        .ToListAsync();

                    var containersToUpdate = new List<Container>();
                    var containersToAdd = new List<Container>();

                    foreach (var hqContainer in hqContainersBatch)
                    {
                        try
                        {
                            var containerCode = hqContainer.HGUID ?? UuidHelper.GenerateUuid7();
                            var existingContainer = existingContainers.FirstOrDefault(c =>
                                c.ContainerCode == containerCode
                            );

                            // 使用AutoMapper转换HQ数据到本地实体
                            var localContainer = _mapper.Map<Container>(hqContainer);

                            if (existingContainer != null)
                            {
                                // 更新现有记录，保留原创建信息
                                localContainer.CreatedAt = existingContainer.CreatedAt;
                                localContainer.CreatedBy = existingContainer.CreatedBy;
                                containersToUpdate.Add(localContainer);
                            }
                            else
                            {
                                // 新增记录，AutoMapper已经处理了创建信息
                                containersToAdd.Add(localContainer);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                $"处理增量货柜主表数据失败，跳过记录 {hqContainer.HGUID}: {ex.Message}"
                            );
                            result.ErrorCount++;
                        }
                    }

                    // 批量更新和新增
                    if (containersToUpdate.Any())
                    {
                        await _localContext.Db.Updateable(containersToUpdate).ExecuteCommandAsync();
                        result.UpdatedCount += containersToUpdate.Count;
                        totalContainers += containersToUpdate.Count;
                    }

                    if (containersToAdd.Any())
                    {
                        await _localContext.Db.Insertable(containersToAdd).ExecuteCommandAsync();
                        result.AddedCount += containersToAdd.Count;
                        totalContainers += containersToAdd.Count;
                    }

                    pageNumber++;
                }

                _logger.LogInformation($"货柜主表增量同步完成，共处理 {totalContainers} 条记录");

                // 3. 增量同步货柜明细表数据
                _logger.LogInformation("开始增量同步货柜明细表数据...");
                pageNumber = 1;

                while (true)
                {
                    // 查询HQ数据库中更新时间大于指定日期的货柜明细数据
                    var hqDetailsBatch = await _hqContext
                        .CPT_RED_货柜单详情表Db.AsQueryable()
                        .Where(d =>
                            d.FGC_LastModifyDate != null && d.FGC_LastModifyDate > lastUpdateDate
                        )
                        .Skip((pageNumber - 1) * batchSize)
                        .Take(batchSize)
                        .ToListAsync();

                    if (!hqDetailsBatch.Any())
                        break; // 没有更多数据

                    _logger.LogInformation(
                        $"从HQ数据库获取到第 {pageNumber} 批增量货柜明细数据，共 {hqDetailsBatch.Count} 条"
                    );

                    // 获取现有的本地货柜明细数据
                    var hqDetailGuids = hqDetailsBatch
                        .Select(d => d.HGUID)
                        .Where(g => !string.IsNullOrEmpty(g))
                        .ToList();
                    var existingDetails = await _localContext
                        .Db.Queryable<ContainerDetail>()
                        .Where(d => hqDetailGuids.Contains(d.DetailCode))
                        .ToListAsync();

                    var detailsToUpdate = new List<ContainerDetail>();
                    var detailsToAdd = new List<ContainerDetail>();

                    foreach (var hqDetail in hqDetailsBatch)
                    {
                        try
                        {
                            var detailCode = hqDetail.HGUID ?? UuidHelper.GenerateUuid7();
                            var existingDetail = existingDetails.FirstOrDefault(d =>
                                d.DetailCode == detailCode
                            );

                            // 使用AutoMapper转换HQ数据到本地实体
                            var localDetail = _mapper.Map<ContainerDetail>(hqDetail);

                            if (existingDetail != null)
                            {
                                // 更新现有记录，保留原创建信息
                                localDetail.CreatedAt = existingDetail.CreatedAt;
                                localDetail.CreatedBy = existingDetail.CreatedBy;
                                detailsToUpdate.Add(localDetail);
                            }
                            else
                            {
                                // 新增记录，AutoMapper已经处理了创建信息
                                detailsToAdd.Add(localDetail);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(
                                $"处理增量货柜明细数据失败，跳过记录 {hqDetail.HGUID}: {ex.Message}"
                            );
                            result.ErrorCount++;
                        }
                    }

                    // 批量更新和新增明细数据
                    if (detailsToUpdate.Any())
                    {
                        await _localContext.Db.Updateable(detailsToUpdate).ExecuteCommandAsync();
                        totalDetails += detailsToUpdate.Count;
                    }

                    if (detailsToAdd.Any())
                    {
                        await _localContext.Db.Insertable(detailsToAdd).ExecuteCommandAsync();
                        totalDetails += detailsToAdd.Count;
                    }

                    pageNumber++;
                }

                _logger.LogInformation($"货柜明细增量同步完成，共处理 {totalDetails} 条记录");

                // 4. 设置同步结果
                result.IsSuccess = true;
                result.Message =
                    $"货柜数据增量同步成功，主表: {totalContainers} 条，明细: {totalDetails} 条";
                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增量同步货柜数据时发生错误");
                result.Message = $"增量同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }

        /// <summary>
        /// 从HQ同步分店零售价数据
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreRetailPricesFromHqAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                _logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店零售价数据{(selectedStoreCodes?.Any() == true ? $"，指定分店: {string.Join(", ", selectedStoreCodes)}" : "，全部分店")}"
                );

                // 🚀 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                var query = _hqContext
                    .Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                        (price, product) =>
                            new JoinQueryInfos(JoinType.Inner, price.H商品编码 == product.H商品编码)
                    )
                    .Where(
                        (price, product) =>
                            !string.IsNullOrEmpty(price.H商品编码)
                            && !string.IsNullOrEmpty(price.H分店代码)
                            && price.H使用状态 == true
                            && product.H使用状态 == true
                    );

                // 如果指定了分店代码，添加分店代码过滤条件
                if (selectedStoreCodes?.Any() == true)
                {
                    query = query.Where(
                        (price, product) => selectedStoreCodes.Contains(price.H分店代码)
                    );
                }

                var hqRetailPrices = await query.Select((price, product) => price).ToListAsync();

                _logger.LogInformation(
                    $"📊 从HQ获取到 {hqRetailPrices.Count:N0} 条有效的分店零售价记录（已过滤无效商品和分店）"
                );

                if (!hqRetailPrices.Any())
                {
                    result.Message = "✅ HQ数据库中没有分店零售价数据，同步完成";
                    result.IsSuccess = true;
                    return result;
                }

                // 开始数据库事务
                var db = _localContext.Db;
                await db.Ado.BeginTranAsync();

                try
                {
                    // 根据是否指定分店来决定删除策略
                    if (selectedStoreCodes?.Any() == true)
                    {
                        _logger.LogInformation(
                            $"🗑️ 正在清空指定分店的零售价数据: {string.Join(", ", selectedStoreCodes)}"
                        );
                        await db.Deleteable<StoreRetailPrice>()
                            .Where(x =>
                                x.StoreCode != null && selectedStoreCodes.Contains(x.StoreCode)
                            )
                            .ExecuteCommandAsync();
                        _logger.LogInformation("✅ 指定分店的零售价数据已清空");
                    }
                    else
                    {
                        _logger.LogInformation("🗑️ 正在清空本地分店零售价表...");
                        await db.Deleteable<StoreRetailPrice>().ExecuteCommandAsync();
                        _logger.LogInformation("✅ 本地分店零售价表已清空");
                    }

                    int totalProcessed = 0;
                    int totalAdded = 0;
                    int totalErrors = 0;
                    const int batchSize = 50000;

                    // 转换数据 - 使用AutoMapper
                    _logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localRetailPrices = _mapper.Map<List<StoreRetailPrice>>(hqRetailPrices);
                    _logger.LogInformation(
                        $"✅ 数据转换完成，共 {localRetailPrices.Count:N0} 条记录"
                    );

                    // 使用PageSize优化的批量插入
                    _logger.LogInformation(
                        $"📦 开始批量插入 {localRetailPrices.Count:N0} 条记录..."
                    );
                    try
                    {
                        await db.Fastest<StoreRetailPrice>()
                            .PageSize(batchSize) // 让SqlSugar内部处理分批
                            .BulkCopyAsync(localRetailPrices);

                        totalAdded = localRetailPrices.Count;
                        totalProcessed = localRetailPrices.Count;

                        _logger.LogInformation($"✅ 批量插入完成，成功插入 {totalAdded:N0} 条记录");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ 批量插入失败，错误: {ex.Message}");
                        totalErrors = localRetailPrices.Count;
                    }

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        totalErrors == 0
                            ? $"🎉 分店零售价数据同步成功！共处理 {totalProcessed:N0} 条记录，全部成功插入"
                            : $"⚠️ 分店零售价数据同步部分成功！成功: {totalAdded:N0}, 失败: {totalErrors:N0}";

                    _logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 同步分店零售价数据时发生错误");
                result.Message = $"❌ 同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                _logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }

        /// <summary>
        /// 🚀 按分店并发版本：从HQ同步分店零售价数据
        /// 支持500万条数据的高效同步，按分店代码分别并发执行查询和插入
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <param name="maxConcurrency">最大并发分店数，默认15（降低以减少HQ数据库压力）</param>
        /// <param name="batchSize">每个分店的批次大小，默认50,000条</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreRetailPricesFromHqConcurrentAsync(
            List<string>? selectedStoreCodes = null,
            int maxConcurrency = 15,
            int batchSize = 200000
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var totalProcessed = 0;
            var totalAdded = 0;
            var totalErrors = 0;
            var totalQueryTime = 0.0;
            var totalInsertTime = 0.0;
            var progressLock = new object();

            try
            {
                var initialMemory = GC.GetTotalMemory(false) / 1024 / 1024;
                var processId = Environment.ProcessId;

                _logger.LogInformation(
                    "🚀 开始按分店并发同步 - {StoreInfo}，并发数={MaxConcurrency}，批次={BatchSize:N0}，进程ID={ProcessId}，内存={Memory:N0}MB",
                    selectedStoreCodes?.Any() == true
                        ? $"指定分店({selectedStoreCodes.Count}个)"
                        : "全部分店",
                    maxConcurrency,
                    batchSize,
                    processId,
                    initialMemory
                );

                // 步骤1：获取需要处理的分店代码列表
                List<string> storeCodesToProcess;
                if (selectedStoreCodes?.Any() == true)
                {
                    storeCodesToProcess = selectedStoreCodes;
                }
                else
                {
                    // 从HQ数据库获取所有有数据的分店代码
                    var startQueryTime = DateTime.Now;
                    storeCodesToProcess = await _hqContext
                        .Db.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                            (price, product) =>
                                new JoinQueryInfos(
                                    JoinType.Inner,
                                    price.H商品编码 == product.H商品编码
                                )
                        )
                        .Where(
                            (price, product) =>
                                !string.IsNullOrEmpty(price.H商品编码)
                                && !string.IsNullOrEmpty(price.H分店代码)
                                && price.H使用状态 == true
                                && product.H使用状态 == true
                        )
                        .GroupBy((price, product) => price.H分店代码)
                        .Select((price, product) => price.H分店代码)
                        .ToListAsync();

                    var queryDuration = DateTime.Now - startQueryTime;
                    _logger.LogInformation(
                        "📋 发现分店{StoreCount}个，查询耗时{QueryDuration:F1}秒",
                        storeCodesToProcess.Count,
                        queryDuration.TotalSeconds
                    );
                }

                if (!storeCodesToProcess.Any())
                {
                    result.Message = "✅ 没有找到需要同步的分店，同步完成";
                    result.IsSuccess = true;
                    return result;
                }

                // 步骤2：清空本地数据
                var localDb = _localContext.Db;
                var deleteStartTime = DateTime.Now;
                int deletedCount = await localDb
                    .Deleteable<StoreRetailPrice>()
                    .Where(x => x.StoreCode != null && storeCodesToProcess.Contains(x.StoreCode))
                    .ExecuteCommandAsync();
                var deleteDuration = DateTime.Now - deleteStartTime;
                _logger.LogInformation(
                    "🗑️ 清空本地数据：删除{DeletedCount:N0}条，耗时{DeleteDuration:F1}秒",
                    deletedCount,
                    deleteDuration.TotalSeconds
                );

                // 步骤3：按分店并发处理
                var storeTasks =
                    new List<
                        Task<(
                            int processed,
                            int added,
                            int errors,
                            double queryTime,
                            double insertTime
                        )>
                    >();
                var progressTimer = new System.Timers.Timer(10000); // 每10秒报告进度
                var processStartTime = DateTime.Now;
                var completedStores = 0;
                progressTimer.Elapsed += (sender, e) =>
                {
                    lock (progressLock)
                    {
                        var progress =
                            storeCodesToProcess.Count > 0
                                ? (double)completedStores / storeCodesToProcess.Count * 100
                                : 0;
                        var elapsed = DateTime.Now - processStartTime;
                        var speed = totalProcessed > 0 ? totalProcessed / elapsed.TotalSeconds : 0;
                        var activeTasks = storeTasks.Count(t => !t.IsCompleted);
                        var currentMemory = GC.GetTotalMemory(false) / 1024 / 1024;

                        _logger.LogInformation(
                            "📈 进度：{CompletedStores}/{TotalStores}({Progress:F1}%) 成功{TotalAdded:N0}条 速度{Speed:F0}条/秒 活跃{ActiveTasks}个 内存{Memory:N0}MB",
                            completedStores,
                            storeCodesToProcess.Count,
                            progress,
                            totalAdded,
                            speed,
                            activeTasks,
                            currentMemory
                        );
                    }
                };
                progressTimer.Start();

                try
                {
                    for (int storeIndex = 0; storeIndex < storeCodesToProcess.Count; storeIndex++)
                    {
                        var currentStoreIndex = storeIndex;
                        var storeCode = storeCodesToProcess[currentStoreIndex];

                        // 使用Task.Run确保任务在线程池中并发执行
                        var storeTask = Task.Run(async () =>
                        {
                            return await ProcessSingleStoreAsync(
                                currentStoreIndex,
                                storeCode,
                                batchSize,
                                semaphore
                            );
                        });

                        storeTasks.Add(storeTask);

                        // 如果达到最大并发数，等待一些任务完成
                        if (storeTasks.Count >= maxConcurrency)
                        {
                            var completedTask = await Task.WhenAny(storeTasks);
                            storeTasks.Remove(completedTask);
                            var storeResult = await completedTask; // 获取结果并确保异常被抛出

                            // 更新总体进度
                            lock (progressLock)
                            {
                                totalProcessed += storeResult.processed;
                                totalAdded += storeResult.added;
                                totalErrors += storeResult.errors;
                                totalQueryTime += storeResult.queryTime;
                                totalInsertTime += storeResult.insertTime;
                                completedStores++;
                            }
                        }
                    }

                    // 等待所有剩余任务完成并收集结果
                    var allResults = await Task.WhenAll(storeTasks);

                    // 汇总所有分店的结果
                    foreach (var storeResult in allResults)
                    {
                        lock (progressLock)
                        {
                            totalProcessed += storeResult.processed;
                            totalAdded += storeResult.added;
                            totalErrors += storeResult.errors;
                            totalQueryTime += storeResult.queryTime;
                            totalInsertTime += storeResult.insertTime;
                            completedStores++;
                        }
                    }
                }
                finally
                {
                    progressTimer.Stop();
                    progressTimer.Dispose();
                }

                result.AddedCount = totalAdded;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;

                // 最终统计信息
                var finalDuration = DateTime.Now - result.StartTime;
                var finalSpeed =
                    totalProcessed > 0 ? totalProcessed / finalDuration.TotalSeconds : 0;
                var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024;
                var errorRate = totalProcessed > 0 ? (double)totalErrors / totalProcessed * 100 : 0;

                result.Message =
                    totalErrors == 0
                        ? $"🎉 按分店并发同步成功！共处理 {storeCodesToProcess.Count} 个分店，{totalProcessed:N0} 条记录，全部成功插入"
                        : $"⚠️ 按分店并发同步部分成功！分店: {storeCodesToProcess.Count}，成功: {totalAdded:N0}, 失败: {totalErrors:N0}";

                _logger.LogInformation(
                    "✅ 同步完成：分店{StoreCount}个 记录{TotalProcessed:N0}条 耗时{Duration:F1}秒 速度{Speed:F0}条/秒 错误率{ErrorRate:F2}% 进程{ProcessId} 内存{Memory:N0}MB",
                    storeCodesToProcess.Count,
                    totalProcessed,
                    finalDuration.TotalSeconds,
                    finalSpeed,
                    errorRate,
                    processId,
                    finalMemory
                );
                _logger.LogInformation(
                    "⏱️ 性能统计：查询{QueryTime:F1}秒({QuerySpeed:F0}条/秒) 插入{InsertTime:F1}秒({InsertSpeed:F0}条/秒)",
                    totalQueryTime,
                    totalProcessed / Math.Max(totalQueryTime, 0.001),
                    totalInsertTime,
                    totalAdded / Math.Max(totalInsertTime, 0.001)
                );

                // 主动触发垃圾回收，清理大量临时对象
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var afterGCMemory = GC.GetTotalMemory(false) / 1024 / 1024;
                _logger.LogInformation(
                    "🧹 内存清理：{BeforeMemory:N0}MB→{AfterMemory:N0}MB 释放{ReleasedMemory:N0}MB",
                    finalMemory,
                    afterGCMemory,
                    finalMemory - afterGCMemory
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 按分店并发同步零售价数据时发生错误");
                result.Message = $"❌ 同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                semaphore.Dispose();
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                _logger.LogInformation(
                    $"⏱️ 按分店并发同步耗时: {result.Duration.TotalSeconds:F1} 秒，平均速度: {(totalProcessed > 0 ? totalProcessed / result.Duration.TotalSeconds : 0):F0} 条/秒"
                );
            }

            return result;
        }

        /// <summary>
        /// 🏪 处理单个分店的并发同步（查询和插入）
        /// </summary>
        private async Task<(
            int processed,
            int added,
            int errors,
            double queryTime,
            double insertTime
        )> ProcessSingleStoreAsync(
            int storeIndex,
            string storeCode,
            int batchSize,
            SemaphoreSlim semaphore
        )
        {
            await semaphore.WaitAsync();

            ISqlSugarClient? localDb = null;
            ISqlSugarClient? hqDb = null;
            try
            {
                var storeStartTime = DateTime.Now;
                var currentThreadId = Thread.CurrentThread.ManagedThreadId;
                var processId = Environment.ProcessId;
                var initialMemory = GC.GetTotalMemory(false) / 1024 / 1024;

                _logger.LogInformation(
                    "🏪 分店{StoreCode}开始 线程{ThreadId} 进程{ProcessId} 内存{Memory:N0}MB",
                    storeCode,
                    currentThreadId,
                    processId,
                    initialMemory
                );

                // 创建独立的数据库连接（本地和HQ）
                localDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                hqDb = Data.HqSqlSugarContext.CreateConcurrentConnection(_configuration);

                // 步骤1：使用独立的HQ连接查询该分店的数据
                var queryStartTime = DateTime.Now;
                var storeQuery = hqDb.Queryable<DIC_商品零售价表, DIC_商品信息字典表>(
                        (price, product) =>
                            new JoinQueryInfos(JoinType.Inner, price.H商品编码 == product.H商品编码)
                    )
                    .Where(
                        (price, product) =>
                            !string.IsNullOrEmpty(price.H商品编码)
                            && price.H分店代码 == storeCode
                            && price.H使用状态 == true
                            && product.H使用状态 == true
                    );

                var hqStoreData = await storeQuery.Select((price, product) => price).ToListAsync();

                var queryDuration = DateTime.Now - queryStartTime;

                if (!hqStoreData.Any())
                {
                    _logger.LogWarning("⚠️ 分店{StoreCode}无数据", storeCode);
                    return (0, 0, 0, queryDuration.TotalSeconds, 0);
                }

                // 步骤2：转换数据格式
                var mapStartTime = DateTime.Now;
                var localStoreData = _mapper.Map<List<StoreRetailPrice>>(hqStoreData);
                var mapDuration = DateTime.Now - mapStartTime;

                // 步骤3：直接批量插入（不分页）
                var storeAdded = 0;
                var storeErrors = 0;

                var insertStartTime = DateTime.Now;
                try
                {
                    // 使用重试机制的批量插入
                    await RetryBulkInsertAsync(localDb, localStoreData, 3);
                    storeAdded += localStoreData.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "❌ 分店{StoreCode}插入失败: {Message}",
                        storeCode,
                        ex.Message
                    );
                    storeErrors += localStoreData.Count;
                }

                var storeDuration = DateTime.Now - storeStartTime;
                var totalInsertDuration = DateTime.Now - insertStartTime;
                var processedCount = hqStoreData.Count;
                var finalMemory = GC.GetTotalMemory(false) / 1024 / 1024;

                _logger.LogInformation(
                    "✅ 分店{StoreCode}完成 记录{ProcessedCount:N0}条 成功{AddedCount:N0}条 失败{ErrorCount:N0}条 耗时{Duration:F1}秒 查询{QueryTime:F1}秒 插入{InsertTime:F1}秒 线程{ThreadId} 进程{ProcessId} 内存{Memory:N0}MB",
                    storeCode,
                    processedCount,
                    storeAdded,
                    storeErrors,
                    storeDuration.TotalSeconds,
                    queryDuration.TotalSeconds,
                    totalInsertDuration.TotalSeconds,
                    currentThreadId,
                    processId,
                    finalMemory
                );

                // 释放大对象内存引用，帮助GC回收
                hqStoreData.Clear();
                localStoreData.Clear();

                return (
                    processedCount,
                    storeAdded,
                    storeErrors,
                    queryDuration.TotalSeconds,
                    totalInsertDuration.TotalSeconds
                );
            }
            catch (Exception ex)
            {
                var errorThreadId = Thread.CurrentThread.ManagedThreadId;
                var errorProcessId = Environment.ProcessId;
                _logger.LogError(
                    ex,
                    "❌ 分店{StoreCode}处理失败 线程{ThreadId} 进程{ProcessId}: {Message}",
                    storeCode,
                    errorThreadId,
                    errorProcessId,
                    ex.Message
                );
                return (0, 0, 1, 0, 0);
            }
            finally
            {
                // 确保连接被释放
                localDb?.Dispose();
                hqDb?.Dispose();
                semaphore.Release();
            }
        }

        /// <summary>
        /// 🚀 处理单个批次的并发插入（仅插入，不查询）
        /// </summary>
        private async Task<(int processed, int added, int errors)> ProcessBatchInsertAsync(
            int batchIndex,
            List<DIC_商品零售价表> hqBatchData,
            SemaphoreSlim semaphore
        )
        {
            await semaphore.WaitAsync();

            ISqlSugarClient? localDb = null;
            try
            {
                var batchStartTime = DateTime.Now;
                _logger.LogInformation(
                    $"📦 批次 {batchIndex + 1} 开始插入处理: {hqBatchData.Count:N0} 条记录"
                );

                // 创建独立的数据库连接用于插入
                localDb = SqlSugarContext.CreateConcurrentConnection(_configuration);
                _logger.LogDebug($"🔗 批次 {batchIndex + 1} 本地数据库连接创建完成");

                if (!hqBatchData.Any())
                {
                    _logger.LogWarning($"⚠️ 批次 {batchIndex + 1} 没有数据，跳过处理");
                    return (0, 0, 0);
                }

                // 步骤1：转换数据格式
                var mapStartTime = DateTime.Now;
                var localBatchData = _mapper.Map<List<StoreRetailPrice>>(hqBatchData);
                var mapDuration = DateTime.Now - mapStartTime;
                _logger.LogDebug(
                    $"🔄 批次 {batchIndex + 1} 数据转换完成: {localBatchData.Count:N0} 条（耗时: {mapDuration.TotalSeconds:F1}秒）"
                );

                // 步骤2：直接批量插入（不分页）
                var batchAdded = 0;
                var batchErrors = 0;

                _logger.LogInformation(
                    $"📄 批次 {batchIndex + 1} 直接插入 {localBatchData.Count:N0} 条记录"
                );

                try
                {
                    // 使用重试机制的批量插入
                    await RetryBulkInsertAsync(localDb, localBatchData, 3);
                    batchAdded += localBatchData.Count;

                    _logger.LogDebug(
                        $"✅ 批次 {batchIndex + 1} 批量插入成功: {localBatchData.Count:N0} 条"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ 批次 {batchIndex + 1} 批量插入失败: {ex.Message}");
                    batchErrors += localBatchData.Count;
                }

                var batchDuration = DateTime.Now - batchStartTime;
                var batchSpeed = batchAdded > 0 ? batchAdded / batchDuration.TotalSeconds : 0;

                _logger.LogInformation(
                    $"✅ 批次 {batchIndex + 1} 完成: 处理 {hqBatchData.Count:N0} 条，成功 {batchAdded:N0} 条，失败 {batchErrors:N0} 条"
                );
                _logger.LogInformation(
                    $"⚡ 批次 {batchIndex + 1} 性能: 耗时 {batchDuration.TotalSeconds:F1}秒，速度 {batchSpeed:F0}条/秒"
                );

                return (hqBatchData.Count, batchAdded, batchErrors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ 批次 {batchIndex + 1} 处理失败: {ex.Message}");
                return (hqBatchData.Count, 0, hqBatchData.Count);
            }
            finally
            {
                // 确保连接被释放
                localDb?.Dispose();
                semaphore.Release();
            }
        }

        /// <summary>
        /// 🚀 带重试机制的批量插入
        /// </summary>
        private async Task RetryBulkInsertAsync<T>(ISqlSugarClient db, List<T> data, int maxRetries)
            where T : class, new()
        {
            var retryCount = 0;
            var insertStartTime = DateTime.Now;

            while (retryCount < maxRetries)
            {
                try
                {
                    var attemptStartTime = DateTime.Now;
                    await db.Fastest<T>().PageSize(50000).BulkCopyAsync(data);

                    var insertDuration = DateTime.Now - insertStartTime;
                    var attemptDuration = DateTime.Now - attemptStartTime;
                    var speed = data.Count / attemptDuration.TotalSeconds;

                    if (retryCount > 0)
                    {
                        _logger.LogInformation(
                            "✅ 批量插入重试成功: {Count:N0}条 第{RetryCount}次尝试 耗时{Duration:F1}秒 速度{Speed:F0}条/秒",
                            data.Count,
                            retryCount + 1,
                            attemptDuration.TotalSeconds,
                            speed
                        );
                    }

                    // 批量插入成功后，建议GC清理临时对象
                    if (data.Count > 10000) // 只对大批量数据触发GC
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }

                    return; // 成功则退出
                }
                catch (Exception ex) when (retryCount < maxRetries - 1)
                {
                    retryCount++;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // 指数退避
                    _logger.LogWarning(
                        "⚠️ 批量插入失败，第{RetryCount}/{MaxRetries}次重试，等待{DelaySeconds}秒: {Message}",
                        retryCount,
                        maxRetries,
                        delay.TotalSeconds,
                        ex.Message
                    );
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    var totalDuration = DateTime.Now - insertStartTime;
                    _logger.LogError(
                        ex,
                        "❌ 批量插入最终失败: {Count:N0}条，尝试{MaxRetries}次，总耗时{Duration:F1}秒",
                        data.Count,
                        maxRetries,
                        totalDuration.TotalSeconds
                    );
                    throw;
                }
            }
        }

        /// <summary>
        /// 从HQ同步分店清货价数据
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreClearancePricesFromHqAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                _logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店清货价数据{(selectedStoreCodes?.Any() == true ? $"，指定分店: {string.Join(", ", selectedStoreCodes)}" : "，全部分店")}"
                );

                // 🚀 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                var query = _hqContext
                    .Db.Queryable<DIC_商品清货价表, DIC_商品信息字典表>(
                        (clearance, product) =>
                            new JoinQueryInfos(
                                JoinType.Inner,
                                clearance.商品编码 == product.H商品编码
                            )
                    )
                    .Where(
                        (clearance, product) =>
                            !string.IsNullOrEmpty(clearance.商品编码)
                            && !string.IsNullOrEmpty(clearance.分店代码)
                            && product.H使用状态 == true
                    );

                // 如果指定了分店代码，添加分店代码过滤条件
                if (selectedStoreCodes?.Any() == true)
                {
                    query = query.Where(
                        (clearance, product) => selectedStoreCodes.Contains(clearance.分店代码)
                    );
                }

                var hqClearancePrices = await query
                    .Select((clearance, product) => clearance)
                    .ToListAsync();

                _logger.LogInformation(
                    $"📊 从HQ获取到 {hqClearancePrices.Count:N0} 条有效的分店清货价记录（已过滤无效商品和分店）"
                );

                if (!hqClearancePrices.Any())
                {
                    result.Message = "✅ HQ数据库中没有分店清货价数据，同步完成";
                    result.IsSuccess = true;
                    return result;
                }

                // 开始数据库事务
                var db = _localContext.Db;
                await db.Ado.BeginTranAsync();

                try
                {
                    // 根据是否指定分店来决定删除策略
                    if (selectedStoreCodes?.Any() == true)
                    {
                        _logger.LogInformation(
                            $"🗑️ 正在清空指定分店的清货价数据: {string.Join(", ", selectedStoreCodes)}"
                        );
                        await db.Deleteable<StoreClearancePrice>()
                            .Where(x =>
                                x.StoreCode != null && selectedStoreCodes.Contains(x.StoreCode)
                            )
                            .ExecuteCommandAsync();
                        _logger.LogInformation("✅ 指定分店的清货价数据已清空");
                    }
                    else
                    {
                        _logger.LogInformation("🗑️ 正在清空本地分店清货价表...");
                        await db.Deleteable<StoreClearancePrice>().ExecuteCommandAsync();
                        _logger.LogInformation("✅ 本地分店清货价表已清空");
                    }

                    int totalProcessed = 0;
                    int totalAdded = 0;
                    int totalErrors = 0;
                    const int batchSize = 10000;

                    // 转换数据 - 使用AutoMapper
                    _logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localClearancePrices = _mapper.Map<List<StoreClearancePrice>>(
                        hqClearancePrices
                    );
                    _logger.LogInformation(
                        $"✅ 数据转换完成，共 {localClearancePrices.Count:N0} 条记录"
                    );

                    // 使用PageSize优化的批量插入
                    _logger.LogInformation(
                        $"📦 开始批量插入 {localClearancePrices.Count:N0} 条记录..."
                    );
                    try
                    {
                        await db.Fastest<StoreClearancePrice>()
                            .PageSize(batchSize) // 让SqlSugar内部处理分批
                            .BulkCopyAsync(localClearancePrices);

                        totalAdded = localClearancePrices.Count;
                        totalProcessed = localClearancePrices.Count;

                        _logger.LogInformation($"✅ 批量插入完成，成功插入 {totalAdded:N0} 条记录");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ 批量插入失败，错误: {ex.Message}");
                        totalErrors = localClearancePrices.Count;
                    }

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        totalErrors == 0
                            ? $"🎉 分店清货价数据同步成功！共处理 {totalProcessed:N0} 条记录，全部成功插入"
                            : $"⚠️ 分店清货价数据同步部分成功！成功: {totalAdded:N0}, 失败: {totalErrors:N0}";

                    _logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 同步分店清货价数据时发生错误");
                result.Message = $"❌ 同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                _logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }

        /// <summary>
        /// 从HQ同步分店一品多码数据
        /// </summary>
        /// <param name="selectedStoreCodes">选中的分店代码列表，为空时同步所有分店</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncStoreMultiCodeProductsFromHqAsync(
            List<string>? selectedStoreCodes = null
        )
        {
            var result = new SyncResult { StartTime = DateTime.Now };

            try
            {
                _logger.LogInformation(
                    $"🔄 开始从HQ数据库同步分店一品多码数据{(selectedStoreCodes?.Any() == true ? $"，指定分店: {string.Join(", ", selectedStoreCodes)}" : "，全部分店")}"
                );

                // 🚀 使用JOIN查询获取有效的HQ数据（商品信息存在且分店代码匹配）
                var query = _hqContext
                    .Db.Queryable<DIC_分店一品多码表, DIC_商品信息字典表>(
                        (multiCode, product) =>
                            new JoinQueryInfos(
                                JoinType.Inner,
                                multiCode.H商品编码 == product.H商品编码
                            )
                    )
                    .Where(
                        (multiCode, product) =>
                            !string.IsNullOrEmpty(multiCode.H商品编码)
                            && !string.IsNullOrEmpty(multiCode.H分店代码)
                            && multiCode.H使用状态 == true
                            && product.H使用状态 == true
                    );

                // 如果指定了分店代码，添加分店代码过滤条件
                if (selectedStoreCodes?.Any() == true)
                {
                    query = query.Where(
                        (multiCode, product) =>
                            !string.IsNullOrEmpty(multiCode.H分店代码)
                            && selectedStoreCodes.Contains(multiCode.H分店代码)
                    );
                }

                var hqMultiCodeProducts = await query
                    .Select((multiCode, product) => multiCode)
                    .ToListAsync();

                _logger.LogInformation(
                    $"📊 从HQ获取到 {hqMultiCodeProducts.Count:N0} 条有效的分店一品多码记录（已过滤无效商品和分店）"
                );

                if (!hqMultiCodeProducts.Any())
                {
                    result.Message = "✅ HQ数据库中没有分店一品多码数据，同步完成";
                    result.IsSuccess = true;
                    return result;
                }

                // 开始数据库事务
                var db = _localContext.Db;
                await db.Ado.BeginTranAsync();

                try
                {
                    _logger.LogInformation("🗑️ 正在清空本地分店一品多码表...");
                    await db.Deleteable<StoreMultiCodeProduct>().ExecuteCommandAsync();
                    _logger.LogInformation("✅ 本地分店一品多码表已清空");

                    int totalProcessed = 0;
                    int totalAdded = 0;
                    int totalErrors = 0;
                    const int batchSize = 20000;

                    // 转换数据 - 使用AutoMapper
                    _logger.LogInformation("🔄 开始转换数据格式 (使用AutoMapper)...");
                    var localMultiCodeProducts = _mapper.Map<List<StoreMultiCodeProduct>>(
                        hqMultiCodeProducts
                    );
                    _logger.LogInformation(
                        $"✅ 数据转换完成，共 {localMultiCodeProducts.Count:N0} 条记录"
                    );

                    // 使用PageSize优化的批量插入
                    _logger.LogInformation(
                        $"📦 开始批量插入 {localMultiCodeProducts.Count:N0} 条记录..."
                    );
                    try
                    {
                        await db.Fastest<StoreMultiCodeProduct>()
                            .PageSize(batchSize) // 让SqlSugar内部处理分批
                            .BulkCopyAsync(localMultiCodeProducts);

                        totalAdded = localMultiCodeProducts.Count;
                        totalProcessed = localMultiCodeProducts.Count;

                        _logger.LogInformation($"✅ 批量插入完成，成功插入 {totalAdded:N0} 条记录");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"❌ 批量插入失败，错误: {ex.Message}");
                        totalErrors = localMultiCodeProducts.Count;
                    }

                    // 提交事务
                    await db.Ado.CommitTranAsync();
                    _logger.LogInformation("✅ 事务提交成功");

                    result.AddedCount = totalAdded;
                    result.ErrorCount = totalErrors;
                    result.IsSuccess = totalErrors == 0;
                    result.Message =
                        totalErrors == 0
                            ? $"🎉 分店一品多码数据同步成功！共处理 {totalProcessed:N0} 条记录，全部成功插入"
                            : $"⚠️ 分店一品多码数据同步部分成功！成功: {totalAdded:N0}, 失败: {totalErrors:N0}";

                    _logger.LogInformation(result.Message);
                }
                catch (Exception ex)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(ex, "❌ 事务回滚，同步失败");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 同步分店一品多码数据时发生错误");
                result.Message = $"❌ 同步失败: {ex.Message}";
                result.IsSuccess = false;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
                _logger.LogInformation($"⏱️ 同步耗时: {result.Duration.TotalSeconds:F1} 秒");
            }

            return result;
        }

        /// <summary>
        /// 测试PostgreSQL数据库连接
        /// 🔗 使用配置的PostgreSQL连接字符串测试数据库连接状态
        /// </summary>
        /// <returns>连接测试结果</returns>
        public async Task<SyncResult> TestPostgresConnectionAsync()
        {
            var result = new SyncResult();
            var connectionString =
                "Host=hotbargain.vip;Port=5432;Database=postgresdb;Username=postgres;Password=REDACTED;

            try
            {
                _logger.LogInformation("🔗 开始测试PostgreSQL数据库连接...");
                _logger.LogInformation(
                    "连接字符串: Host=hotbargain.vip;Port=5432;Database=postgresdb;Username=postgres;Password=REDACTED
                );

                using var connection = new Npgsql.NpgsqlConnection(connectionString);

                // 测试连接
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                await connection.OpenAsync();
                stopwatch.Stop();

                // 测试简单查询
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT version(), current_database(), current_user, now()";
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var version = reader.GetString(0);
                    var database = reader.GetString(1);
                    var user = reader.GetString(2);
                    var serverTime = reader.GetDateTime(3);

                    result.IsSuccess = true;
                    result.Message =
                        $"✅ PostgreSQL连接成功！响应时间: {stopwatch.ElapsedMilliseconds}ms";
                    result.Details =
                        $"数据库版本: {version.Split(',')[0]}\n"
                        + $"数据库名称: {database}\n"
                        + $"连接用户: {user}\n"
                        + $"服务器时间: {serverTime:yyyy-MM-dd HH:mm:ss}";

                    _logger.LogInformation(
                        "✅ PostgreSQL数据库连接测试成功，响应时间: {ElapsedMs}ms",
                        stopwatch.ElapsedMilliseconds
                    );
                }
                else
                {
                    result.IsSuccess = false;
                    result.Message = "❌ 连接成功但无法执行查询";
                    result.Details = "数据库连接正常，但查询测试失败";
                }

                await connection.CloseAsync();
            }
            catch (Npgsql.NpgsqlException ex)
            {
                result.IsSuccess = false;
                result.Message = "❌ PostgreSQL连接失败";
                result.Details = $"错误类型: {ex.GetType().Name}\n错误信息: {ex.Message}";
                result.ErrorCount = 1;

                _logger.LogError(ex, "PostgreSQL数据库连接失败");
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Message = "❌ 数据库连接测试异常";
                result.Details = $"异常类型: {ex.GetType().Name}\n异常信息: {ex.Message}";
                result.ErrorCount = 1;

                _logger.LogError(ex, "PostgreSQL数据库连接测试时发生异常");
            }

            return result;
        }

        /// <summary>
        /// 反向同步：将本地国内商品信息同步到HQ数据库
        /// 按照货号匹配，将DomesticProduct表中指定日期更新的商品信息同步到CPT_DIC_商品信息字典表
        /// </summary>
        /// <param name="lastUpdateDate">最后更新日期，只同步此日期之后更新的商品</param>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncDomesticProductsToHqAsync(DateTime lastUpdateDate)
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                IsSuccess = false,
                Message = "国内商品信息反向同步失败",
            };

            try
            {
                _logger.LogInformation(
                    "开始反向同步国内商品信息到HQ数据库，上次更新时间: {LastUpdateDate}",
                    lastUpdateDate
                );

                // 查询本地DomesticProduct表中需要同步的商品（按更新时间筛选，且商品编码不为空）
                var domesticProducts = await _localContext
                    .DomesticProductDb.AsQueryable()
                    .Where(dp =>
                        dp.UpdatedAt >= lastUpdateDate
                        && !string.IsNullOrEmpty(dp.ProductCode)
                        && dp.IsActive == true
                    )
                    .ToListAsync();

                if (!domesticProducts.Any())
                {
                    result.IsSuccess = true;
                    result.Message =
                        $"没有找到 {lastUpdateDate:yyyy-MM-dd HH:mm:ss} 之后更新的国内商品信息，无需同步";
                    _logger.LogInformation(result.Message);
                    return result;
                }

                _logger.LogInformation($"找到 {domesticProducts.Count} 个需要反向同步的国内商品");

                int totalUpdated = 0;
                int totalErrors = 0;

                // 分批处理商品
                const int batchSize = 100;
                for (int i = 0; i < domesticProducts.Count; i += batchSize)
                {
                    var batch = domesticProducts.Skip(i).Take(batchSize).ToList();
                    var productCodes = batch
                        .Where(p => !string.IsNullOrEmpty(p.ProductCode))
                        .Select(p => p.ProductCode!)
                        .ToList();

                    if (!productCodes.Any())
                        continue;

                    try
                    {
                        // 查询HQ数据库中对应的商品信息字典记录（通过商品编码匹配）
                        // 移除更新日期限制，因为我们需要处理所有本地商品（包括新建）
                        var hqProducts = await _hbSalesContext
                            .CPT_DIC_商品信息字典表Db.AsQueryable()
                            .Where(hp =>
                                !string.IsNullOrEmpty(hp.商品编码)
                                && productCodes.Contains(hp.商品编码)
                            )
                            .ToListAsync();

                        _logger.LogInformation(
                            $"批次 {i / batchSize + 1}: 本地商品 {productCodes.Count} 个，HQ中已存在 {hqProducts.Count} 个，需新建 {productCodes.Count - hqProducts.Count} 个"
                        );

                        // 创建更新映射（通过商品编码）
                        var hqProductDict = hqProducts.ToDictionary(
                            hp => hp.商品编码 ?? "",
                            hp => hp
                        );

                        // 批量准备需要更新的商品数据
                        var batchUpdates = new List<CPT_DIC_商品信息字典表>();
                        var batchInserts = new List<CPT_DIC_商品信息字典表>(); // 新增：批量插入的商品数据

                        foreach (var domesticProduct in batch)
                        {
                            if (string.IsNullOrEmpty(domesticProduct.ProductCode))
                                continue;

                            if (
                                hqProductDict.TryGetValue(
                                    domesticProduct.ProductCode,
                                    out var hqProduct
                                )
                            )
                            {
                                var isUpdated = false;
                                var originalProduct = new CPT_DIC_商品信息字典表();
                                originalProduct = hqProduct;

                                if (originalProduct.使用状态 == null)
                                {
                                    originalProduct.使用状态 = 1;
                                    isUpdated = true;
                                }
                                if (originalProduct.HGUID == null)
                                {
                                    originalProduct.HGUID = Guid.NewGuid().ToString();
                                    isUpdated = true;
                                }
                                if (originalProduct.供应商编码 == null)
                                {
                                    originalProduct.供应商编码 = domesticProduct.SupplierCode;
                                    isUpdated = true;
                                }
                                if (originalProduct.HB货号 == null)
                                {
                                    originalProduct.HB货号 = domesticProduct.HBProductNo;
                                    isUpdated = true;
                                }

                                if (originalProduct.条形码 == null)
                                {
                                    originalProduct.条形码 = domesticProduct.Barcode;
                                    isUpdated = true;
                                }

                                if (originalProduct.FGC_Creator == null)
                                {
                                    originalProduct.FGC_Creator = domesticProduct.CreatedBy;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_CreateDate == null)
                                {
                                    originalProduct.FGC_CreateDate = domesticProduct.CreatedAt;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_LastModifyDate == null)
                                {
                                    originalProduct.FGC_LastModifyDate = DateTime.Now;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_LastModifier == null)
                                {
                                    originalProduct.FGC_LastModifier = domesticProduct.UpdatedBy;
                                    isUpdated = true;
                                }
                                if (originalProduct.FGC_UpdateHelp == null)
                                {
                                    originalProduct.FGC_UpdateHelp = Guid.NewGuid().ToString();
                                    isUpdated = true;
                                }
                                if (originalProduct.使用状态 == null)
                                {
                                    originalProduct.使用状态 = 1;
                                    isUpdated = true;
                                }

                                // 只有当源数据字段不为空时才进行更新
                                if (!string.IsNullOrEmpty(domesticProduct.EnglishProductName))
                                {
                                    originalProduct.英文名称 = domesticProduct.EnglishProductName;
                                    isUpdated = true;
                                }

                                if (!string.IsNullOrEmpty(domesticProduct.ProductName))
                                {
                                    originalProduct.中文名称 = domesticProduct.ProductName;
                                    isUpdated = true;
                                }

                                if (domesticProduct.ProductType > 0)
                                {
                                    originalProduct.商品类型 = domesticProduct.ProductType;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.MiddlePackQuantity.HasValue
                                    && domesticProduct.MiddlePackQuantity.Value > 0
                                )
                                {
                                    originalProduct.中包数量 = domesticProduct
                                        .MiddlePackQuantity
                                        .Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.DomesticPrice.HasValue
                                    && domesticProduct.DomesticPrice.Value > 0
                                )
                                {
                                    originalProduct.国内价格 = domesticProduct.DomesticPrice.Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.ImportPrice.HasValue
                                    && domesticProduct.ImportPrice.Value > 0
                                )
                                {
                                    originalProduct.进口价格 = domesticProduct.ImportPrice.Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.OEMPrice.HasValue
                                    && domesticProduct.OEMPrice.Value > 0
                                )
                                {
                                    originalProduct.贴牌价格 = domesticProduct.OEMPrice.Value;
                                    isUpdated = true;
                                }

                                // 处理商品图片：修复可能的重复URL，如果为空则使用默认地址+货号
                                // 确保HBProductNo不是完整的URL，避免重复拼接
                                string? productImage = domesticProduct.ProductImage;

                                // 先修复可能存在的重复URL（防止污染HQ数据库）
                                if (!string.IsNullOrEmpty(productImage))
                                {
                                    productImage =
                                        BlazorApp.Api.Utils.ImageUrlHelper.FixDuplicateUrl(
                                            productImage
                                        )
                                        ?? productImage;
                                }

                                // 如果为空，则根据货号生成
                                if (
                                    string.IsNullOrEmpty(productImage)
                                    && !string.IsNullOrEmpty(domesticProduct.HBProductNo)
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "http://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "https://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    productImage =
                                        $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{domesticProduct.HBProductNo}.jpg";
                                }

                                if (!string.IsNullOrEmpty(productImage))
                                {
                                    originalProduct.商品图片 = productImage;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.PackingQuantity.HasValue
                                    && domesticProduct.PackingQuantity.Value > 0
                                )
                                {
                                    originalProduct.单件装箱数 = domesticProduct
                                        .PackingQuantity
                                        .Value;
                                    isUpdated = true;
                                }

                                if (
                                    domesticProduct.UnitVolume.HasValue
                                    && domesticProduct.UnitVolume.Value > 0
                                )
                                {
                                    originalProduct.单件体积 = domesticProduct.UnitVolume.Value;
                                    isUpdated = true;
                                }
                                originalProduct.FGC_LastModifyDate = DateTime.Now;

                                if (isUpdated)
                                {
                                    batchUpdates.Add(originalProduct);
                                }
                            }
                            else
                            {
                                // 如果在HQ数据库中找不到对应的商品记录，则创建新商品
                                _logger.LogInformation(
                                    $"在HQ数据库中未找到对应的商品记录，准备新建: 商品编码={domesticProduct.ProductCode}, HB货号={domesticProduct.HBProductNo}"
                                );

                                var newHqProduct = new CPT_DIC_商品信息字典表
                                {
                                    商品编码 = domesticProduct.ProductCode,
                                    供应商编码 = domesticProduct.SupplierCode,
                                    HB货号 = domesticProduct.HBProductNo,
                                    中文名称 = domesticProduct.ProductName,
                                    英文名称 = domesticProduct.EnglishProductName,
                                    条形码 = domesticProduct.Barcode,
                                    商品类型 = domesticProduct.ProductType,
                                    中包数量 = domesticProduct.MiddlePackQuantity ?? 0,
                                    国内价格 = domesticProduct.DomesticPrice ?? 0,
                                    进口价格 = domesticProduct.ImportPrice ?? 0,
                                    贴牌价格 = domesticProduct.OEMPrice ?? 0,
                                    单件装箱数 = domesticProduct.PackingQuantity ?? 0,
                                    单件体积 = domesticProduct.UnitVolume ?? 0,
                                    使用状态 = domesticProduct.IsActive ? 1 : 0,
                                    HGUID = Guid.NewGuid().ToString(),
                                    FGC_Creator = domesticProduct.CreatedBy ?? "System",
                                    FGC_CreateDate = domesticProduct.CreatedAt,
                                    FGC_LastModifyDate = DateTime.Now,
                                    FGC_LastModifier = domesticProduct.UpdatedBy ?? "System",
                                    FGC_UpdateHelp = Guid.NewGuid().ToString(),
                                };

                                // 处理商品图片：修复可能的重复URL，如果为空则使用默认地址+货号
                                // 确保HBProductNo不是完整的URL，避免重复拼接
                                string? productImage = domesticProduct.ProductImage;

                                // 先修复可能存在的重复URL（防止污染HQ数据库）
                                if (!string.IsNullOrEmpty(productImage))
                                {
                                    productImage =
                                        BlazorApp.Api.Utils.ImageUrlHelper.FixDuplicateUrl(
                                            productImage
                                        )
                                        ?? productImage;
                                }

                                // 如果为空，则根据货号生成
                                if (
                                    string.IsNullOrEmpty(productImage)
                                    && !string.IsNullOrEmpty(domesticProduct.HBProductNo)
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "http://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    && !domesticProduct.HBProductNo.StartsWith(
                                        "https://",
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    productImage =
                                        $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{domesticProduct.HBProductNo}.jpg";
                                }
                                newHqProduct.商品图片 = productImage;

                                batchInserts.Add(newHqProduct);
                            }
                        }

                        // 使用大数据方法执行批量更新
                        if (batchUpdates.Any())
                        {
                            try
                            {
                                var batchUpdateCount = _hbSalesContext
                                    .Db.Fastest<CPT_DIC_商品信息字典表>()
                                    .BulkUpdate(batchUpdates);

                                totalUpdated += batchUpdateCount;
                                _logger.LogInformation(
                                    $"批次 {i / batchSize + 1} 大数据批量更新完成，更新了 {batchUpdateCount} 个商品"
                                );
                            }
                            catch (Exception batchEx)
                            {
                                _logger.LogError(
                                    batchEx,
                                    $"批次 {i / batchSize + 1} 大数据批量更新失败"
                                );
                                totalErrors += batchUpdates.Count;
                            }
                        }
                        else
                        {
                            _logger.LogInformation(
                                $"批次 {i / batchSize + 1} 没有需要更新的商品数据"
                            );
                        }

                        // 使用大数据方法执行批量插入新商品
                        if (batchInserts.Any())
                        {
                            try
                            {
                                var batchInsertCount = _hbSalesContext
                                    .Db.Fastest<CPT_DIC_商品信息字典表>()
                                    .BulkCopy(batchInserts);

                                totalUpdated += batchInsertCount;
                                _logger.LogInformation(
                                    $"批次 {i / batchSize + 1} 大数据批量插入完成，新建了 {batchInsertCount} 个商品"
                                );
                            }
                            catch (Exception batchEx)
                            {
                                _logger.LogError(
                                    batchEx,
                                    $"批次 {i / batchSize + 1} 大数据批量插入失败"
                                );
                                totalErrors += batchInserts.Count;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"批次 {i / batchSize + 1} 处理失败");
                        totalErrors += batch.Count;
                    }
                }

                // 设置结果
                result.UpdatedCount = totalUpdated;
                result.ErrorCount = totalErrors;
                result.IsSuccess = totalErrors == 0;
                result.Message =
                    $"国内商品信息反向同步完成！成功同步（更新+新建）: {totalUpdated} 个，错误: {totalErrors} 个";

                _logger.LogInformation(result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "反向同步国内商品信息时发生错误");
                result.Message = $"反向同步失败: {ex.Message}";
                result.IsSuccess = false;
                result.ErrorCount = 1;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }

            return result;
        }
    }

    /// <summary>
    /// 批次处理结果
    /// </summary>
    public class BatchResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 处理数量
        /// </summary>
        public int ProcessedCount { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int PageNumber { get; set; }
    }
}
