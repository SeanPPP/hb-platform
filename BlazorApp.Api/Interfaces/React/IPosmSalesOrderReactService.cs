using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IPosmSalesOrderReactService
    {
        Task<PagedListReactDto<PosmSalesOrderDto>> GetSalesOrderListAsync(PosmSalesOrderQueryParams queryParams);
        Task<ApiResponse<PosmSalesOrderDetailResponse>> GetSalesOrderDetailAsync(string orderGuid);
    }
}
