using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 国内供应商服务接口
    /// </summary>
    public interface IChinaSupplierService
    {
        /// <summary>
        /// 获取国内供应商列表
        /// </summary>
        Task<ApiResponse<PagedResult<ChinaSupplierDto>>> GetChinaSuppliersAsync(ChinaSupplierQueryDto query);

        /// <summary>
        /// 根据GUID获取国内供应商详情
        /// </summary>
        Task<ApiResponse<ChinaSupplierDetailDto>> GetChinaSupplierByGuidAsync(string guid);

        /// <summary>
        /// 创建国内供应商
        /// </summary>
        Task<ApiResponse<ChinaSupplierDto>> CreateChinaSupplierAsync(CreateChinaSupplierDto dto);

        /// <summary>
        /// 更新国内供应商
        /// </summary>
        Task<ApiResponse<ChinaSupplierDto>> UpdateChinaSupplierAsync(string guid, UpdateChinaSupplierDto dto);

        /// <summary>
        /// 删除国内供应商
        /// </summary>
        Task<ApiResponse<bool>> DeleteChinaSupplierAsync(string guid);

        /// <summary>
        /// 启用/禁用国内供应商
        /// </summary>
        Task<ApiResponse<ChinaSupplierDto>> ToggleSupplierStatusAsync(string guid, int status);

        /// <summary>
        /// 根据供应商代码检查是否存在
        /// </summary>
        Task<ApiResponse<bool>> CheckSupplierCodeExistsAsync(string supplierCode, string? excludeGuid = null);

        /// <summary>
        /// 获取所有启用的国内供应商（下拉选择用）
        /// </summary>
        Task<ApiResponse<List<ChinaSupplierDto>>> GetActiveChinaSuppliersAsync();

        /// <summary>
        /// 获取所有国内供应商（不分页）
        /// </summary>
        Task<ApiResponse<List<ChinaSupplierDto>>> GetAllChinaSuppliersAsync();

        /// <summary>
        /// 自动生成下一个供应商编码
        /// </summary>
        Task<ApiResponse<string>> GenerateNextSupplierCodeAsync();
    }
}