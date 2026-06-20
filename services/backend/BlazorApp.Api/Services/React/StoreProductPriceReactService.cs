using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店商品价格服务实现
    /// </summary>
    public class StoreProductPriceReactService : IStoreProductPriceReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<StoreProductPriceReactService> _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        public StoreProductPriceReactService(
            SqlSugarContext context,
            ILogger<StoreProductPriceReactService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 获取分店商品价格网格数据
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>分页结果</returns>
        public async Task<GridResponseDto<StoreProductPriceListDto>> GetGridDataAsync(
            StoreProductPriceQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var pageIndex = query.PageNumber;
                var pageSize = query.PageSize;

                var baseQuery = db.Queryable<Product>()
                    .With(SqlWith.NoLock)
                    .Where(p => p.IsDeleted == false);

                if (!string.IsNullOrWhiteSpace(query.LocalSupplierCode))
                {
                    baseQuery = baseQuery.Where(p =>
                        p.LocalSupplierCode == query.LocalSupplierCode
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var keyword = query.Search.Trim();
                    baseQuery = baseQuery.Where(p =>
                        p.ProductName.Contains(keyword)
                        || (p.ProductCode != null && p.ProductCode.Contains(keyword))
                        || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        || (p.Barcode != null && p.Barcode.Contains(keyword))
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.ProductName))
                {
                    baseQuery = baseQuery.Where(p => p.ProductName.Contains(query.ProductName));
                }

                if (!string.IsNullOrWhiteSpace(query.ProductCode))
                {
                    baseQuery = baseQuery.Where(p =>
                        p.ProductCode != null && p.ProductCode.Contains(query.ProductCode)
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.ItemNumber))
                {
                    baseQuery = baseQuery.Where(p =>
                        p.ItemNumber != null && p.ItemNumber.Contains(query.ItemNumber)
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.Barcode))
                {
                    baseQuery = baseQuery.Where(p =>
                        p.Barcode != null && p.Barcode.Contains(query.Barcode)
                    );
                }

                if (query.ProductType.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.ProductType == query.ProductType.Value);
                }

                if (query.IsActive.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.IsActive == query.IsActive.Value);
                }

                if (query.IsSpecialProduct.HasValue)
                {
                    baseQuery = baseQuery.Where(p =>
                        p.IsSpecialProduct == query.IsSpecialProduct.Value
                    );
                }

                var joinQuery = baseQuery
                    .LeftJoin<StoreRetailPrice>(
                        (p, srp) =>
                            p.ProductCode == srp.ProductCode
                            && srp.StoreCode == query.StoreCode
                            && srp.IsDeleted == false
                    )
                    .LeftJoin<HBLocalSupplier>(
                        (p, srp, sup) =>
                            p.LocalSupplierCode == sup.LocalSupplierCode && sup.IsDeleted == false
                    );

                if (query.PurchasePriceGt.HasValue)
                {
                    joinQuery = joinQuery.Where(
                        (p, srp, sup) => srp.PurchasePrice >= query.PurchasePriceGt.Value
                    );
                }

                if (query.PurchasePriceLt.HasValue)
                {
                    joinQuery = joinQuery.Where(
                        (p, srp, sup) => srp.PurchasePrice <= query.PurchasePriceLt.Value
                    );
                }

                if (query.RetailPriceGt.HasValue)
                {
                    joinQuery = joinQuery.Where(
                        (p, srp, sup) => srp.StoreRetailPriceValue >= query.RetailPriceGt.Value
                    );
                }

                if (query.RetailPriceLt.HasValue)
                {
                    joinQuery = joinQuery.Where(
                        (p, srp, sup) => srp.StoreRetailPriceValue <= query.RetailPriceLt.Value
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.SortBy))
                {
                    var asc = query.SortOrder?.ToLower() == "asc";
                    joinQuery = query.SortBy.ToLower() switch
                    {
                        "productname" => joinQuery.OrderBy(
                            (p, srp, sup) => p.ProductName,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "productcode" => joinQuery.OrderBy(
                            (p, srp, sup) => p.ProductCode,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "itemnumber" => joinQuery.OrderBy(
                            (p, srp, sup) => p.ItemNumber,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "barcode" => joinQuery.OrderBy(
                            (p, srp, sup) => p.Barcode,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "middlesackagequantity" => joinQuery.OrderBy(
                            (p, srp, sup) => p.MiddlePackageQuantity,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "purchaseprice" => joinQuery.OrderBy(
                            (p, srp, sup) => srp.PurchasePrice,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "retailprice" => joinQuery.OrderBy(
                            (p, srp, sup) => srp.StoreRetailPriceValue,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "discountrate" => joinQuery.OrderBy(
                            (p, srp, sup) => srp.DiscountRate,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        "updatedat" => joinQuery.OrderBy(
                            (p, srp, sup) => p.UpdatedAt,
                            asc ? OrderByType.Asc : OrderByType.Desc
                        ),
                        _ => joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, OrderByType.Desc),
                    };
                }
                else
                {
                    joinQuery = joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, OrderByType.Desc);
                }

                var totalRef = new RefAsync<int>(0);
                var items = await joinQuery
                    .Select(
                        (p, srp, sup) =>
                            new StoreProductPriceListDto
                            {
                                ProductCode = p.ProductCode,
                                ProductName = p.ProductName,
                                ProductImage = p.ProductImage,
                                ItemNumber = p.ItemNumber,
                                Barcode = p.Barcode,
                                LocalSupplierCode = p.LocalSupplierCode,
                                LocalSupplierName = sup.Name,
                                ProductType = p.ProductType,
                                MiddlePackageQuantity = p.MiddlePackageQuantity,
                                IsActive = p.IsActive,
                                UpdatedAt = srp.UpdatedAt,
                                UpdatedBy = srp.UpdatedBy,
                                StoreCode = srp.StoreCode,
                                StorePurchasePrice = srp.PurchasePrice,
                                StoreRetailPrice = srp.StoreRetailPriceValue,
                                IsStoreAutoPricing = srp.IsAutoPricing,
                                IsStoreSpecialProduct = srp.IsSpecialProduct,
                                DiscountRate = srp.DiscountRate,
                            }
                    )
                    .ToPageListAsync(pageIndex, pageSize, totalRef);
                return GridResponseDto<StoreProductPriceListDto>.OK(items, totalRef.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreProductPrice Grid 查询失败");
                return GridResponseDto<StoreProductPriceListDto>.Error("查询失败");
            }
        }

        /// <summary>
        /// 批量更新分店商品价格
        /// </summary>
        /// <param name="dto">批量更新数据传输对象</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>API响应</returns>
        public async Task<ApiResponse<object>> BatchUpdateStoreRetailPricesAsync(
            BatchUpdateStoreRetailPriceDto dto,
            string updatedBy
        )
        {
            try
            {
                if (dto.ProductCodes == null || !dto.ProductCodes.Any())
                {
                    return ApiResponse<object>.Error(
                        "请选择要更新的分店和商品",
                        "VALIDATION_ERROR"
                    );
                }

                // 验证分店编码
                if (string.IsNullOrEmpty(dto.StoreCode))
                {
                    return ApiResponse<object>.Error("请选择要更新的分店", "VALIDATION_ERROR");
                }

                var db = _context.Db;

                await db.Ado.BeginTranAsync();

                try
                {
                    var query = db.Updateable<StoreRetailPrice>();

                    if (dto.PurchasePrice.HasValue)
                    {
                        query = query.SetColumns(x => x.PurchasePrice == dto.PurchasePrice.Value);
                    }

                    if (dto.StoreRetailPriceValue.HasValue)
                    {
                        query = query.SetColumns(x =>
                            x.StoreRetailPriceValue == dto.StoreRetailPriceValue.Value
                        );
                    }

                    if (dto.IsAutoPricing.HasValue)
                    {
                        query = query.SetColumns(x => x.IsAutoPricing == dto.IsAutoPricing.Value);
                    }

                    if (dto.IsSpecialProduct.HasValue)
                    {
                        query = query.SetColumns(x =>
                            x.IsSpecialProduct == dto.IsSpecialProduct.Value
                        );
                    }

                    if (dto.DiscountRate.HasValue)
                    {
                        query = query.SetColumns(x => x.DiscountRate == dto.DiscountRate.Value);
                    }

                    query = query
                        .SetColumns(x => x.UpdatedAt == DateTime.Now)
                        .SetColumns(x => x.UpdatedBy == updatedBy)
                        .Where(x =>
                            x.ProductCode != null
                            && dto.ProductCodes.Contains(x.ProductCode)
                            && x.IsDeleted == false
                            && x.StoreCode == dto.StoreCode
                        );

                    var affectedRows = await query.ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    return ApiResponse<object>.CreateSuccess($"成功更新 {affectedRows} 条记录");
                }
                catch (Exception)
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分店零售价失败");
                return ApiResponse<object>.Error("批量更新失败", "DATABASE_ERROR", ex.Message);
            }
        }

        /// <summary>
        /// 同步商品价格到其他分店
        /// </summary>
        /// <param name="dto">同步数据传输对象</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>API响应</returns>
        public async Task<ApiResponse<object>> SyncToOtherStoresAsync(
            SyncToOtherStoresDto dto,
            string updatedBy
        )
        {
            try
            {
                if (dto.ProductCodes == null || !dto.ProductCodes.Any())
                {
                    return ApiResponse<object>.Error("请选择要同步的商品", "VALIDATION_ERROR");
                }

                if (dto.TargetStoreCodes == null || !dto.TargetStoreCodes.Any())
                {
                    return ApiResponse<object>.Error("请至少选择一个目标分店", "VALIDATION_ERROR");
                }

                var hasAnySyncField =
                    dto.SyncPurchasePrice
                    || dto.SyncRetailPrice
                    || dto.SyncIsAutoPricing
                    || dto.SyncIsSpecialProduct
                    || dto.SyncDiscountRate;

                if (!hasAnySyncField)
                {
                    return ApiResponse<object>.Error(
                        "请至少选择一个要同步的字段",
                        "VALIDATION_ERROR"
                    );
                }

                var db = _context.Db;

                await db.Ado.BeginTranAsync();

                try
                {
                    var sourcePrices = await db.Queryable<StoreRetailPrice>()
                        .With(SqlWith.NoLock)
                        .Where(x =>
                            x.StoreCode == dto.SourceStoreCode
                            && x.ProductCode != null
                            && dto.ProductCodes.Contains(x.ProductCode)
                            && x.IsDeleted == false
                        )
                        .ToListAsync();

                    if (!sourcePrices.Any())
                    {
                        await db.Ado.RollbackTranAsync();
                        return ApiResponse<object>.Error("未找到源分店的价格数据", "NOT_FOUND");
                    }

                    var sourcePriceMap = sourcePrices
                        .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                        .GroupBy(x => x.ProductCode!)
                        .ToDictionary(g => g.Key, g => g.First());

                    var updateable = db.Updateable<StoreRetailPrice>()
                        .SetColumns(x => x.UpdatedAt == DateTime.Now)
                        .SetColumns(x => x.UpdatedBy == updatedBy);

                    if (dto.Mode == SyncModeConstants.Overwrite)
                    {
                        if (dto.SyncPurchasePrice)
                        {
                            updateable.SetColumns(x =>
                                x.PurchasePrice
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.PurchasePrice)
                            );
                        }

                        if (dto.SyncRetailPrice)
                        {
                            updateable.SetColumns(x =>
                                x.StoreRetailPriceValue
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.StoreRetailPriceValue)
                            );
                        }

                        if (dto.SyncIsAutoPricing)
                        {
                            updateable.SetColumns(x =>
                                x.IsAutoPricing
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.IsAutoPricing)
                            );
                        }

                        if (dto.SyncIsSpecialProduct)
                        {
                            updateable.SetColumns(x =>
                                x.IsSpecialProduct
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.IsSpecialProduct)
                            );
                        }

                        if (dto.SyncDiscountRate)
                        {
                            updateable.SetColumns(x =>
                                x.DiscountRate
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.DiscountRate)
                            );
                        }
                    }
                    else if (dto.Mode == SyncModeConstants.OnlyUpdateNull)
                    {
                        if (dto.SyncPurchasePrice)
                        {
                            updateable.SetColumns(x =>
                                x.PurchasePrice
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.PurchasePrice)
                            );
                            updateable.Where(x => x.PurchasePrice == null);
                        }

                        if (dto.SyncRetailPrice)
                        {
                            updateable.SetColumns(x =>
                                x.StoreRetailPriceValue
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.StoreRetailPriceValue)
                            );
                            updateable.Where(x => x.StoreRetailPriceValue == null);
                        }

                        if (dto.SyncIsAutoPricing)
                        {
                            updateable.SetColumns(x =>
                                x.IsAutoPricing
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.IsAutoPricing)
                            );
                            updateable.Where(x => x.IsAutoPricing == false);
                        }

                        if (dto.SyncIsSpecialProduct)
                        {
                            updateable.SetColumns(x =>
                                x.IsSpecialProduct
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.IsSpecialProduct)
                            );
                            updateable.Where(x => x.IsSpecialProduct == false);
                        }

                        if (dto.SyncDiscountRate)
                        {
                            updateable.SetColumns(x =>
                                x.DiscountRate
                                == SqlFunc
                                    .Subqueryable<StoreRetailPrice>()
                                    .Where(s =>
                                        s.StoreCode == dto.SourceStoreCode
                                        && s.ProductCode == x.ProductCode
                                        && s.IsDeleted == false
                                    )
                                    .Select(s => s.DiscountRate)
                            );
                            updateable.Where(x => x.DiscountRate == null);
                        }
                    }

                    var affectedRows = await updateable
                        .Where(x =>
                            x.StoreCode != null
                            && dto.TargetStoreCodes.Contains(x.StoreCode)
                            && x.ProductCode != null
                            && dto.ProductCodes.Contains(x.ProductCode)
                            && x.IsDeleted == false
                        )
                        .ExecuteCommandAsync();

                    await db.Ado.CommitTranAsync();

                    return ApiResponse<object>.CreateSuccess($"成功同步 {affectedRows} 条记录");
                }
                catch (Exception)
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步到其他分店失败");
                return ApiResponse<object>.Error("同步失败", "DATABASE_ERROR", ex.Message);
            }
        }

        /// <summary>
        /// 复制分店数据：将源分店的零售价表和多码表数据复制到目标分店
        /// 使用 Channel 生产者-消费者管道模式，每个目标分店独立 Pipeline 并发执行
        /// </summary>
        public async Task<ApiResponse<CopyStoreDataResultDto>> CopyStoreDataAsync(
            CopyStoreDataDto dto,
            string updatedBy
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.SourceStoreCode))
                    return ApiResponse<CopyStoreDataResultDto>.Error(
                        "请选择源分店",
                        "VALIDATION_ERROR"
                    );

                if (dto.TargetStoreCodes == null || !dto.TargetStoreCodes.Any())
                    return ApiResponse<CopyStoreDataResultDto>.Error(
                        "请至少选择一个目标分店",
                        "VALIDATION_ERROR"
                    );

                if (dto.TargetStoreCodes.Contains(dto.SourceStoreCode))
                    return ApiResponse<CopyStoreDataResultDto>.Error(
                        "目标分店不能包含源分店",
                        "VALIDATION_ERROR"
                    );

                _logger.LogInformation(
                    "开始复制分店数据（Pipeline模式）：源分店={SourceStore}，目标分店={TargetStores}，模式={Mode}，操作人={User}",
                    dto.SourceStoreCode,
                    string.Join(",", dto.TargetStoreCodes),
                    dto.Mode,
                    updatedBy
                );

                const int sourcePageSize = 40000;
                const int mergeBatchSize = 10000;
                var db = _context.Db;

                var storeLimiter = new SemaphoreSlim(3, 3);
                var producerLimiter = new SemaphoreSlim(3, 3);
                var consumerLimiter = new SemaphoreSlim(5, 5);

                var allTasks = new List<Task<(int retailCount, int multiCodeCount)>>();

                foreach (var targetStore in dto.TargetStoreCodes)
                {
                    await storeLimiter.WaitAsync();
                    var store = targetStore;

                    allTasks.Add(
                        Task.Run(async () =>
                        {
                            try
                            {
                                int retailCount = await ProcessStoreRetailPricePipelineAsync(
                                    db.CopyNew(),
                                    dto.SourceStoreCode,
                                    store,
                                    dto,
                                    updatedBy,
                                    sourcePageSize,
                                    mergeBatchSize,
                                    producerLimiter,
                                    consumerLimiter,
                                    null,
                                    CancellationToken.None
                                );

                                int multiCount = 0;
                                if (dto.SyncMultiCode)
                                {
                                    multiCount =
                                        await ProcessStoreMultiCodePipelineAsync(
                                            db.CopyNew(),
                                            dto.SourceStoreCode,
                                            store,
                                            dto,
                                            updatedBy,
                                            sourcePageSize,
                                            mergeBatchSize,
                                            producerLimiter,
                                            consumerLimiter,
                                            null,
                                            CancellationToken.None
                                        );
                                }

                                return (retailCount, multiCount);
                            }
                            finally
                            {
                                storeLimiter.Release();
                            }
                        })
                    );
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var results = await Task.WhenAll(allTasks);
                sw.Stop();

                int retailPriceCopied = results.Sum(r => r.retailCount);
                int multiCodeCopied = results.Sum(r => r.multiCodeCount);

                _logger.LogInformation(
                    "复制完成（Pipeline模式）：零售价 {RetailCopied} 条，多码 {MultiCodeCopied} 条，耗时 {Elapsed}ms",
                    retailPriceCopied,
                    multiCodeCopied,
                    sw.ElapsedMilliseconds
                );

                return ApiResponse<CopyStoreDataResultDto>.OK(
                    new CopyStoreDataResultDto
                    {
                        StoreRetailPriceCopied = retailPriceCopied,
                        StoreMultiCodeProductCopied = multiCodeCopied,
                    },
                    $"复制完成：零售价 {retailPriceCopied} 条，多码 {multiCodeCopied} 条"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "复制分店数据失败");
                return ApiResponse<CopyStoreDataResultDto>.Error(
                    "复制失败",
                    "DATABASE_ERROR",
                    ex.Message
                );
            }
        }

        public async IAsyncEnumerable<CopyProgressDto> CopyStoreDataWithProgressAsync(
            CopyStoreDataDto dto,
            string updatedBy,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(dto.SourceStoreCode))
            {
                yield return new CopyProgressDto
                {
                    EventType = "error",
                    Message = "请选择源分店",
                    Timestamp = DateTime.UtcNow
                };
                yield break;
            }

            if (dto.TargetStoreCodes == null || !dto.TargetStoreCodes.Any())
            {
                yield return new CopyProgressDto
                {
                    EventType = "error",
                    Message = "请至少选择一个目标分店",
                    Timestamp = DateTime.UtcNow
                };
                yield break;
            }

            if (dto.TargetStoreCodes.Contains(dto.SourceStoreCode))
            {
                yield return new CopyProgressDto
                {
                    EventType = "error",
                    Message = "目标分店不能包含源分店",
                    Timestamp = DateTime.UtcNow
                };
                yield break;
            }

            _logger.LogInformation(
                "开始复制分店数据（SSE进度模式）：源分店={SourceStore}，目标分店={TargetStores}，模式={Mode}，操作人={User}",
                dto.SourceStoreCode,
                string.Join(",", dto.TargetStoreCodes),
                dto.Mode,
                updatedBy
            );

            const int sourcePageSize = 40000;
            const int mergeBatchSize = 10000;
            var db = _context.Db;

            var storeLimiter = new SemaphoreSlim(3, 3);
            var producerLimiter = new SemaphoreSlim(3, 3);
            var consumerLimiter = new SemaphoreSlim(5, 5);

            var progressChannel = Channel.CreateBounded<CopyProgressDto>(
                new BoundedChannelOptions(50)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                }
            );

            int totalRetail = 0;
            int totalMulti = 0;
            int storeIndex = 0;

            var processingTask = Task.Run(async () =>
            {
                foreach (var targetStore in dto.TargetStoreCodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await storeLimiter.WaitAsync(cancellationToken);
                    var store = targetStore;
                    storeIndex++;

                    await progressChannel.Writer.WriteAsync(new CopyProgressDto
                    {
                        EventType = "store_started",
                        StoreCode = store,
                        StoreIndex = storeIndex,
                        TotalStores = dto.TargetStoreCodes.Count,
                        Message = $"开始处理分店 {store}...",
                        Timestamp = DateTime.UtcNow
                    }, cancellationToken);

                    try
                    {
                        var progress = new Progress<CopyProgressDto>(p =>
                        {
                            p.StoreIndex = storeIndex;
                            p.TotalStores = dto.TargetStoreCodes.Count;
                            progressChannel.Writer.TryWrite(p);
                        });

                        var retailTask = ProcessStoreRetailPricePipelineAsync(
                            db.CopyNew(),
                            dto.SourceStoreCode,
                            store,
                            dto,
                            updatedBy,
                            sourcePageSize,
                            mergeBatchSize,
                            producerLimiter,
                            consumerLimiter,
                            progress,
                            cancellationToken
                        );

                        var multiTask = dto.SyncMultiCode
                            ? ProcessStoreMultiCodePipelineAsync(
                                db.CopyNew(),
                                dto.SourceStoreCode,
                                store,
                                dto,
                                updatedBy,
                                sourcePageSize,
                                mergeBatchSize,
                                producerLimiter,
                                consumerLimiter,
                                progress,
                                cancellationToken
                            )
                            : Task.FromResult(0);

                        await Task.WhenAll(retailTask, multiTask);

                        int retailCount = retailTask.Result;
                        int multiCount = multiTask.Result;
                        totalRetail += retailCount;
                        totalMulti += multiCount;

                        await progressChannel.Writer.WriteAsync(new CopyProgressDto
                        {
                            EventType = "store_completed",
                            StoreCode = store,
                            StoreIndex = storeIndex,
                            TotalStores = dto.TargetStoreCodes.Count,
                            RetailPriceCopied = totalRetail,
                            MultiCodeCopied = totalMulti,
                            Message = $"分店 {store} 完成",
                            Timestamp = DateTime.UtcNow
                        }, cancellationToken);
                    }
                    finally
                    {
                        storeLimiter.Release();
                    }
                }

                progressChannel.Writer.Complete();
            }, cancellationToken);

            await foreach (var progress in progressChannel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return progress;
            }

            await processingTask;

            yield return new CopyProgressDto
            {
                EventType = "completed",
                TotalStores = dto.TargetStoreCodes.Count,
                RetailPriceCopied = totalRetail,
                MultiCodeCopied = totalMulti,
                Message = $"全部完成：零售价 {totalRetail} 条，多码 {totalMulti} 条",
                Timestamp = DateTime.UtcNow
            };
        }

        private async Task<int> ProcessStoreRetailPricePipelineAsync(
            ISqlSugarClient db,
            string sourceStoreCode,
            string targetStoreCode,
            CopyStoreDataDto dto,
            string updatedBy,
            int sourcePageSize,
            int mergeBatchSize,
            SemaphoreSlim producerLimiter,
            SemaphoreSlim consumerLimiter,
            IProgress<CopyProgressDto>? progress,
            CancellationToken cancellationToken = default
        )
        {
            var channel = Channel.CreateBounded<List<StoreRetailPrice>>(
                new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                }
            );

            var producerTask = Task.Run(async () =>
            {
                var producerDb = db.CopyNew();
                try
                {
                    int pageIndex = 0;
                    while (true)
                    {
                        await producerLimiter.WaitAsync();
                        List<StoreRetailPrice> page;
                        try
                        {
                            page = await producerDb
                                .Queryable<StoreRetailPrice>()
                                .With(SqlWith.NoLock)
                                .Where(x => x.StoreCode == sourceStoreCode && x.IsDeleted == false)
                                .Skip(pageIndex * sourcePageSize)
                                .Take(sourcePageSize)
                                .ToListAsync();
                        }
                        finally
                        {
                            producerLimiter.Release();
                        }
                        if (page == null || !page.Any())
                            break;
                        await channel.Writer.WriteAsync(page);
                        _logger.LogDebug(
                            "[零售价-Pipeline] 源分店 {Source} → 目标 {Target}：第 {Page} 页加载 {Count} 条",
                            sourceStoreCode,
                            targetStoreCode,
                            pageIndex + 1,
                            page.Count
                        );
                        if (page.Count < sourcePageSize)
                            break;
                        pageIndex++;
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            });

            var consumerTask = Task.Run(async () =>
            {
                var consumerDb = db.CopyNew();
                var targetDict = new Dictionary<string, StoreRetailPrice>();
                int tPageIndex = 0;
                while (true)
                {
                    await producerLimiter.WaitAsync();
                    List<StoreRetailPrice> targetPage;
                    try
                    {
                        targetPage = await consumerDb
                            .Queryable<StoreRetailPrice>()
                            .With(SqlWith.NoLock)
                            .Where(x => x.StoreCode == targetStoreCode && x.IsDeleted == false)
                            .Skip(tPageIndex * sourcePageSize)
                            .Take(sourcePageSize)
                            .ToListAsync();
                    }
                    finally
                    {
                        producerLimiter.Release();
                    }
                    if (targetPage == null || !targetPage.Any())
                        break;
                    foreach (var item in targetPage)
                    {
                        if (string.IsNullOrEmpty(item.ProductCode))
                            continue;
                        if (!targetDict.ContainsKey(item.ProductCode))
                            targetDict[item.ProductCode] = item;
                    }
                    if (targetPage.Count < sourcePageSize)
                        break;
                    tPageIndex++;
                }

                _logger.LogInformation(
                    "[零售价-Pipeline] 目标分店 {Target} 已加载 {Count} 条数据",
                    targetStoreCode,
                    targetDict.Count
                );

                var toMerge = new List<StoreRetailPrice>();
                int totalCopied = 0;
                int batchCount = 0;

                await foreach (var sourcePage in channel.Reader.ReadAllAsync())
                {
                    foreach (var source in sourcePage)
                    {
                        if (string.IsNullOrEmpty(source.ProductCode))
                            continue;

                        if (targetDict.TryGetValue(source.ProductCode, out var target))
                        {
                            var mergeItem = CloneStoreRetailPrice(target);
                            bool needMerge = false;

                            if (dto.SyncPurchasePrice && source.PurchasePrice.HasValue)
                            {
                                if (dto.Mode == "Overwrite" || !target.PurchasePrice.HasValue)
                                {
                                    mergeItem.PurchasePrice = source.PurchasePrice;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncRetailPrice && source.StoreRetailPriceValue.HasValue)
                            {
                                if (
                                    dto.Mode == "Overwrite"
                                    || !target.StoreRetailPriceValue.HasValue
                                )
                                {
                                    mergeItem.StoreRetailPriceValue = source.StoreRetailPriceValue;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncIsAutoPricing)
                            {
                                if (dto.Mode == "Overwrite" || target.IsAutoPricing == false)
                                {
                                    mergeItem.IsAutoPricing = source.IsAutoPricing;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncIsSpecialProduct)
                            {
                                if (dto.Mode == "Overwrite" || target.IsSpecialProduct == false)
                                {
                                    mergeItem.IsSpecialProduct = source.IsSpecialProduct;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncDiscountRate && source.DiscountRate.HasValue)
                            {
                                if (dto.Mode == "Overwrite" || !target.DiscountRate.HasValue)
                                {
                                    mergeItem.DiscountRate = source.DiscountRate;
                                    needMerge = true;
                                }
                            }

                            if (needMerge)
                            {
                                mergeItem.UpdatedAt = DateTime.UtcNow;
                                mergeItem.UpdatedBy = updatedBy;
                                toMerge.Add(mergeItem);
                            }
                        }
                        else
                        {
                            toMerge.Add(
                                new StoreRetailPrice
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = targetStoreCode,
                                    ProductCode = source.ProductCode,
                                    StoreProductCode = targetStoreCode + source.ProductCode,
                                    SupplierCode = source.SupplierCode,
                                    PurchasePrice = dto.SyncPurchasePrice
                                        ? source.PurchasePrice
                                        : null,
                                    StoreRetailPriceValue = dto.SyncRetailPrice
                                        ? source.StoreRetailPriceValue
                                        : null,
                                    DiscountRate = dto.SyncDiscountRate
                                        ? source.DiscountRate
                                        : null,
                                    IsAutoPricing = dto.SyncIsAutoPricing
                                        ? source.IsAutoPricing
                                        : false,
                                    IsSpecialProduct = dto.SyncIsSpecialProduct
                                        ? source.IsSpecialProduct
                                        : false,
                                    IsActive = true,
                                    IsDeleted = false,
                                    CreatedAt = DateTime.UtcNow,
                                    CreatedBy = updatedBy,
                                    UpdatedAt = DateTime.UtcNow,
                                    UpdatedBy = updatedBy,
                                }
                            );
                        }
                    }

                    if (toMerge.Count >= mergeBatchSize)
                    {
                        var batch = toMerge.Take(mergeBatchSize).ToList();
                        toMerge = toMerge.Skip(mergeBatchSize).ToList();
                        await consumerLimiter.WaitAsync();
                        try
                        {
                            var batchDb = db.CopyNew();
                            await batchDb.Ado.BeginTranAsync();
                            try
                            {
                                int count = await batchDb
                                    .Fastest<StoreRetailPrice>()
                                    .PageSize(mergeBatchSize)
                                    .BulkMergeAsync(batch);
                                await batchDb.Ado.CommitTranAsync();
                                totalCopied += count;
                                _logger.LogDebug(
                                    "[零售价-Pipeline] 目标 {Target} 批量合并 {Count} 条",
                                    targetStoreCode,
                                    count
                                );
                                batchCount++;
                                progress?.Report(new CopyProgressDto
                                {
                                    EventType = "batch_completed",
                                    StoreCode = targetStoreCode,
                                    RetailPriceCopied = totalCopied,
                                    BatchCount = batchCount,
                                    Message = $"[零售价] 批量写入 {count} 条 (累计 {totalCopied})",
                                    Timestamp = DateTime.UtcNow
                                });
                            }
                            catch (Exception)
                            {
                                await batchDb.Ado.RollbackTranAsync();
                                throw;
                            }
                        }
                        finally
                        {
                            consumerLimiter.Release();
                        }
                    }
                }

                if (toMerge.Any())
                {
                    await consumerLimiter.WaitAsync();
                    try
                    {
                        var batchDb = db.CopyNew();
                        await batchDb.Ado.BeginTranAsync();
                        try
                        {
                            int count = await batchDb
                                .Fastest<StoreRetailPrice>()
                                .PageSize(mergeBatchSize)
                                .BulkMergeAsync(toMerge);
                            await batchDb.Ado.CommitTranAsync();
                            totalCopied += count;
                            batchCount++;
                            progress?.Report(new CopyProgressDto
                            {
                                EventType = "batch_completed",
                                StoreCode = targetStoreCode,
                                RetailPriceCopied = totalCopied,
                                BatchCount = batchCount,
                                Message = $"[零售价] 最终批量写入 {count} 条",
                                Timestamp = DateTime.UtcNow
                            });
                        }
                        catch (Exception)
                        {
                            await batchDb.Ado.RollbackTranAsync();
                            throw;
                        }
                    }
                    finally
                    {
                        consumerLimiter.Release();
                    }
                }

                _logger.LogInformation(
                    "[零售价-Pipeline] 目标分店 {Target} 处理完成，总计 {Count} 条",
                    targetStoreCode,
                    totalCopied
                );
                progress?.Report(new CopyProgressDto
                {
                    EventType = "store_completed",
                    StoreCode = targetStoreCode,
                    RetailPriceCopied = totalCopied,
                    Message = $"[零售价] 分店 {targetStoreCode} 完成，共 {totalCopied} 条",
                    Timestamp = DateTime.UtcNow
                });
                return totalCopied;
            });

            await Task.WhenAll(producerTask, consumerTask);
            return consumerTask.Result;
        }

        private async Task<int> ProcessStoreMultiCodePipelineAsync(
            ISqlSugarClient db,
            string sourceStoreCode,
            string targetStoreCode,
            CopyStoreDataDto dto,
            string updatedBy,
            int sourcePageSize,
            int mergeBatchSize,
            SemaphoreSlim producerLimiter,
            SemaphoreSlim consumerLimiter,
            IProgress<CopyProgressDto>? progress,
            CancellationToken cancellationToken = default
        )
        {
            var channel = Channel.CreateBounded<List<StoreMultiCodeProduct>>(
                new BoundedChannelOptions(2)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                }
            );

            var producerTask = Task.Run(async () =>
            {
                var producerDb = db.CopyNew();
                try
                {
                    int pageIndex = 0;
                    while (true)
                    {
                        await producerLimiter.WaitAsync();
                        List<StoreMultiCodeProduct> page;
                        try
                        {
                            page = await producerDb
                                .Queryable<StoreMultiCodeProduct>()
                                .With(SqlWith.NoLock)
                                .Where(x => x.StoreCode == sourceStoreCode && x.IsDeleted == false)
                                .Skip(pageIndex * sourcePageSize)
                                .Take(sourcePageSize)
                                .ToListAsync();
                        }
                        finally
                        {
                            producerLimiter.Release();
                        }
                        if (page == null || !page.Any())
                            break;
                        await channel.Writer.WriteAsync(page);
                        _logger.LogDebug(
                            "[多码-Pipeline] 源分店 {Source} → 目标 {Target}：第 {Page} 页加载 {Count} 条",
                            sourceStoreCode,
                            targetStoreCode,
                            pageIndex + 1,
                            page.Count
                        );
                        if (page.Count < sourcePageSize)
                            break;
                        pageIndex++;
                    }
                }
                finally
                {
                    channel.Writer.Complete();
                }
            });

            var consumerTask = Task.Run(async () =>
            {
                var consumerDb = db.CopyNew();
                var targetDict = new Dictionary<string, StoreMultiCodeProduct>();
                int tPageIndex = 0;
                while (true)
                {
                    await producerLimiter.WaitAsync();
                    List<StoreMultiCodeProduct> targetPage;
                    try
                    {
                        targetPage = await consumerDb
                            .Queryable<StoreMultiCodeProduct>()
                            .With(SqlWith.NoLock)
                            .Where(x => x.StoreCode == targetStoreCode && x.IsDeleted == false)
                            .Skip(tPageIndex * sourcePageSize)
                            .Take(sourcePageSize)
                            .ToListAsync();
                    }
                    finally
                    {
                        producerLimiter.Release();
                    }
                    if (targetPage == null || !targetPage.Any())
                        break;
                    foreach (var item in targetPage)
                    {
                        if (string.IsNullOrEmpty(item.ProductCode))
                            continue;
                        var key = $"{item.ProductCode}_{item.MultiCodeProductCode}";
                        if (!targetDict.ContainsKey(key))
                            targetDict[key] = item;
                    }
                    if (targetPage.Count < sourcePageSize)
                        break;
                    tPageIndex++;
                }

                _logger.LogInformation(
                    "[多码-Pipeline] 目标分店 {Target} 已加载 {Count} 条数据",
                    targetStoreCode,
                    targetDict.Count
                );

                var toMerge = new List<StoreMultiCodeProduct>();
                int totalCopied = 0;
                int batchCount = 0;

                await foreach (var sourcePage in channel.Reader.ReadAllAsync())
                {
                    foreach (var source in sourcePage)
                    {
                        if (string.IsNullOrEmpty(source.ProductCode))
                            continue;

                        var key = $"{source.ProductCode}_{source.MultiCodeProductCode}";
                        if (targetDict.TryGetValue(key, out var target))
                        {
                            var mergeItem = CloneStoreMultiCode(target);
                            bool needMerge = false;

                            if (dto.SyncPurchasePrice && source.PurchasePrice.HasValue)
                            {
                                if (dto.Mode == "Overwrite" || !target.PurchasePrice.HasValue)
                                {
                                    mergeItem.PurchasePrice = source.PurchasePrice;
                                    needMerge = true;
                                }
                            }
                            if (
                                dto.SyncMultiCodeRetailPrice && source.MultiCodeRetailPrice.HasValue
                            )
                            {
                                if (
                                    dto.Mode == "Overwrite"
                                    || !target.MultiCodeRetailPrice.HasValue
                                )
                                {
                                    mergeItem.MultiCodeRetailPrice = source.MultiCodeRetailPrice;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncDiscountRate && source.DiscountRate.HasValue)
                            {
                                if (dto.Mode == "Overwrite" || !target.DiscountRate.HasValue)
                                {
                                    mergeItem.DiscountRate = source.DiscountRate;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncIsAutoPricing)
                            {
                                if (dto.Mode == "Overwrite" || target.IsAutoPricing == false)
                                {
                                    mergeItem.IsAutoPricing = source.IsAutoPricing;
                                    needMerge = true;
                                }
                            }
                            if (dto.SyncIsSpecialProduct)
                            {
                                if (dto.Mode == "Overwrite" || target.IsSpecialProduct == false)
                                {
                                    mergeItem.IsSpecialProduct = source.IsSpecialProduct;
                                    needMerge = true;
                                }
                            }

                            if (needMerge)
                            {
                                mergeItem.UpdatedAt = DateTime.UtcNow;
                                mergeItem.UpdatedBy = updatedBy;
                                toMerge.Add(mergeItem);
                            }
                        }
                        else
                        {
                            toMerge.Add(
                                new StoreMultiCodeProduct
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = targetStoreCode,
                                    ProductCode = source.ProductCode,
                                    MultiCodeProductCode = source.MultiCodeProductCode,
                                    StoreMultiCodeProductCode = targetStoreCode + source.MultiCodeProductCode,
                                    MultiBarcode = source.MultiBarcode,
                                    PurchasePrice = dto.SyncPurchasePrice
                                        ? source.PurchasePrice
                                        : null,
                                    MultiCodeRetailPrice = dto.SyncMultiCodeRetailPrice
                                        ? source.MultiCodeRetailPrice
                                        : null,
                                    DiscountRate = dto.SyncDiscountRate
                                        ? source.DiscountRate
                                        : null,
                                    IsAutoPricing = dto.SyncIsAutoPricing
                                        ? source.IsAutoPricing
                                        : false,
                                    IsSpecialProduct = dto.SyncIsSpecialProduct
                                        ? source.IsSpecialProduct
                                        : false,
                                    IsActive = true,
                                    IsDeleted = false,
                                    CreatedAt = DateTime.UtcNow,
                                    CreatedBy = updatedBy,
                                    UpdatedAt = DateTime.UtcNow,
                                    UpdatedBy = updatedBy,
                                }
                            );
                        }
                    }

                    if (toMerge.Count >= mergeBatchSize)
                    {
                        var batch = toMerge.Take(mergeBatchSize).ToList();
                        toMerge = toMerge.Skip(mergeBatchSize).ToList();
                        await consumerLimiter.WaitAsync();
                        try
                        {
                            var batchDb = db.CopyNew();
                            await batchDb.Ado.BeginTranAsync();
                            try
                            {
                                int count = await batchDb
                                    .Fastest<StoreMultiCodeProduct>()
                                    .PageSize(mergeBatchSize)
                                    .BulkMergeAsync(batch);
                                await batchDb.Ado.CommitTranAsync();
                                totalCopied += count;
                                _logger.LogDebug(
                                    "[多码-Pipeline] 目标 {Target} 批量合并 {Count} 条",
                                    targetStoreCode,
                                    count
                                );
                                batchCount++;
                                progress?.Report(new CopyProgressDto
                                {
                                    EventType = "batch_completed",
                                    StoreCode = targetStoreCode,
                                    MultiCodeCopied = totalCopied,
                                    BatchCount = batchCount,
                                    Message = $"[多码] 批量写入 {count} 条 (累计 {totalCopied})",
                                    Timestamp = DateTime.UtcNow
                                });
                            }
                            catch (Exception)
                            {
                                await batchDb.Ado.RollbackTranAsync();
                                throw;
                            }
                        }
                        finally
                        {
                            consumerLimiter.Release();
                        }
                    }
                }

                if (toMerge.Any())
                {
                    await consumerLimiter.WaitAsync();
                    try
                    {
                        var batchDb = db.CopyNew();
                        await batchDb.Ado.BeginTranAsync();
                        try
                        {
                            int count = await batchDb
                                .Fastest<StoreMultiCodeProduct>()
                                .PageSize(mergeBatchSize)
                                .BulkMergeAsync(toMerge);
                            await batchDb.Ado.CommitTranAsync();
                            totalCopied += count;
                            batchCount++;
                            progress?.Report(new CopyProgressDto
                            {
                                EventType = "batch_completed",
                                StoreCode = targetStoreCode,
                                MultiCodeCopied = totalCopied,
                                BatchCount = batchCount,
                                Message = $"[多码] 最终批量写入 {count} 条",
                                Timestamp = DateTime.UtcNow
                            });
                        }
                        catch (Exception)
                        {
                            await batchDb.Ado.RollbackTranAsync();
                            throw;
                        }
                    }
                    finally
                    {
                        consumerLimiter.Release();
                    }
                }

                _logger.LogInformation(
                    "[多码-Pipeline] 目标分店 {Target} 处理完成，总计 {Count} 条",
                    targetStoreCode,
                    totalCopied
                );
                progress?.Report(new CopyProgressDto
                {
                    EventType = "store_completed",
                    StoreCode = targetStoreCode,
                    MultiCodeCopied = totalCopied,
                    Message = $"[多码] 分店 {targetStoreCode} 完成，共 {totalCopied} 条",
                    Timestamp = DateTime.UtcNow
                });
                return totalCopied;
            });

            await Task.WhenAll(producerTask, consumerTask);
            return consumerTask.Result;
        }

        private StoreRetailPrice CloneStoreRetailPrice(StoreRetailPrice source)
        {
            return new StoreRetailPrice
            {
                UUID = source.UUID,
                StoreCode = source.StoreCode,
                ProductCode = source.ProductCode,
                StoreProductCode = source.StoreProductCode,
                SupplierCode = source.SupplierCode,
                PurchasePrice = source.PurchasePrice,
                StoreRetailPriceValue = source.StoreRetailPriceValue,
                DiscountRate = source.DiscountRate,
                IsAutoPricing = source.IsAutoPricing,
                IsSpecialProduct = source.IsSpecialProduct,
                IsActive = source.IsActive,
                IsDeleted = source.IsDeleted,
                CreatedAt = source.CreatedAt,
                CreatedBy = source.CreatedBy,
                UpdatedAt = source.UpdatedAt,
                UpdatedBy = source.UpdatedBy,
            };
        }

        private StoreMultiCodeProduct CloneStoreMultiCode(StoreMultiCodeProduct source)
        {
            return new StoreMultiCodeProduct
            {
                UUID = source.UUID,
                StoreCode = source.StoreCode,
                ProductCode = source.ProductCode,
                MultiCodeProductCode = source.MultiCodeProductCode,
                StoreMultiCodeProductCode = source.StoreMultiCodeProductCode,
                MultiBarcode = source.MultiBarcode,
                PurchasePrice = source.PurchasePrice,
                MultiCodeRetailPrice = source.MultiCodeRetailPrice,
                DiscountRate = source.DiscountRate,
                IsAutoPricing = source.IsAutoPricing,
                IsSpecialProduct = source.IsSpecialProduct,
                IsActive = source.IsActive,
                IsDeleted = source.IsDeleted,
                CreatedAt = source.CreatedAt,
                CreatedBy = source.CreatedBy,
                UpdatedAt = source.UpdatedAt,
                UpdatedBy = source.UpdatedBy,
            };
        }
    }
}
