using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 只读仓库商品档案查询服务：摘要、货柜进货记录、分店配货统计。
    /// </summary>
    public interface IWarehouseProductRecordQueryService
    {
        /// <summary>
        /// 查询商品摘要；商品不存在时返回 null。
        /// </summary>
        Task<WarehouseProductRecordSummaryDto?> GetSummaryAsync(string productCode);

        /// <summary>
        /// 分页查询该商品的全部货柜进货记录；商品不存在时抛出 KeyNotFoundException。
        /// </summary>
        Task<WarehouseProductRecordContainerQueryResultDto> QueryContainersAsync(
            string productCode,
            WarehouseProductRecordContainerQueryRequest request
        );

        /// <summary>
        /// 按分店汇总该商品的配货统计；商品不存在时抛出 KeyNotFoundException。
        /// </summary>
        Task<WarehouseProductRecordAllocationQueryResultDto> QueryAllocationsAsync(
            string productCode,
            WarehouseProductRecordAllocationQueryRequest request
        );
    }
}
