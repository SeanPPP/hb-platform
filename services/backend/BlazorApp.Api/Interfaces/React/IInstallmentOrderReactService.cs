using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IInstallmentOrderReactService
{
    Task<PagedListReactDto<InstallmentOrderSummaryDto>> GetOrderListAsync(
        InstallmentOrderQueryParams queryParams
    );

    Task<ApiResponse<InstallmentOrderDetailResponse>> GetOrderDetailAsync(
        string installmentGuid,
        IReadOnlyCollection<string>? allowedStoreCodes
    );

}
