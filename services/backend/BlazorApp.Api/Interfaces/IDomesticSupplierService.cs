using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 国内供应商服务接口
    /// </summary>
    public interface IDomesticSupplierService
    {
        /// <summary>
        /// 获取分页供应商列表
        /// </summary>
        /// <param name="query">查询参数</param>
        /// <returns>分页结果</returns>
        Task<PagedResult<DomesticSupplierDto>> GetSuppliersAsync(DomesticSupplierQueryDto query);

        /// <summary>
        /// 根据GUID获取供应商详情
        /// </summary>
        /// <param name="guid">供应商GUID</param>
        /// <returns>供应商详情</returns>
        Task<DomesticSupplierDto?> GetSupplierByGuidAsync(string guid);

        /// <summary>
        /// 根据供应商编码获取供应商详情
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>供应商详情</returns>
        Task<DomesticSupplierDto?> GetSupplierByCodeAsync(string supplierCode);

        /// <summary>
        /// 创建新供应商
        /// </summary>
        /// <param name="dto">创建供应商请求DTO</param>
        /// <param name="currentUser">当前用户</param>
        /// <returns>创建的供应商信息</returns>
        Task<DomesticSupplierDto> CreateSupplierAsync(CreateDomesticSupplierDto dto, string currentUser);

        /// <summary>
        /// 更新供应商信息
        /// </summary>
        /// <param name="guid">供应商GUID</param>
        /// <param name="dto">更新供应商请求DTO</param>
        /// <param name="currentUser">当前用户</param>
        /// <returns>更新后的供应商信息</returns>
        Task<DomesticSupplierDto?> UpdateSupplierAsync(string guid, UpdateDomesticSupplierDto dto, string currentUser);

        /// <summary>
        /// 删除供应商
        /// </summary>
        /// <param name="guid">供应商GUID</param>
        /// <returns>是否删除成功</returns>
        Task<bool> DeleteSupplierAsync(string guid);

        /// <summary>
        /// 检查供应商编码是否已存在
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="excludeGuid">排除的GUID（用于更新时检查）</param>
        /// <returns>是否已存在</returns>
        Task<bool> IsSupplierCodeExistsAsync(string supplierCode, string? excludeGuid = null);

        /// <summary>
        /// 生成下一个可用的供应商编码（HB+3位序号）
        /// </summary>
        /// <returns>生成的供应商编码</returns>
        Task<string> GenerateNextSupplierCodeAsync();

        /// <summary>
        /// 启用/禁用供应商
        /// </summary>
        /// <param name="guid">供应商GUID</param>
        /// <param name="status">状态（1=启用，0=禁用）</param>
        /// <param name="currentUser">当前用户</param>
        /// <returns>是否操作成功</returns>
        Task<bool> UpdateSupplierStatusAsync(string guid, int status, string currentUser);

        /// <summary>
        /// 获取所有启用的供应商列表（用于下拉选择）
        /// </summary>
        /// <returns>启用的供应商列表</returns>
        Task<List<DomesticSupplierDto>> GetActiveSupplierListAsync();
    }
}
