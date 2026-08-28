using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.StoreOrders.PasteReplace.Domain;

internal readonly record struct PasteReplaceValidationResult(
    bool IsValid,
    string ErrorMessage
)
{
    internal static PasteReplaceValidationResult Valid() => new(true, string.Empty);

    internal static PasteReplaceValidationResult Invalid(string errorMessage) =>
        new(false, errorMessage);
}

internal sealed record PasteReplaceMutationPlan(
    string OrderGuid,
    IReadOnlyList<string> DetailGuidsToDelete,
    IReadOnlyList<WareHouseOrderDetails> DetailsToUpdate,
    IReadOnlyList<WareHouseOrderDetails> DetailsToInsert
);

internal static class PasteReplaceOrderLinesRules
{
    internal static bool IsSupportedTargetField(string targetField)
    {
        return string.Equals(
                targetField,
                StoreOrderPasteTargetFields.Quantity,
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                targetField,
                StoreOrderPasteTargetFields.AllocQuantity,
                StringComparison.OrdinalIgnoreCase
            );
    }

    internal static bool IsSupportedAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return true;
        }

        return string.Equals(
                action,
                StoreOrderPasteActions.Replace,
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                action,
                StoreOrderPasteActions.Append,
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                action,
                StoreOrderPasteActions.Skip,
                StringComparison.OrdinalIgnoreCase
            );
    }

    internal static string NormalizeAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return StoreOrderPasteActions.Replace;
        }

        if (
            string.Equals(
                action,
                StoreOrderPasteActions.Append,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return StoreOrderPasteActions.Append;
        }

        if (
            string.Equals(
                action,
                StoreOrderPasteActions.Skip,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return StoreOrderPasteActions.Skip;
        }

        return StoreOrderPasteActions.Replace;
    }

    internal static List<ProductQuantityDto> NormalizeImportableItems(
        IReadOnlyCollection<ProductQuantityDto> items
    )
    {
        return items
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode)
                // Excel 粘贴允许 0 写入已有明细用于清零；负数仍过滤。
                && item.Quantity >= 0
                && !string.Equals(
                    NormalizeAction(item.Action),
                    StoreOrderPasteActions.Skip,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Select(item => new ProductQuantityDto
            {
                ProductCode = item.ProductCode.Trim(),
                Quantity = item.Quantity,
                ImportPrice = item.ImportPrice,
                Action = NormalizeAction(item.Action),
            })
            .ToList();
    }

    internal static decimal CalculateImportAmount(decimal? quantity, decimal? importPrice)
    {
        return (quantity ?? 0) * (importPrice ?? 0);
    }
}
