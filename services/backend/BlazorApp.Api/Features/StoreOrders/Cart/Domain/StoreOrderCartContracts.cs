using BlazorApp.Api.Features.StoreOrders.Common;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.StoreOrders.Cart.Domain;

internal enum StoreOrderCartResponseShape
{
    Full,
    Mutation,
}

internal readonly record struct StoreOrderCartValidationFailure(string Message);

internal sealed record StoreOrderCartMutationSummary(
    long CartRevision,
    decimal TotalAmount,
    decimal TotalImportAmount,
    decimal TotalQuantity,
    int TotalSku
);

internal sealed record StoreOrderCartMutationWrite(
    string OrderGuid,
    string StoreCode,
    string ProductCode,
    string? DetailGuid,
    bool Removed,
    StoreOrderCartMutationSummary Summary
);

internal sealed record StoreOrderCartMutationOutcome(
    bool Success,
    string? Message,
    StoreOrderCartMutationWrite? Write
)
{
    internal static StoreOrderCartMutationOutcome ProductMissing() =>
        new(false, "商品不存在", null);

    internal static StoreOrderCartMutationOutcome Completed(
        StoreOrderCartMutationWrite write
    ) => new(true, null, write);
}

internal sealed record StoreOrderCartClearOutcome(bool CartExisted);

internal static class StoreOrderCartRules
{
    internal static string NormalizeStoreCode(string? storeCode)
    {
        return storeCode?.Trim() ?? string.Empty;
    }

    internal static string NormalizeLockKey(StoreOrderCartScope scope)
    {
        var storeKey = string.IsNullOrWhiteSpace(scope.StoreCode)
            ? "unknown"
            : scope.StoreCode.Trim().ToUpperInvariant();
        var ownerKey = string.IsNullOrWhiteSpace(scope.CartOwnerUserGuid)
            ? "store"
            : scope.CartOwnerUserGuid.Trim().ToUpperInvariant();

        return $"{storeKey}:{ownerKey}";
    }

    internal static (DateTime RevisionAt, long CartRevision) ResolveNextRevision(
        DateTime? previousUpdatedAt,
        DateTime? nowOverride = null
    )
    {
        var now = nowOverride ?? DateTime.Now;
        var nowRevision = ToRevision(now);
        var previousRevision = previousUpdatedAt.HasValue
            ? ToRevision(previousUpdatedAt.Value)
            : 0;
        var nextRevision = Math.Max(nowRevision, previousRevision + 1);

        return (
            DateTimeOffset.FromUnixTimeMilliseconds(nextRevision).LocalDateTime,
            nextRevision
        );
    }

    internal static decimal CalculateImportAmount(decimal? quantity, decimal? importPrice)
    {
        return (quantity ?? 0) * (importPrice ?? 0);
    }

    internal static decimal? CalculateVolume(decimal? unitVolume, decimal quantity)
    {
        return unitVolume.HasValue ? unitVolume.Value * quantity : null;
    }

    internal static ApiResponse<T> ValidationFailure<T>(
        StoreOrderCartValidationFailure failure
    ) => new()
    {
        Success = false,
        Message = failure.Message,
    };

    private static long ToRevision(DateTime revisionAt)
    {
        return new DateTimeOffset(revisionAt).ToUnixTimeMilliseconds();
    }
}
