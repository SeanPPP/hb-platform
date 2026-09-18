using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    public interface IPosmSalesOrderReactService
    {
        Task<PagedListReactDto<PosmSalesOrderDto>> GetSalesOrderListAsync(PosmSalesOrderQueryParams queryParams);
        Task<ApiResponse<PosmSalesOrderDetailResponse>> GetSalesOrderDetailAsync(string orderGuid);

        /// <summary>
        /// 查询一批订单中被关键词命中的明细商品（货号 / 条码 / 商品名），按订单分组。
        /// 只针对当前页订单执行，不参与列表本身的过滤与分页。
        /// </summary>
        Task<Dictionary<string, List<PosmSalesOrderMatchedProductDto>>> GetMatchedProductsAsync(
            IReadOnlyCollection<string> orderGuids,
            string keyword
        );
    }
}
