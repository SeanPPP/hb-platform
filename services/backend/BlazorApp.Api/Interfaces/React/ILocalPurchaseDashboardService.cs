using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 进货金额看板分店范围。全店权限必须显式声明，空受限集合表示无权访问任何分店。
    /// </summary>
    public sealed class LocalPurchaseDashboardStoreScope
    {
        private LocalPurchaseDashboardStoreScope(
            bool includesAllStores,
            IReadOnlyList<string> storeCodes
        )
        {
            IncludesAllStores = includesAllStores;
            StoreCodes = storeCodes;
        }

        public bool IncludesAllStores { get; }
        public IReadOnlyList<string> StoreCodes { get; }

        public static LocalPurchaseDashboardStoreScope AllStores() =>
            new(true, Array.Empty<string>());

        public static LocalPurchaseDashboardStoreScope Restricted(
            IReadOnlyList<string> storeCodes
        )
        {
            ArgumentNullException.ThrowIfNull(storeCodes);
            return new LocalPurchaseDashboardStoreScope(false, storeCodes);
        }
    }

    public interface ILocalPurchaseDashboardService
    {
        Task<ApiResponse<LocalPurchaseDashboardResponseDto>> GetDashboardAsync(
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope,
            CancellationToken cancellationToken
        );

        Task<ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>> GetStoreSuppliersAsync(
            string storeCode,
            string endMonth,
            LocalPurchaseDashboardStoreScope storeScope,
            CancellationToken cancellationToken
        );
    }
}
