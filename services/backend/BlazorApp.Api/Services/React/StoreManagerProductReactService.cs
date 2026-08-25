using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    public class StoreManagerProductReactService : IStoreManagerProductReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<StoreManagerProductReactService> _logger;

        public StoreManagerProductReactService(
            SqlSugarContext context,
            ILogger<StoreManagerProductReactService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ApiResponse<List<StoreDto>>> GetAuthorizedStoresAsync(string userGuid)
        {
            try
            {
                var db = _context.Db;

                var storeCodes = await db.Queryable<UserStore>()
                    .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                    .Where((us, s) => us.UserGUID == userGuid && us.IsDeleted == false)
                    .Select((us, s) => s.StoreCode)
                    .Distinct()
                    .ToListAsync();

                if (!storeCodes.Any())
                {
                    return ApiResponse<List<StoreDto>>.Error("没有找到关联的分店");
                }

                var stores = await db.Queryable<Store>()
                    .Where(s => storeCodes.Contains(s.StoreCode) && s.IsDeleted == false)
                    .Select(s => new StoreDto
                    {
                        StoreCode = s.StoreCode,
                        StoreName = s.StoreName,
                        TimeZoneId = s.TimeZoneId,
                        IsActive = s.IsActive,
                    })
                    .ToListAsync();

                return ApiResponse<List<StoreDto>>.OK(stores);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取店长有权限的分店列表失败");
                return ApiResponse<List<StoreDto>>.Error($"获取分店列表失败: {ex.Message}");
            }
        }

        public async Task<
            StoreManagerPagedListDto<StoreManagerProductListItemDto>
        > GetProductPagedListAsync(StoreManagerProductFilterDto filter)
        {
            var db = _context.Db;
            var sw = Stopwatch.StartNew();

            var query = db.Queryable<Product>()
                .InnerJoin<StoreRetailPrice>((p, srp) => p.ProductCode == srp.ProductCode)
                .InnerJoin<HBLocalSupplier>(
                    (p, srp, ls) => srp.SupplierCode == ls.LocalSupplierCode
                )
                .Where((p, srp, ls) => filter.StoreCodes!.Contains(srp.StoreCode!));

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var keyword = filter.Search.Trim();
                query = query.Where(
                    (p, srp, ls) =>
                        (p.ProductName != null && p.ProductName.Contains(keyword))
                        || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        || (p.Barcode != null && p.Barcode.Contains(keyword))
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.SupplierName))
            {
                var supplierName = filter.SupplierName.Trim();
                query = query.Where(
                    (p, srp, ls) => ls.Name != null && ls.Name.Contains(supplierName)
                );
            }

            if (filter.IsAutoPricing.HasValue)
            {
                query = query.Where(
                    (p, srp, ls) => srp.IsAutoPricing == filter.IsAutoPricing.Value
                );
            }

            if (filter.MinPurchasePrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.PurchasePrice >= filter.MinPurchasePrice.Value
                );
            if (filter.MaxPurchasePrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.PurchasePrice <= filter.MaxPurchasePrice.Value
                );
            if (filter.MinRetailPrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.StoreRetailPriceValue >= filter.MinRetailPrice.Value
                );
            if (filter.MaxRetailPrice.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.StoreRetailPriceValue <= filter.MaxRetailPrice.Value
                );
            if (filter.MinDiscountRate.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.DiscountRate >= filter.MinDiscountRate.Value
                );
            if (filter.MaxDiscountRate.HasValue)
                query = query.Where(
                    (p, srp, ls) => srp.DiscountRate <= filter.MaxDiscountRate.Value
                );

            if (!string.IsNullOrWhiteSpace(filter.SortBy))
            {
                var isDesc = filter.SortOrder?.ToLower() == "desc";
                query = filter.SortBy.ToLower() switch
                {
                    "purchaseprice" => query.OrderBy(
                        (p, srp, ls) => srp.PurchasePrice,
                        isDesc ? OrderByType.Desc : OrderByType.Asc
                    ),
                    "retailprice" => query.OrderBy(
                        (p, srp, ls) => srp.StoreRetailPriceValue,
                        isDesc ? OrderByType.Desc : OrderByType.Asc
                    ),
                    "discountrate" => query.OrderBy(
                        (p, srp, ls) => srp.DiscountRate,
                        isDesc ? OrderByType.Desc : OrderByType.Asc
                    ),
                    _ => query.OrderBy((p, srp, ls) => p.ProductName, OrderByType.Asc),
                };
            }
            else
            {
                query = query.OrderBy((p, srp, ls) => p.ProductName, OrderByType.Asc);
            }

            var total = await query.CountAsync();
            var items = await query
                .Select(
                    (p, srp, ls) =>
                        new StoreManagerProductListItemDto
                        {
                            ProductCode = p.ProductCode!,
                            ProductName = p.ProductName!,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            ProductImage = p.ProductImage,
                        }
                )
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();

            return new StoreManagerPagedListDto<StoreManagerProductListItemDto>
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        public async Task<ApiResponse<StoreManagerProductDetailDto>> GetProductDetailAsync(
            string productCode,
            List<string> authorizedStoreCodes
        )
        {
            try
            {
                var db = _context.Db;

                var product = await db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .Select<StoreManagerProductListItemDto>(p => new StoreManagerProductListItemDto
                    {
                        ProductCode = p.ProductCode!,
                        ProductName = p.ProductName!,
                        ItemNumber = p.ItemNumber,
                        Barcode = p.Barcode,
                        ProductImage = p.ProductImage,
                    })
                    .FirstAsync();

                if (product == null)
                {
                    return ApiResponse<StoreManagerProductDetailDto>.Error("商品不存在");
                }

                var storePrices = await db.Queryable<StoreRetailPrice>()
                    .LeftJoin<Store>((srp, s) => srp.StoreCode == s.StoreCode)
                    .Where(
                        (srp, s) =>
                            srp.ProductCode == productCode
                            && authorizedStoreCodes.Contains(srp.StoreCode!)
                            && srp.IsDeleted == false
                            && s.IsDeleted == false
                    )
                    .Select<StoreManagerStorePriceDto>(
                        (srp, s) =>
                            new StoreManagerStorePriceDto
                            {
                                UUID = srp.UUID,
                                StoreCode = srp.StoreCode!,
                                StoreName = s.StoreName!,
                                ProductCode = srp.ProductCode!,
                                PurchasePrice = srp.PurchasePrice,
                                StoreRetailPriceValue = srp.StoreRetailPriceValue,
                                IsAutoPricing = srp.IsAutoPricing,
                            }
                    )
                    .ToListAsync();

                var multiCodePrices = await db.Queryable<StoreMultiCodeProduct>()
                    .LeftJoin<Store>((smcp, s) => smcp.StoreCode == s.StoreCode)
                    .Where(
                        (smcp, s) =>
                            smcp.ProductCode == productCode
                            && authorizedStoreCodes.Contains(smcp.StoreCode!)
                            && smcp.IsDeleted == false
                            && s.IsDeleted == false
                    )
                    .Select<StoreManagerMultiCodePriceDto>(
                        (smcp, s) =>
                            new StoreManagerMultiCodePriceDto
                            {
                                UUID = smcp.UUID,
                                StoreCode = smcp.StoreCode!,
                                ProductCode = smcp.ProductCode!,
                                MultiBarcode = smcp.MultiBarcode,
                                PurchasePrice = smcp.PurchasePrice,
                                MultiCodeRetailPrice = smcp.MultiCodeRetailPrice,
                                IsAutoPricing = smcp.IsAutoPricing,
                            }
                    )
                    .ToListAsync();

                var detail = new StoreManagerProductDetailDto
                {
                    Product = product,
                    StorePrices = storePrices,
                    MultiCodePrices = multiCodePrices,
                };

                return ApiResponse<StoreManagerProductDetailDto>.OK(detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品详情失败: {ProductCode}", productCode);
                return ApiResponse<StoreManagerProductDetailDto>.Error(
                    $"获取商品详情失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<StoreManagerStorePriceDto>> UpdateStorePriceAsync(
            string uuid,
            StoreManagerUpdatePriceDto dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var exists = await db.Queryable<StoreRetailPrice>()
                    .Where(srp => srp.UUID == uuid && srp.IsDeleted == false)
                    .FirstAsync();

                if (exists == null)
                {
                    return ApiResponse<StoreManagerStorePriceDto>.Error("分店价格记录不存在");
                }

                var expectedStoreCode = exists.StoreCode;
                var expectedProductCode = exists.ProductCode;
                if (
                    string.IsNullOrWhiteSpace(expectedStoreCode)
                    || string.IsNullOrWhiteSpace(expectedProductCode)
                )
                {
                    return ApiResponse<StoreManagerStorePriceDto>.Error("分店价格记录缺少门店或商品编码");
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                        db,
                        new[] { expectedProductCode }
                    );

                    // 获取业务锁后必须复读，避免 UUID 在等待锁期间被转移到其他分组。
                    exists = await db.Queryable<StoreRetailPrice>()
                        .Where(srp => srp.UUID == uuid && srp.IsDeleted == false)
                        .FirstAsync();
                    if (
                        exists == null
                        || !string.Equals(exists.StoreCode, expectedStoreCode, StringComparison.Ordinal)
                        || !string.Equals(exists.ProductCode, expectedProductCode, StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidOperationException("分店价格记录在等待业务锁期间已变更");
                    }

                    var updatedAt = DateTime.UtcNow;
                    var update = db.Updateable<StoreRetailPrice>()
                        .Where(srp => srp.UUID == uuid && srp.IsDeleted == false);
                    if (dto.PurchasePrice.HasValue)
                        update = update.SetColumns(srp => srp.PurchasePrice == dto.PurchasePrice.Value);
                    if (dto.StoreRetailPriceValue.HasValue)
                        update = update.SetColumns(srp =>
                            srp.StoreRetailPriceValue == dto.StoreRetailPriceValue.Value
                        );
                    if (dto.IsAutoPricing.HasValue)
                        update = update.SetColumns(srp => srp.IsAutoPricing == dto.IsAutoPricing.Value);
                    await update
                        .SetColumns(srp => srp.UpdatedBy == updatedBy)
                        .SetColumns(srp => srp.UpdatedAt == updatedAt)
                        .ExecuteCommandAsync();

                    var writeback = await new SetChildPurchasePriceService(db)
                        .RecalculateStoresLockedAsync(
                            lockScope,
                            new[] { expectedProductCode },
                            new[] { expectedStoreCode },
                            updatedBy
                        );
                    if (writeback.StoreMultiCodeProduct.SkippedGroupCount > 0)
                    {
                        throw new InvalidOperationException("目标门店套装子项成本重算不完整");
                    }

                    exists = await db.Queryable<StoreRetailPrice>()
                        .Where(srp => srp.UUID == uuid && srp.IsDeleted == false)
                        .FirstAsync();
                    await db.Ado.CommitTranAsync();
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == exists.StoreCode)
                    .FirstAsync();

                var result = new StoreManagerStorePriceDto
                {
                    UUID = exists.UUID,
                    StoreCode = exists.StoreCode!,
                    StoreName = store?.StoreName ?? "",
                    ProductCode = exists.ProductCode!,
                    PurchasePrice = exists.PurchasePrice,
                    StoreRetailPriceValue = exists.StoreRetailPriceValue,
                    IsAutoPricing = exists.IsAutoPricing,
                };

                return ApiResponse<StoreManagerStorePriceDto>.OK(result);
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict)
            )
            {
                _logger.LogWarning(ex, "更新分店价格获取套装成本业务锁失败: {UUID}", uuid);
                return ApiResponse<StoreManagerStorePriceDto>.Error(
                    conflict!.Message,
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店价格失败: {UUID}", uuid);
                return ApiResponse<StoreManagerStorePriceDto>.Error(
                    $"更新分店价格失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<BatchOperationReactResult>> BatchUpdateStorePricesAsync(
            List<StoreManagerUpdatePriceDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var result = new BatchOperationReactResult
                {
                    SuccessCount = 0,
                    FailedCount = 0,
                    Errors = new List<string>(),
                };

                var resolvedItems = new List<(int Index, StoreManagerUpdatePriceDto Item, string StoreCode, string ProductCode)>();
                // 事务外只读解析 UUID，再按业务键分组，避免跨门店或跨商品共用事务。
                for (var index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    try
                    {
                        var exists = await db.Queryable<StoreRetailPrice>()
                            .Where(srp => srp.UUID == item.UUID && srp.IsDeleted == false)
                            .FirstAsync();

                        if (exists == null)
                        {
                            AddBatchFailure(result, item.UUID, $"UUID {item.UUID} 的记录不存在");
                            continue;
                        }

                        if (
                            string.IsNullOrWhiteSpace(exists.StoreCode)
                            || string.IsNullOrWhiteSpace(exists.ProductCode)
                        )
                        {
                            AddBatchFailure(
                                result,
                                item.UUID,
                                $"UUID {item.UUID} 的记录缺少门店或商品编码"
                            );
                            continue;
                        }

                        resolvedItems.Add((index, item, exists.StoreCode, exists.ProductCode));
                    }
                    catch (Exception ex)
                    {
                        AddBatchFailure(
                            result,
                            item.UUID,
                            $"UUID {item.UUID} 更新失败: {ex.Message}"
                        );
                    }
                }

                foreach (
                    var group in resolvedItems
                        .GroupBy(x => (x.StoreCode, x.ProductCode))
                        .OrderBy(x => x.Min(item => item.Index))
                )
                {
                    try
                    {
                        await db.Ado.BeginTranAsync();
                        try
                        {
                            var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                                db,
                                new[] { group.Key.ProductCode }
                            );

                            foreach (var entry in group.OrderBy(x => x.Index))
                            {
                                // 锁内复读并确认仍归属预读分组，防止等待锁时发生并发迁移。
                                var exists = await db.Queryable<StoreRetailPrice>()
                                    .Where(srp => srp.UUID == entry.Item.UUID && srp.IsDeleted == false)
                                    .FirstAsync();
                                if (
                                    exists == null
                                    || !string.Equals(exists.StoreCode, group.Key.StoreCode, StringComparison.Ordinal)
                                    || !string.Equals(exists.ProductCode, group.Key.ProductCode, StringComparison.Ordinal)
                                )
                                {
                                    throw new InvalidOperationException(
                                        $"UUID {entry.Item.UUID} 在等待业务锁期间已离开目标分组"
                                    );
                                }

                                var updatedAt = DateTime.UtcNow;
                                var update = db.Updateable<StoreRetailPrice>()
                                    .Where(srp => srp.UUID == entry.Item.UUID && srp.IsDeleted == false);
                                if (entry.Item.PurchasePrice.HasValue)
                                    update = update.SetColumns(srp =>
                                        srp.PurchasePrice == entry.Item.PurchasePrice.Value
                                    );
                                if (entry.Item.StoreRetailPriceValue.HasValue)
                                    update = update.SetColumns(srp =>
                                        srp.StoreRetailPriceValue
                                        == entry.Item.StoreRetailPriceValue.Value
                                    );
                                if (entry.Item.IsAutoPricing.HasValue)
                                    update = update.SetColumns(srp =>
                                        srp.IsAutoPricing == entry.Item.IsAutoPricing.Value
                                    );
                                await update
                                    .SetColumns(srp => srp.UpdatedBy == updatedBy)
                                    .SetColumns(srp => srp.UpdatedAt == updatedAt)
                                    .ExecuteCommandAsync();
                            }

                            var writeback = await new SetChildPurchasePriceService(db)
                                .RecalculateStoresLockedAsync(
                                    lockScope,
                                    new[] { group.Key.ProductCode },
                                    new[] { group.Key.StoreCode },
                                    updatedBy
                                );
                            if (writeback.StoreMultiCodeProduct.SkippedGroupCount > 0)
                            {
                                throw new InvalidOperationException("目标门店套装子项成本重算不完整");
                            }

                            await db.Ado.CommitTranAsync();
                            result.SuccessCount += group.Count();
                        }
                        catch
                        {
                            await db.Ado.RollbackTranAsync();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorCode = SetChildPurchasePriceMutationLock.TryResolveConflict(
                            ex,
                            out var conflict
                        )
                            ? SetChildPurchasePriceMutationLock.BusyErrorCode
                            : null;
                        var message = conflict?.Message ?? ex.Message;
                        foreach (var entry in group)
                        {
                            AddBatchFailure(
                                result,
                                entry.Item.UUID,
                                $"UUID {entry.Item.UUID} 更新失败: {message}",
                                errorCode
                            );
                        }
                    }
                }

                return BuildBatchResponse(result);
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict)
            )
            {
                _logger.LogWarning(ex, "批量更新分店价格获取套装成本业务锁失败");
                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = false,
                    ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
                    Message = conflict!.Message,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新分店价格失败");
                return ApiResponse<BatchOperationReactResult>.Error($"批量更新失败: {ex.Message}");
            }
        }

        public async Task<ApiResponse<StoreManagerMultiCodePriceDto>> UpdateMultiCodePriceAsync(
            string uuid,
            StoreManagerUpdateMultiCodePriceDto dto,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var exists = await db.Queryable<StoreMultiCodeProduct>()
                    .Where(smcp => smcp.UUID == uuid && smcp.IsDeleted == false)
                    .FirstAsync();

                if (exists == null)
                {
                    return ApiResponse<StoreManagerMultiCodePriceDto>.Error("多码价格记录不存在");
                }

                var expectedStoreCode = exists.StoreCode;
                var expectedProductCode = exists.ProductCode;
                if (
                    string.IsNullOrWhiteSpace(expectedStoreCode)
                    || string.IsNullOrWhiteSpace(expectedProductCode)
                )
                {
                    return ApiResponse<StoreManagerMultiCodePriceDto>.Error("多码价格记录缺少门店或商品编码");
                }

                await db.Ado.BeginTranAsync();
                try
                {
                    var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                        db,
                        new[] { expectedProductCode }
                    );

                    // 获取业务锁后必须复读，确保套装关系判断和写入属于同一业务分组。
                    exists = await db.Queryable<StoreMultiCodeProduct>()
                        .Where(smcp => smcp.UUID == uuid && smcp.IsDeleted == false)
                        .FirstAsync();
                    if (
                        exists == null
                        || !string.Equals(exists.StoreCode, expectedStoreCode, StringComparison.Ordinal)
                        || !string.Equals(exists.ProductCode, expectedProductCode, StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidOperationException("多码价格记录在等待业务锁期间已变更");
                    }

                    var isSetChild = await db.Queryable<ProductSetCode>()
                        .Where(x =>
                            x.ProductCode == expectedProductCode
                            && x.SetProductCode == exists.MultiCodeProductCode
                            && (x.SetType == 1 || x.SetType == 2)
                            && x.IsActive
                            && !x.IsDeleted
                        )
                        .AnyAsync();
                    var updatedAt = DateTime.UtcNow;
                    var update = db.Updateable<StoreMultiCodeProduct>()
                        .Where(smcp => smcp.UUID == uuid && smcp.IsDeleted == false);
                    // 两类套装子项成本均由锁内统一重算服务写回，不能接受客户端最终成本。
                    if (dto.PurchasePrice.HasValue && !isSetChild)
                        update = update.SetColumns(smcp =>
                            smcp.PurchasePrice == dto.PurchasePrice.Value
                        );
                    if (dto.MultiCodeRetailPrice.HasValue)
                        update = update.SetColumns(smcp =>
                            smcp.MultiCodeRetailPrice == dto.MultiCodeRetailPrice.Value
                        );
                    if (dto.IsAutoPricing.HasValue)
                        update = update.SetColumns(smcp => smcp.IsAutoPricing == dto.IsAutoPricing.Value);
                    await update
                        .SetColumns(smcp => smcp.UpdatedBy == updatedBy)
                        .SetColumns(smcp => smcp.UpdatedAt == updatedAt)
                        .ExecuteCommandAsync();

                    var writeback = await new SetChildPurchasePriceService(db)
                        .RecalculateStoresLockedAsync(
                            lockScope,
                            new[] { expectedProductCode },
                            new[] { expectedStoreCode },
                            updatedBy
                        );
                    if (writeback.StoreMultiCodeProduct.SkippedGroupCount > 0)
                    {
                        throw new InvalidOperationException("目标门店套装子项成本重算不完整");
                    }

                    exists = await db.Queryable<StoreMultiCodeProduct>()
                        .Where(smcp => smcp.UUID == uuid && smcp.IsDeleted == false)
                        .FirstAsync();
                    await db.Ado.CommitTranAsync();
                }
                catch
                {
                    await db.Ado.RollbackTranAsync();
                    throw;
                }

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == exists.StoreCode)
                    .FirstAsync();

                var result = new StoreManagerMultiCodePriceDto
                {
                    UUID = exists.UUID,
                    StoreCode = exists.StoreCode!,
                    ProductCode = exists.ProductCode!,
                    MultiBarcode = exists.MultiBarcode,
                    PurchasePrice = exists.PurchasePrice,
                    MultiCodeRetailPrice = exists.MultiCodeRetailPrice,
                    IsAutoPricing = exists.IsAutoPricing,
                };

                return ApiResponse<StoreManagerMultiCodePriceDto>.OK(result);
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict)
            )
            {
                _logger.LogWarning(ex, "更新多码价格获取套装成本业务锁失败: {UUID}", uuid);
                return ApiResponse<StoreManagerMultiCodePriceDto>.Error(
                    conflict!.Message,
                    SetChildPurchasePriceMutationLock.BusyErrorCode
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新多码价格失败: {UUID}", uuid);
                return ApiResponse<StoreManagerMultiCodePriceDto>.Error(
                    $"更新多码价格失败: {ex.Message}"
                );
            }
        }

        public async Task<ApiResponse<BatchOperationReactResult>> BatchUpdateMultiCodePricesAsync(
            List<StoreManagerUpdateMultiCodePriceDto> items,
            string updatedBy
        )
        {
            try
            {
                var db = _context.Db;

                var result = new BatchOperationReactResult
                {
                    SuccessCount = 0,
                    FailedCount = 0,
                    Errors = new List<string>(),
                };

                var resolvedItems = new List<(int Index, StoreManagerUpdateMultiCodePriceDto Item, string StoreCode, string ProductCode)>();
                // 事务外只读解析 UUID，再按门店和主商品拆分独立的一致性边界。
                for (var index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    try
                    {
                        var exists = await db.Queryable<StoreMultiCodeProduct>()
                            .Where(smcp => smcp.UUID == item.UUID && smcp.IsDeleted == false)
                            .FirstAsync();

                        if (exists == null)
                        {
                            AddBatchFailure(result, item.UUID, $"UUID {item.UUID} 的记录不存在");
                            continue;
                        }

                        if (
                            string.IsNullOrWhiteSpace(exists.StoreCode)
                            || string.IsNullOrWhiteSpace(exists.ProductCode)
                        )
                        {
                            AddBatchFailure(
                                result,
                                item.UUID,
                                $"UUID {item.UUID} 的记录缺少门店或商品编码"
                            );
                            continue;
                        }

                        resolvedItems.Add((index, item, exists.StoreCode, exists.ProductCode));
                    }
                    catch (Exception ex)
                    {
                        AddBatchFailure(
                            result,
                            item.UUID,
                            $"UUID {item.UUID} 更新失败: {ex.Message}"
                        );
                    }
                }

                foreach (
                    var group in resolvedItems
                        .GroupBy(x => (x.StoreCode, x.ProductCode))
                        .OrderBy(x => x.Min(item => item.Index))
                )
                {
                    try
                    {
                        await db.Ado.BeginTranAsync();
                        try
                        {
                            var lockScope = await SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                                db,
                                new[] { group.Key.ProductCode }
                            );

                            foreach (var entry in group.OrderBy(x => x.Index))
                            {
                                // 套装成本和请求字段必须在同一锁内读取、判断及写入。
                                var exists = await db.Queryable<StoreMultiCodeProduct>()
                                    .Where(smcp => smcp.UUID == entry.Item.UUID && smcp.IsDeleted == false)
                                    .FirstAsync();
                                if (
                                    exists == null
                                    || !string.Equals(exists.StoreCode, group.Key.StoreCode, StringComparison.Ordinal)
                                    || !string.Equals(exists.ProductCode, group.Key.ProductCode, StringComparison.Ordinal)
                                )
                                {
                                    throw new InvalidOperationException(
                                        $"UUID {entry.Item.UUID} 在等待业务锁期间已离开目标分组"
                                    );
                                }

                                var isSetChild = await db.Queryable<ProductSetCode>()
                                    .Where(x =>
                                        x.ProductCode == group.Key.ProductCode
                                        && x.SetProductCode == exists.MultiCodeProductCode
                                        && (x.SetType == 1 || x.SetType == 2)
                                        && x.IsActive
                                        && !x.IsDeleted
                                    )
                                    .AnyAsync();
                                var updatedAt = DateTime.UtcNow;
                                var update = db.Updateable<StoreMultiCodeProduct>()
                                    .Where(smcp =>
                                        smcp.UUID == entry.Item.UUID && smcp.IsDeleted == false
                                    );
                                // 两类套装子项均忽略提交成本；无套装关系的历史占位行保持原有逻辑。
                                if (entry.Item.PurchasePrice.HasValue && !isSetChild)
                                    update = update.SetColumns(smcp =>
                                        smcp.PurchasePrice == entry.Item.PurchasePrice.Value
                                    );
                                if (entry.Item.MultiCodeRetailPrice.HasValue)
                                    update = update.SetColumns(smcp =>
                                        smcp.MultiCodeRetailPrice
                                        == entry.Item.MultiCodeRetailPrice.Value
                                    );
                                if (entry.Item.IsAutoPricing.HasValue)
                                    update = update.SetColumns(smcp =>
                                        smcp.IsAutoPricing == entry.Item.IsAutoPricing.Value
                                    );
                                await update
                                    .SetColumns(smcp => smcp.UpdatedBy == updatedBy)
                                    .SetColumns(smcp => smcp.UpdatedAt == updatedAt)
                                    .ExecuteCommandAsync();
                            }

                            var writeback = await new SetChildPurchasePriceService(db)
                                .RecalculateStoresLockedAsync(
                                    lockScope,
                                    new[] { group.Key.ProductCode },
                                    new[] { group.Key.StoreCode },
                                    updatedBy
                                );
                            if (writeback.StoreMultiCodeProduct.SkippedGroupCount > 0)
                            {
                                throw new InvalidOperationException("目标门店套装子项成本重算不完整");
                            }

                            await db.Ado.CommitTranAsync();
                            result.SuccessCount += group.Count();
                        }
                        catch
                        {
                            await db.Ado.RollbackTranAsync();
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        var errorCode = SetChildPurchasePriceMutationLock.TryResolveConflict(
                            ex,
                            out var conflict
                        )
                            ? SetChildPurchasePriceMutationLock.BusyErrorCode
                            : null;
                        var message = conflict?.Message ?? ex.Message;
                        foreach (var entry in group)
                        {
                            AddBatchFailure(
                                result,
                                entry.Item.UUID,
                                $"UUID {entry.Item.UUID} 更新失败: {message}",
                                errorCode
                            );
                        }
                    }
                }

                return BuildBatchResponse(result);
            }
            catch (Exception ex) when (
                SetChildPurchasePriceMutationLock.TryResolveConflict(ex, out var conflict)
            )
            {
                _logger.LogWarning(ex, "批量更新多码价格获取套装成本业务锁失败");
                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = false,
                    ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
                    Message = conflict!.Message,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新多码价格失败");
                return ApiResponse<BatchOperationReactResult>.Error($"批量更新失败: {ex.Message}");
            }
        }

        private static void AddBatchFailure(
            BatchOperationReactResult result,
            string? itemKey,
            string message,
            string? errorCode = null
        )
        {
            result.FailedCount++;
            result.Errors.Add(message);
            result.FailureDetails.Add(new BatchOperationFailureDto
            {
                ItemKey = itemKey ?? string.Empty,
                Message = message,
                ErrorCode = errorCode,
            });
        }

        private static ApiResponse<BatchOperationReactResult> BuildBatchResponse(
            BatchOperationReactResult result
        )
        {
            var containsBusyFailure = result.FailureDetails.Any(failure =>
                string.Equals(
                    failure.ErrorCode,
                    SetChildPurchasePriceMutationLock.BusyErrorCode,
                    StringComparison.Ordinal
                )
            );
            if (result.SuccessCount == 0 && containsBusyFailure)
            {
                return new ApiResponse<BatchOperationReactResult>
                {
                    Success = false,
                    ErrorCode = SetChildPurchasePriceMutationLock.BusyErrorCode,
                    Message = "套装子项成本正在被其他操作更新，请稍后重试",
                    Data = result,
                };
            }

            return ApiResponse<BatchOperationReactResult>.OK(result);
        }
    }
}
