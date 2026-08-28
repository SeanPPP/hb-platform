using BlazorApp.Api.Data;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.Common;

internal interface IStoreOrderProductCostCoordinator
{
    string BusyErrorCode { get; }

    IReadOnlyList<string> NormalizeProductCodes(IEnumerable<string?> productCodes);

    Task<StoreOrderProductCostMutationScope> AcquireProductsAsync(
        IEnumerable<string?> productCodes
    );

    Task RecalculateAsync(StoreOrderProductCostMutationScope scope, string updatedBy);

    ISugarQueryable<T> WithUpdateLock<T>(ISugarQueryable<T> queryable);

    bool IsBusyConflict(Exception exception);
}

internal sealed class StoreOrderProductCostMutationScope
{
    internal StoreOrderProductCostMutationScope(
        SetChildPurchasePriceLockScope? lockScope,
        IReadOnlyList<string> productCodes
    )
    {
        LockScope = lockScope;
        ProductCodes = productCodes;
    }

    internal SetChildPurchasePriceLockScope? LockScope { get; }

    internal IReadOnlyList<string> ProductCodes { get; }
}

internal sealed class StoreOrderProductCostCoordinator(SqlSugarContext context)
    : IStoreOrderProductCostCoordinator
{
    private readonly ISqlSugarClient _db = context.Db;

    public string BusyErrorCode => SetChildPurchasePriceMutationLock.BusyErrorCode;

    public IReadOnlyList<string> NormalizeProductCodes(IEnumerable<string?> productCodes)
    {
        return SetChildPurchasePriceMutationLock.NormalizeProductCodes(productCodes);
    }

    public async Task<StoreOrderProductCostMutationScope> AcquireProductsAsync(
        IEnumerable<string?> productCodes
    )
    {
        var normalizedCodes = SetChildPurchasePriceMutationLock.NormalizeProductCodes(productCodes);
        var lockScope = normalizedCodes.Count == 0
            ? null
            : await SetChildPurchasePriceMutationLock.AcquireProductsAsync(_db, normalizedCodes);
        return new StoreOrderProductCostMutationScope(lockScope, normalizedCodes);
    }

    public async Task RecalculateAsync(
        StoreOrderProductCostMutationScope scope,
        string updatedBy
    )
    {
        if (scope.LockScope == null || scope.ProductCodes.Count == 0)
        {
            return;
        }

        var recalculator = new SetChildPurchasePriceService(_db);
        await recalculator.RecalculateGlobalLockedAsync(
            scope.LockScope,
            scope.ProductCodes,
            updatedBy
        );

        var storeGroups = await CollectActualStoreGroupsAsync(scope.ProductCodes);
        if (storeGroups.Count > 0)
        {
            await recalculator.RecalculateStoreGroupsLockedAsync(
                scope.LockScope,
                storeGroups,
                updatedBy
            );
        }
    }

    public ISugarQueryable<T> WithUpdateLock<T>(ISugarQueryable<T> queryable)
    {
        return _db.CurrentConnectionConfig.DbType == DbType.SqlServer
            ? queryable.With(SqlWith.UpdLock)
            : queryable;
    }

    public bool IsBusyConflict(Exception exception)
    {
        return SetChildPurchasePriceMutationLock.TryResolveConflict(exception, out _);
    }

    private async Task<List<(string? StoreCode, string? ProductCode)>> CollectActualStoreGroupsAsync(
        IReadOnlyCollection<string> parentProductCodes
    )
    {
        var retailRows = await _db.Queryable<StoreRetailPrice>()
            .Where(item =>
                item.StoreCode != null
                && item.ProductCode != null
                && parentProductCodes.Contains(item.ProductCode)
                && item.IsActive
                && !item.IsDeleted
            )
            .Select(item => new StoreRetailPrice
            {
                StoreCode = item.StoreCode,
                ProductCode = item.ProductCode,
            })
            .ToListAsync();
        var multiCodeRows = await _db.Queryable<StoreMultiCodeProduct>()
            .Where(item =>
                item.StoreCode != null
                && item.ProductCode != null
                && parentProductCodes.Contains(item.ProductCode)
                && item.IsActive
                && !item.IsDeleted
            )
            .Select(item => new StoreMultiCodeProduct
            {
                StoreCode = item.StoreCode,
                ProductCode = item.ProductCode,
            })
            .ToListAsync();

        return retailRows
            .Select(item => (StoreCode: item.StoreCode, ProductCode: item.ProductCode))
            .Concat(
                multiCodeRows.Select(item =>
                    (StoreCode: item.StoreCode, ProductCode: item.ProductCode)
                )
            )
            .GroupBy(
                item => $"{item.StoreCode!}\u0001{item.ProductCode!}",
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.First())
            .ToList();
    }
}
