using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 套装商品管理服务接口
    /// </summary>
    public interface IDomesticSetProductService
    {
        /// <summary>
        /// 获取套装商品分页列表
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果</returns>
        Task<ApiResponse<PagedResult<DomesticSetProductDto>>> GetDomesticSetProductsAsync(DomesticSetProductQueryDto query);

        /// <summary>
        /// 根据编码获取套装商品详情
        /// </summary>
        /// <param name="setProductCode">套装商品编码</param>
        /// <returns>套装商品详情</returns>
        Task<ApiResponse<DomesticSetProductDetailDto>> GetDomesticSetProductByCodeAsync(string setProductCode);

        /// <summary>
        /// 根据商品编码获取套装商品列表
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>套装商品列表</returns>
        Task<ApiResponse<List<DomesticSetProductDto>>> GetSetProductsByProductCodeAsync(string productCode);

        /// <summary>
        /// 根据供应商编码获取套装商品列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>套装商品列表</returns>
        Task<ApiResponse<List<DomesticSetProductDto>>> GetSetProductsBySupplierCodeAsync(string supplierCode);

        /// <summary>
        /// 创建套装商品
        /// </summary>
        /// <param name="dto">创建套装商品DTO</param>
        /// <returns>创建的套装商品</returns>
        Task<ApiResponse<DomesticSetProductDto>> CreateDomesticSetProductAsync(CreateDomesticSetProductDto dto);

        /// <summary>
        /// 更新套装商品
        /// </summary>
        /// <param name="setProductCode">套装商品编码</param>
        /// <param name="dto">更新套装商品DTO</param>
        /// <returns>更新的套装商品</returns>
        Task<ApiResponse<DomesticSetProductDto>> UpdateDomesticSetProductAsync(string setProductCode, UpdateDomesticSetProductDto dto);

        /// <summary>
        /// 删除套装商品
        /// </summary>
        /// <param name="setProductCode">套装商品编码</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> DeleteDomesticSetProductAsync(string setProductCode);

        /// <summary>
        /// 生成下一个套装货号
        /// </summary>
        /// <param name="baseItemNumber">基础商品货号</param>
        /// <returns>生成的套装货号</returns>
        Task<ApiResponse<string>> GenerateNextSetProductNoAsync(string baseItemNumber);

        /// <summary>
        /// 生成套装商品条形码
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>生成的条形码</returns>
        Task<ApiResponse<string>> GenerateSetProductBarcodeAsync(string supplierCode);

        /// <summary>
        /// 检查套装货号是否存在
        /// </summary>
        /// <param name="setProductNo">套装货号</param>
        /// <param name="excludeSetProductCode">排除的套装商品编码（用于更新时）</param>
        /// <returns>是否存在</returns>
        Task<ApiResponse<bool>> CheckSetProductNoExistsAsync(string setProductNo, string? excludeSetProductCode = null);

        /// <summary>
        /// 检查套装条形码是否存在
        /// </summary>
        /// <param name="setBarcode">套装条形码</param>
        /// <param name="excludeSetProductCode">排除的套装商品编码（用于更新时）</param>
        /// <returns>是否存在</returns>
        Task<ApiResponse<bool>> CheckSetBarcodeExistsAsync(string setBarcode, string? excludeSetProductCode = null);

        /// <summary>
        /// 批量创建套装商品
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>创建结果</returns>
        Task<ApiResponse<List<DomesticSetProductDto>>> BatchCreateDomesticSetProductsAsync(BatchCreateDomesticSetProductDto dto);

        /// <summary>
        /// 批量删除套装商品
        /// </summary>
        /// <param name="setProductCodes">套装商品编码列表</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> BatchDeleteDomesticSetProductsAsync(List<string> setProductCodes);

        /// <summary>
        /// 复制套装商品结构
        /// </summary>
        /// <param name="sourceProductCode">源商品编码</param>
        /// <param name="targetProductCode">目标商品编码</param>
        /// <returns>复制结果</returns>
        Task<ApiResponse<List<DomesticSetProductDto>>> CopySetProductStructureAsync(string sourceProductCode, string targetProductCode);

        /// <summary>
        /// 获取套装商品价格统计
        /// </summary>
        /// <param name="productCode">商品编码（可选）</param>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <returns>价格统计</returns>
        Task<ApiResponse<Dictionary<string, decimal?>>> GetSetProductPriceStatisticsAsync(string? productCode = null, string? supplierCode = null);
    }
}
