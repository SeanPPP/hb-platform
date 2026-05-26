using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IStoreVoucherReactService
{
    Task<PagedListReactDto<StoreVoucherDto>> GetVoucherListAsync(StoreVoucherQueryParams queryParams);
    Task<ApiResponse<StoreVoucherDetailResponse>> GetVoucherDetailAsync(string idOrCode);
}
