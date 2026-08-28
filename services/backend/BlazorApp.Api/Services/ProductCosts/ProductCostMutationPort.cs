using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Services.ProductCosts;

/// <summary>
/// 垂直切片使用的套装成本写入锁端口。旧 React 服务保留实现所有权，
/// 本端口只隔离命名空间并保持原事务、锁与错误语义。
/// </summary>
internal static class ProductCostMutationLock
{
    internal const string BusyErrorCode =
        React.SetChildPurchasePriceMutationLock.BusyErrorCode;

    internal static async Task<ProductCostMutationLockScope> AcquireAllAsync(
        ISqlSugarClient db
    ) =>
        new(await React.SetChildPurchasePriceMutationLock.AcquireAllAsync(db));

    internal static async Task<ProductCostMutationLockScope> AcquireProductsAsync(
        ISqlSugarClient db,
        IEnumerable<string?> productCodes
    ) =>
        new(
            await React.SetChildPurchasePriceMutationLock.AcquireProductsAsync(
                db,
                productCodes
            )
        );

    internal static bool TryResolveConflict(
        Exception? exception,
        out Exception? conflict
    )
    {
        var resolved = React.SetChildPurchasePriceMutationLock.TryResolveConflict(
            exception,
            out var legacyConflict
        );
        conflict = legacyConflict;
        return resolved;
    }

    internal static List<string> NormalizeProductCodes(
        IEnumerable<string?> productCodes
    ) => React.SetChildPurchasePriceMutationLock.NormalizeProductCodes(productCodes);
}

internal sealed class ProductCostMutationLockScope
{
    internal ProductCostMutationLockScope(React.SetChildPurchasePriceLockScope inner)
    {
        Inner = inner;
    }

    internal React.SetChildPurchasePriceLockScope Inner { get; }

    internal void EnsureCovers(ISqlSugarClient db, IEnumerable<string?> productCodes) =>
        Inner.EnsureCovers(db, productCodes);
}

/// <summary>
/// 垂直切片使用的套装成本重算端口；所有计算与持久化继续委派给既有实现。
/// </summary>
internal sealed class ProductCostRecalculationService
{
    private readonly React.SetChildPurchasePriceService _inner;

    internal ProductCostRecalculationService(ISqlSugarClient db)
    {
        _inner = new React.SetChildPurchasePriceService(db);
    }

    internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateLockedAsync(
        ProductCostMutationLockScope lockScope,
        IEnumerable<string?> productCodes,
        IEnumerable<string?>? storeCodes,
        string updatedBy
    ) =>
        _inner.RecalculateLockedAsync(
            lockScope.Inner,
            productCodes,
            storeCodes,
            updatedBy
        );

    internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateGlobalLockedAsync(
        ProductCostMutationLockScope lockScope,
        IEnumerable<string?> productCodes,
        string updatedBy
    ) =>
        _inner.RecalculateGlobalLockedAsync(
            lockScope.Inner,
            productCodes,
            updatedBy
        );

    internal Task<SetChildPurchasePriceWritebackResultDto> RecalculateStoreGroupsLockedAsync(
        ProductCostMutationLockScope lockScope,
        IEnumerable<(string? StoreCode, string? ProductCode)> groups,
        string updatedBy
    ) =>
        _inner.RecalculateStoreGroupsLockedAsync(
            lockScope.Inner,
            groups,
            updatedBy
        );
}
