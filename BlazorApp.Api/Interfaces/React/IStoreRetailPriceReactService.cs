using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// React 分店零售价服务接口
    /// 提供网格查询、详情、创建、更新、删除与批量事务操作
    /// 支持基于分店/供应商/商品信息的服务端过滤与排序
    /// </summary>
    public interface IStoreRetailPriceReactService
    {
        /// <summary>
        /// 获取网格数据（服务端分页/过滤/排序）
        /// </summary>
        /// <param name="request">网格请求参数</param>
        /// <returns>网格响应（列表与总数）</returns>
        Task<GridResponseDto<StoreRetailPriceListDto>> GetGridDataAsync(GridRequestDto request);

        /// <summary>
        /// 根据主键UUID获取详情
        /// </summary>
        /// <param name="uuid">主键UUID</param>
        /// <returns>分店零售价详情</returns>
        Task<ApiResponse<StoreRetailPriceDetailDto>> GetByUuidAsync(string uuid);

        /// <summary>
        /// 新建分店零售价记录
        /// </summary>
        /// <param name="dto">创建请求DTO</param>
        /// <param name="createdBy">创建人</param>
        /// <returns>创建后的详情</returns>
        Task<ApiResponse<StoreRetailPriceDetailDto>> CreateAsync(
            CreateStoreRetailPriceDto dto,
            string createdBy
        );

        /// <summary>
        /// 更新分店零售价记录
        /// </summary>
        /// <param name="uuid">主键UUID</param>
        /// <param name="dto">更新请求DTO</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>更新后的详情</returns>
        Task<ApiResponse<StoreRetailPriceDetailDto>> UpdateAsync(
            string uuid,
            UpdateStoreRetailPriceDto dto,
            string updatedBy
        );

        /// <summary>
        /// 软删除分店零售价记录
        /// </summary>
        /// <param name="uuid">主键UUID</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>操作结果</returns>
        Task<ApiResponse<bool>> DeleteAsync(string uuid, string updatedBy);

        /// <summary>
        /// 批量新增或更新（事务）
        /// </summary>
        /// <param name="items">批量项列表（含键与更新值）</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>插入/更新/失败统计与错误信息</returns>
        Task<ApiResponse<BatchResultDto>> BatchUpsertAsync(
            List<StoreRetailPriceUpsertItemDto> items,
            string updatedBy
        );

        /// <summary>
        /// 批量删除（事务，软删）
        /// </summary>
        /// <param name="uuids">UUID 列表</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>操作结果</returns>
        Task<ApiResponse<bool>> BatchDeleteAsync(List<string> uuids, string updatedBy);

        Task<ApiResponse<bool>> BatchUpdateSpecialFlagAsync(
            List<string> productCodes,
            bool isSpecial,
            string updatedBy
        );

        /// <summary>
        /// 按 UUID 批量获取列表用于前端增量刷新
        /// </summary>
        /// <param name="uuids">UUID 列表</param>
        /// <returns>列表数据</returns>
        Task<ApiResponse<List<StoreRetailPriceListDto>>> GetListByUuidsAsync(List<string> uuids);

        Task<ApiResponse<BatchResultDto>> UpsertForActiveStoresAsync(
            List<StoreRetailPriceUpsertForActiveStoresItemDto> items,
            string updatedBy
        );

        Task<ApiResponse<BatchResultDto>> BatchDeleteByProductCodesAsync(
            List<string> productCodes,
            List<string> storeCodes,
            string updatedBy
        );
    }
}
