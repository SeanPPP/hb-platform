using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.Cart.Application.Ports;
using BlazorApp.Api.Features.StoreOrders.Cart.Domain;
using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Infrastructure;

internal sealed class SqlSugarStoreOrderCartStore(
    SqlSugarContext context,
    IStoreOrderActorContext actorContext
)
    : IStoreOrderCartQueryStore,
        IStoreOrderCartCommandStore,
        IStoreOrderCartPlacementPort
{
    private readonly ISqlSugarClient _db = context.Db;

    public async Task<StoreOrderCartDto?> GetFullAsync(StoreOrderCartScope scope)
    {
        var order = await FindActiveCartAsync(scope);
        if (order == null)
        {
            return null;
        }

        var store = await GetStoreByCodeOrGuidAsync(order.StoreCode);
        var details = await _db.Queryable<WareHouseOrderDetails>()
            .LeftJoin<Product>((detail, product) => detail.ProductCode == product.ProductCode)
            .LeftJoin<WarehouseProduct>(
                (detail, product, warehouseProduct) =>
                    detail.ProductCode == warehouseProduct.ProductCode
            )
            .LeftJoin<DomesticProduct>(
                (detail, product, warehouseProduct, domesticProduct) =>
                    warehouseProduct.ProductCode == domesticProduct.ProductCode
            )
            .LeftJoin<ProductGrade>(
                (detail, product, warehouseProduct, domesticProduct, grade) =>
                    detail.ProductCode == grade.ProductCode && !grade.IsDeleted
            )
            .Where(detail => detail.OrderGUID == order.OrderGUID && !detail.IsDeleted)
            .Select(
                (detail, product, warehouseProduct, domesticProduct, grade) =>
                    new StoreOrderCartItemDto
                    {
                        DetailGUID = detail.DetailGUID,
                        ProductCode = detail.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        Grade = grade.Grade,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        Price = detail.OEMPrice ?? 0,
                        Quantity = detail.Quantity ?? 0,
                        AllocQuantity = detail.AllocQuantity,
                        Amount = detail.OEMAmount ?? 0,
                        ImportPrice = detail.ImportPrice ?? (warehouseProduct.ImportPrice ?? 0),
                        ImportAmount =
                            detail.ImportAmount
                            ?? (
                                (detail.ImportPrice ?? (warehouseProduct.ImportPrice ?? 0))
                                * (detail.Quantity ?? 0)
                            ),
                        AllocatedImportAmount =
                            (detail.ImportPrice ?? (warehouseProduct.ImportPrice ?? 0))
                            * (detail.AllocQuantity ?? 0),
                        Volume = domesticProduct.PackingQuantity > 0
                            ? domesticProduct.UnitVolume / domesticProduct.PackingQuantity
                            : domesticProduct.UnitVolume,
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                    }
            )
            .ToListAsync();

        foreach (var item in details)
        {
            if (!item.Volume.HasValue)
            {
                continue;
            }

            item.OrderVolume = StoreOrderCartRules.CalculateVolume(item.Volume, item.Quantity);
            item.AllocVolume = StoreOrderCartRules.CalculateVolume(
                item.Volume,
                item.AllocQuantity ?? 0
            );
            item.TotalVolume = item.OrderVolume;
        }

        return new StoreOrderCartDto
        {
            OrderGUID = order.OrderGUID,
            OrderNo = order.OrderNo,
            StoreCode = order.StoreCode,
            TotalAmount = order.OEMTotalAmount ?? 0,
            TotalQuantity = (int)details.Sum(item => item.Quantity),
            TotalSKU = details
                .Select(item => item.ProductCode)
                .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                .Distinct()
                .Count(),
            TotalImportAmount = details.Sum(item => item.ImportAmount),
            TotalAllocatedImportAmount = details.Sum(item => item.AllocatedImportAmount),
            TotalVolume = details.Sum(item => item.TotalVolume ?? 0),
            TotalOrderVolume = details.Sum(item => item.OrderVolume ?? 0),
            TotalAllocVolume = details.Sum(item => item.AllocVolume ?? 0),
            Remarks = order.Remarks,
            StoreAddress = store?.Address,
            StoreContactEmail = store?.ContactEmail,
            ShippingFee = order.ShippingFee,
            OrderDate = order.OrderDate,
            TotalAllocQuantity = (int)details.Sum(item => item.AllocQuantity ?? 0),
            FlowStatus = order.FlowStatus,
            Items = details,
        };
    }

    public async Task<StoreOrderCartDto?> GetSummaryAsync(StoreOrderCartScope scope)
    {
        var order = await FindActiveCartAsync(scope);
        if (order == null)
        {
            return null;
        }

        var store = await GetStoreByCodeOrGuidAsync(order.StoreCode);
        var detailRows = await _db.Queryable<WareHouseOrderDetails>()
            .LeftJoin<DomesticProduct>(
                (detail, domesticProduct) =>
                    detail.ProductCode == domesticProduct.ProductCode
            )
            .Where(detail => detail.OrderGUID == order.OrderGUID && !detail.IsDeleted)
            .Select(
                (detail, domesticProduct) =>
                    new CartSummaryReadRow
                    {
                        ProductCode = detail.ProductCode,
                        Quantity = detail.Quantity ?? 0,
                        AllocQuantity = detail.AllocQuantity ?? 0,
                        ImportAmount =
                            detail.ImportAmount
                            ?? ((detail.ImportPrice ?? 0) * (detail.Quantity ?? 0)),
                        AllocatedImportAmount =
                            (detail.ImportPrice ?? 0) * (detail.AllocQuantity ?? 0),
                        UnitVolume = domesticProduct.PackingQuantity > 0
                            ? domesticProduct.UnitVolume / domesticProduct.PackingQuantity
                            : domesticProduct.UnitVolume,
                    }
            )
            .ToListAsync();

        var totalVolume = detailRows.Sum(row => (row.UnitVolume ?? 0) * row.Quantity);
        var totalAllocVolume = detailRows.Sum(
            row => (row.UnitVolume ?? 0) * row.AllocQuantity
        );

        return new StoreOrderCartDto
        {
            OrderGUID = order.OrderGUID,
            OrderNo = order.OrderNo,
            StoreCode = order.StoreCode,
            TotalAmount = order.OEMTotalAmount ?? 0,
            TotalQuantity = (int)detailRows.Sum(row => row.Quantity),
            TotalImportAmount = detailRows.Sum(row => row.ImportAmount),
            TotalAllocatedImportAmount = detailRows.Sum(row => row.AllocatedImportAmount),
            TotalVolume = totalVolume,
            TotalOrderVolume = totalVolume,
            TotalAllocVolume = totalAllocVolume,
            Remarks = order.Remarks,
            StoreAddress = store?.Address,
            StoreContactEmail = store?.ContactEmail,
            ShippingFee = order.ShippingFee,
            OrderDate = order.OrderDate,
            TotalAllocQuantity = (int)detailRows.Sum(row => row.AllocQuantity),
            TotalSKU = detailRows
                .Select(row => row.ProductCode)
                .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            FlowStatus = order.FlowStatus,
            // Summary 明确不返回商品行，不能被调用方误当成 full cart。
            Items = new List<StoreOrderCartItemDto>(),
        };
    }

    public async Task<StoreOrderCartMutationResultDto> GetMutationResultAsync(
        StoreOrderCartMutationWrite write
    )
    {
        StoreOrderCartItemDto? changedItem = null;
        if (!write.Removed && !string.IsNullOrWhiteSpace(write.OrderGuid))
        {
            changedItem = await QueryChangedItemAsync(
                write.OrderGuid,
                write.ProductCode,
                write.DetailGuid
            );
        }

        return new StoreOrderCartMutationResultDto
        {
            ProductCode = write.ProductCode,
            Removed = write.Removed || changedItem == null,
            Summary = new StoreOrderCartMutationSummaryDto
            {
                OrderGUID = write.OrderGuid,
                StoreCode = write.StoreCode,
                TotalAmount = write.Summary.TotalAmount,
                TotalImportAmount = write.Summary.TotalImportAmount,
                TotalQuantity = (int)write.Summary.TotalQuantity,
                TotalSku = write.Summary.TotalSku,
                CartRevision = write.Summary.CartRevision,
            },
            ChangedItem = changedItem,
        };
    }

    public Task<StoreOrderCartMutationOutcome> AddAsync(
        StoreOrderCartScope scope,
        string productCode,
        decimal quantity,
        StoreOrderProductDto? knownProduct,
        bool omitNonPositiveNewDetail
    )
    {
        return AddCoreAsync(
            scope,
            productCode,
            quantity,
            knownProduct,
            omitNonPositiveNewDetail
        );
    }

    public async Task<StoreOrderCartMutationOutcome> SetQuantityAsync(
        StoreOrderCartScope scope,
        string productCode,
        decimal quantity,
        bool omitNonPositiveNewDetail
    )
    {
        var order = await FindActiveCartAsync(scope);
        if (order == null)
        {
            if (omitNonPositiveNewDetail && quantity <= 0)
            {
                return StoreOrderCartMutationOutcome.Completed(
                    EmptyMutation(scope, productCode)
                );
            }

            return await AddCoreAsync(
                scope,
                productCode,
                quantity,
                null,
                omitNonPositiveNewDetail
            );
        }

        var warehouseProduct = await _db.Queryable<WarehouseProduct>()
            .Where(product => product.ProductCode == productCode)
            .FirstAsync();
        if (warehouseProduct == null)
        {
            return StoreOrderCartMutationOutcome.ProductMissing();
        }

        var now = DateTime.Now;
        var actor = ResolveActorName();
        var detail = await FindActiveDetailAsync(order.OrderGUID, productCode);
        var removed = false;
        var detailGuid = detail?.DetailGUID;
        if (detail == null)
        {
            if (omitNonPositiveNewDetail && quantity <= 0)
            {
                removed = true;
            }
            else
            {
                detail = CreateDetail(
                    order.OrderGUID,
                    scope.StoreCode,
                    productCode,
                    quantity,
                    warehouseProduct.OEMPrice ?? 0,
                    warehouseProduct.ImportPrice ?? 0,
                    now,
                    actor
                );
                detailGuid = detail.DetailGUID;
                await _db.Insertable(detail).ExecuteCommandAsync();
            }
        }
        else
        {
            detail.Quantity = quantity;
            if (detail.Quantity <= 0)
            {
                removed = true;
                await SoftDeleteAsync(detail, actor, now);
            }
            else
            {
                UpdateDetailAmounts(detail, now, actor);
                await _db.Updateable(detail).ExecuteCommandAsync();
            }
        }

        var summary = await RecalculateAsync(order.OrderGUID, order.UpdatedAt);
        return StoreOrderCartMutationOutcome.Completed(
            new StoreOrderCartMutationWrite(
                order.OrderGUID,
                scope.StoreCode,
                productCode,
                detailGuid,
                removed,
                summary
            )
        );
    }

    public async Task<bool> RemoveAsync(StoreOrderCartScope scope, string detailGuid)
    {
        var order = await FindActiveCartAsync(scope);
        if (order == null)
        {
            return false;
        }

        var detail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(candidate =>
                candidate.DetailGUID == detailGuid
                && candidate.OrderGUID == order.OrderGUID
                && candidate.StoreCode == scope.StoreCode
                && !candidate.IsDeleted
            )
            .FirstAsync();
        if (detail == null)
        {
            return false;
        }

        await SoftDeleteAsync(detail, ResolveActorName(), DateTime.Now);
        await RecalculateAsync(order.OrderGUID, order.UpdatedAt);
        return true;
    }

    public async Task<StoreOrderCartClearOutcome> ClearAsync(StoreOrderCartScope scope)
    {
        var cart = await FindActiveCartAsync(scope);
        if (cart == null)
        {
            return new StoreOrderCartClearOutcome(false);
        }

        await _db.Deleteable<WareHouseOrderDetails>()
            .Where(detail => detail.OrderGUID == cart.OrderGUID)
            .ExecuteCommandAsync();
        await _db.Deleteable<WareHouseOrder>()
            .Where(order => order.OrderGUID == cart.OrderGUID)
            .ExecuteCommandAsync();
        return new StoreOrderCartClearOutcome(true);
    }

    public async Task<StoreOrderCartSubmissionSnapshot?> GetActiveForSubmissionAsync(
        StoreOrderCartScope scope
    )
    {
        var order = await FindActiveCartAsync(scope);
        return order == null
            ? null
            : new StoreOrderCartSubmissionSnapshot(order.OrderGUID, order.FlowStatus);
    }

    public Task<int> CountActiveItemsAsync(string orderGuid)
    {
        return _db.Queryable<WareHouseOrderDetails>()
            .Where(detail => detail.OrderGUID == orderGuid && !detail.IsDeleted)
            .CountAsync();
    }

    public Task<int> CompareExchangeSubmitAsync(
        StoreOrderCartSubmissionSnapshot snapshot,
        string orderNo,
        string? remarks,
        DateTime submittedAt,
        string submittedBy
    )
    {
        return _db.Updateable<WareHouseOrder>()
            .SetColumns(order => new WareHouseOrder
            {
                FlowStatus = 1,
                Remarks = remarks,
                OrderDate = submittedAt,
                UpdatedAt = submittedAt,
                UpdatedBy = submittedBy,
                OrderNo = orderNo,
            })
            .Where(order =>
                order.OrderGUID == snapshot.OrderGuid
                && !order.IsDeleted
                && order.FlowStatus == snapshot.FlowStatus
            )
            .ExecuteCommandAsync();
    }

    private async Task<StoreOrderCartMutationOutcome> AddCoreAsync(
        StoreOrderCartScope scope,
        string productCode,
        decimal quantity,
        StoreOrderProductDto? knownProduct,
        bool omitNonPositiveNewDetail
    )
    {
        var now = DateTime.Now;
        var actor = ResolveActorName();
        var order = await FindActiveCartAsync(scope);
        if (order == null)
        {
            order = new WareHouseOrder
            {
                OrderGUID = UuidHelper.GenerateUuid7(),
                StoreCode = scope.StoreCode,
                CartOwnerUserGuid = scope.CartOwnerUserGuid,
                OrderDate = now,
                FlowStatus = 0,
                IsDeleted = false,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = actor,
                OEMTotalAmount = 0,
                ImportTotalAmount = 0,
                ShippingFee = 0,
            };
            await _db.Insertable(order).ExecuteCommandAsync();
        }

        decimal price;
        decimal importPrice;
        if (
            knownProduct != null
            && string.Equals(
                knownProduct.ProductCode,
                productCode,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            price = knownProduct.OEMPrice ?? 0;
            importPrice = knownProduct.ImportPrice ?? 0;
        }
        else
        {
            var productPrice = await _db.Queryable<Product>()
                .InnerJoin<WarehouseProduct>(
                    (product, warehouseProduct) =>
                        product.ProductCode == warehouseProduct.ProductCode
                )
                .Where(
                    (product, warehouseProduct) => product.ProductCode == productCode
                )
                .Select(
                    (product, warehouseProduct) =>
                        new CartProductPriceRow
                        {
                            Price = warehouseProduct.OEMPrice,
                            ImportPrice = warehouseProduct.ImportPrice,
                        }
                )
                .FirstAsync();
            if (productPrice == null)
            {
                return StoreOrderCartMutationOutcome.ProductMissing();
            }

            price = productPrice.Price ?? 0;
            importPrice = productPrice.ImportPrice ?? 0;
        }

        var detail = await FindActiveDetailAsync(order.OrderGUID, productCode);
        var removed = false;
        var detailGuid = detail?.DetailGUID;
        if (detail == null)
        {
            if (omitNonPositiveNewDetail && quantity <= 0)
            {
                removed = true;
            }
            else
            {
                detail = CreateDetail(
                    order.OrderGUID,
                    scope.StoreCode,
                    productCode,
                    quantity,
                    price,
                    importPrice,
                    now,
                    actor
                );
                detailGuid = detail.DetailGUID;
                await _db.Insertable(detail).ExecuteCommandAsync();
            }
        }
        else
        {
            detail.Quantity += quantity;
            if (detail.Quantity <= 0)
            {
                removed = true;
                await SoftDeleteAsync(detail, actor, now);
            }
            else
            {
                UpdateDetailAmounts(detail, now, actor);
                await _db.Updateable(detail).ExecuteCommandAsync();
            }
        }

        var summary = await RecalculateAsync(order.OrderGUID, order.UpdatedAt);
        return StoreOrderCartMutationOutcome.Completed(
            new StoreOrderCartMutationWrite(
                order.OrderGUID,
                scope.StoreCode,
                productCode,
                detailGuid,
                removed,
                summary
            )
        );
    }

    private async Task<StoreOrderCartMutationSummary> RecalculateAsync(
        string orderGuid,
        DateTime? previousUpdatedAt
    )
    {
        var row = await _db.Queryable<WareHouseOrderDetails>()
            .Where(detail => detail.OrderGUID == orderGuid && !detail.IsDeleted)
            .Select(detail => new CartMutationSummaryRow
            {
                TotalQuantity = SqlFunc.AggregateSum(detail.Quantity ?? 0),
                TotalSku = SqlFunc.AggregateDistinctCount(detail.ProductCode),
                TotalAmount = SqlFunc.AggregateSum(detail.OEMAmount ?? 0),
                TotalImportAmount = SqlFunc.AggregateSum(detail.ImportAmount ?? 0),
            })
            .FirstAsync();

        var totalAmount = row?.TotalAmount ?? 0;
        var totalImportAmount = row?.TotalImportAmount ?? 0;
        var (revisionAt, cartRevision) = StoreOrderCartRules.ResolveNextRevision(
            previousUpdatedAt
        );
        await _db.Updateable<WareHouseOrder>()
            .SetColumns(order => new WareHouseOrder
            {
                OEMTotalAmount = totalAmount,
                ImportTotalAmount = totalImportAmount,
                UpdatedAt = revisionAt,
            })
            .Where(order => order.OrderGUID == orderGuid)
            .ExecuteCommandAsync();

        return new StoreOrderCartMutationSummary(
            cartRevision,
            totalAmount,
            totalImportAmount,
            row?.TotalQuantity ?? 0,
            row?.TotalSku ?? 0
        );
    }

    private async Task<StoreOrderCartItemDto?> QueryChangedItemAsync(
        string orderGuid,
        string productCode,
        string? detailGuid
    )
    {
        var query = _db.Queryable<WareHouseOrderDetails>()
            .LeftJoin<Product>((detail, product) => detail.ProductCode == product.ProductCode)
            .LeftJoin<WarehouseProduct>(
                (detail, product, warehouseProduct) =>
                    detail.ProductCode == warehouseProduct.ProductCode
            )
            .LeftJoin<DomesticProduct>(
                (detail, product, warehouseProduct, domesticProduct) =>
                    warehouseProduct.ProductCode == domesticProduct.ProductCode
            )
            .LeftJoin<ProductGrade>(
                (detail, product, warehouseProduct, domesticProduct, grade) =>
                    detail.ProductCode == grade.ProductCode && !grade.IsDeleted
            )
            .Where(detail =>
                detail.OrderGUID == orderGuid
                && detail.ProductCode == productCode
                && !detail.IsDeleted
            );

        if (!string.IsNullOrWhiteSpace(detailGuid))
        {
            query = query.Where(detail => detail.DetailGUID == detailGuid);
        }

        var item = await query
            .Select(
                (detail, product, warehouseProduct, domesticProduct, grade) =>
                    new StoreOrderCartItemDto
                    {
                        DetailGUID = detail.DetailGUID,
                        ProductCode = detail.ProductCode ?? string.Empty,
                        ItemNumber = product.ItemNumber,
                        Barcode = product.Barcode,
                        Grade = grade.Grade,
                        ProductName = product.ProductName,
                        ProductImage = product.ProductImage,
                        Price = detail.OEMPrice ?? 0,
                        Quantity = detail.Quantity ?? 0,
                        AllocQuantity = detail.AllocQuantity,
                        Amount = detail.OEMAmount ?? 0,
                        ImportPrice = detail.ImportPrice ?? (warehouseProduct.ImportPrice ?? 0),
                        ImportAmount =
                            detail.ImportAmount
                            ?? (
                                (detail.ImportPrice ?? (warehouseProduct.ImportPrice ?? 0))
                                * (detail.Quantity ?? 0)
                            ),
                        AllocatedImportAmount =
                            (detail.ImportPrice ?? (warehouseProduct.ImportPrice ?? 0))
                            * (detail.AllocQuantity ?? 0),
                        Volume = domesticProduct.PackingQuantity > 0
                            ? domesticProduct.UnitVolume / domesticProduct.PackingQuantity
                            : domesticProduct.UnitVolume,
                        MinOrderQuantity = warehouseProduct.MinOrderQuantity ?? 1,
                    }
            )
            .FirstAsync();

        if (item?.Volume.HasValue == true)
        {
            item.OrderVolume = StoreOrderCartRules.CalculateVolume(item.Volume, item.Quantity);
            item.AllocVolume = StoreOrderCartRules.CalculateVolume(
                item.Volume,
                item.AllocQuantity ?? 0
            );
            item.TotalVolume = item.OrderVolume;
        }

        return item;
    }

    private async Task<WareHouseOrder?> FindActiveCartAsync(StoreOrderCartScope scope)
    {
        var query = _db.Queryable<WareHouseOrder>()
            .Where(order =>
                order.StoreCode == scope.StoreCode
                && order.FlowStatus == 0
                && !order.IsDeleted
            );
        query = string.IsNullOrWhiteSpace(scope.CartOwnerUserGuid)
            ? query.Where(order => SqlFunc.IsNullOrEmpty(order.CartOwnerUserGuid))
            : query.Where(order => order.CartOwnerUserGuid == scope.CartOwnerUserGuid);
        return await query.FirstAsync();
    }

    private async Task<WareHouseOrderDetails?> FindActiveDetailAsync(
        string orderGuid,
        string productCode
    )
    {
        return await _db.Queryable<WareHouseOrderDetails>()
            .Where(detail =>
                detail.OrderGUID == orderGuid
                && detail.ProductCode == productCode
                && !detail.IsDeleted
            )
            .FirstAsync();
    }

    private async Task<Store?> GetStoreByCodeOrGuidAsync(string? storeCode)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return null;
        }

        var normalized = storeCode.Trim();
        return await _db.Queryable<Store>()
            .Where(store =>
                !store.IsDeleted
                && (store.StoreCode == normalized || store.StoreGUID == normalized)
            )
            .FirstAsync();
    }

    private static WareHouseOrderDetails CreateDetail(
        string orderGuid,
        string storeCode,
        string productCode,
        decimal quantity,
        decimal price,
        decimal importPrice,
        DateTime now,
        string actor
    ) => new()
    {
        DetailGUID = UuidHelper.GenerateUuid7(),
        OrderGUID = orderGuid,
        StoreCode = storeCode,
        ProductCode = productCode,
        Quantity = quantity,
        OEMPrice = price,
        OEMAmount = price * quantity,
        ImportPrice = importPrice,
        ImportAmount = importPrice * quantity,
        IsDeleted = false,
        CreatedAt = now,
        UpdatedAt = now,
        CreatedBy = actor,
        UpdatedBy = actor,
    };

    private static void UpdateDetailAmounts(
        WareHouseOrderDetails detail,
        DateTime now,
        string actor
    )
    {
        detail.OEMAmount = detail.Quantity * detail.OEMPrice;
        detail.ImportAmount = detail.Quantity * detail.ImportPrice;
        detail.UpdatedAt = now;
        detail.UpdatedBy = actor;
    }

    private async Task SoftDeleteAsync(
        WareHouseOrderDetails detail,
        string actor,
        DateTime now
    )
    {
        detail.IsDeleted = true;
        detail.UpdatedAt = now;
        detail.UpdatedBy = actor;
        await _db.Updateable(detail).ExecuteCommandAsync();
    }

    private string ResolveActorName()
    {
        return actorContext.ActorName;
    }

    private static StoreOrderCartMutationWrite EmptyMutation(
        StoreOrderCartScope scope,
        string productCode
    ) => new(
        string.Empty,
        scope.StoreCode,
        productCode,
        null,
        true,
        new StoreOrderCartMutationSummary(0, 0, 0, 0, 0)
    );

    private sealed class CartProductPriceRow
    {
        public decimal? Price { get; init; }
        public decimal? ImportPrice { get; init; }
    }

    private sealed class CartMutationSummaryRow
    {
        public decimal TotalAmount { get; init; }
        public decimal TotalImportAmount { get; init; }
        public decimal TotalQuantity { get; init; }
        public int TotalSku { get; init; }
    }

    private sealed class CartSummaryReadRow
    {
        public string? ProductCode { get; init; }
        public decimal Quantity { get; init; }
        public decimal AllocQuantity { get; init; }
        public decimal ImportAmount { get; init; }
        public decimal AllocatedImportAmount { get; init; }
        public decimal? UnitVolume { get; init; }
    }
}
