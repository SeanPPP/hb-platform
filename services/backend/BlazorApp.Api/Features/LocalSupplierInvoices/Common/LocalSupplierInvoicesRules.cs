using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    /// <summary>各垂直切片共享的无状态业务规则。</summary>
    internal static class LocalSupplierInvoicesRules
    {
        public static bool IsPositiveValue(decimal? value)
        {
            return value.HasValue && value.Value > 0;
        }

        public static bool IsClientSelectableDetailAction(int action)
        {
            return action >= (int)DetailAction.None && action <= (int)DetailAction.AddMultiCode;
        }

        public static string? ResolveDetailStoreCode(
            string? detailStoreCode,
            string? headerStoreCode
        )
        {
            // 明细分店为空白时必须回退单头分店，保证上次进货价优先取当前分店价。
            return string.IsNullOrWhiteSpace(detailStoreCode) ? headerStoreCode : detailStoreCode;
        }
    }
}
