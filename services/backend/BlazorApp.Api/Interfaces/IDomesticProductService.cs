using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 国内商品管理服务接口
    /// </summary>
    public interface IDomesticProductService
    {
        /// <summary>
        /// 获取国内商品分页列表
        /// </summary>
        /// <param name="query">查询条件</param>
        /// <returns>分页结果</returns>
        Task<ApiResponse<PagedResult<DomesticProductDto>>> GetDomesticProductsAsync(DomesticProductQueryDto query);

        /// <summary>
        /// 获取国内商品分页列表（高级过滤）
        /// </summary>
        /// <param name="query">高级查询条件</param>
        /// <returns>分页结果</returns>
        Task<ApiResponse<PagedResult<DomesticProductDto>>> GetDomesticProductsAdvancedAsync(DomesticProductAdvancedQueryDto query);

        /// <summary>
        /// 获取字段信息（用于构建过滤界面）
        /// </summary>
        /// <returns>字段信息列表</returns>
        Task<ApiResponse<List<BlazorApp.Shared.DTOs.FieldInfo>>> GetFieldInfoAsync();

        /// <summary>
        /// 根据编码获取国内商品详情
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>国内商品详情</returns>
        Task<ApiResponse<DomesticProductDetailDto>> GetDomesticProductByCodeAsync(string productCode);

        /// <summary>
        /// 根据供应商编码获取商品列表
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <returns>商品列表</returns>
        Task<ApiResponse<List<DomesticProductDto>>> GetProductsBySupplierCodeAsync(string supplierCode);

        /// <summary>
        /// 获取启用的商品列表（用于下拉选择）
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <param name="productType">商品类型（可选）</param>
        /// <returns>启用的商品列表</returns>
        Task<ApiResponse<List<DomesticProductDto>>> GetActiveProductsAsync(string? supplierCode = null, int? productType = null);

        /// <summary>
        /// 创建国内商品
        /// </summary>
        /// <param name="dto">创建国内商品DTO</param>
        /// <returns>创建的国内商品</returns>
        Task<ApiResponse<DomesticProductDto>> CreateDomesticProductAsync(CreateDomesticProductDto dto);

        /// <summary>
        /// 更新国内商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="dto">更新国内商品DTO</param>
        /// <returns>更新的国内商品</returns>
        Task<ApiResponse<DomesticProductDto>> UpdateDomesticProductAsync(string productCode, UpdateDomesticProductDto dto);

        /// <summary>
        /// 删除国内商品
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> DeleteDomesticProductAsync(string productCode);

        /// <summary>
        /// 切换国内商品状态
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <param name="isActive">是否启用</param>
        /// <returns>更新的国内商品</returns>
        Task<ApiResponse<DomesticProductDto>> ToggleProductStatusAsync(string productCode, bool isActive);

        /// <summary>
        /// 检查HB货号是否存在
        /// </summary>
        /// <param name="hbProductNo">HB货号</param>
        /// <param name="excludeProductCode">排除的商品编码（用于更新时）</param>
        /// <returns>是否存在</returns>
        Task<ApiResponse<bool>> CheckHBProductNoExistsAsync(string hbProductNo, string? excludeProductCode = null);

        /// <summary>
        /// 检查条形码是否存在
        /// </summary>
        /// <param name="barcode">条形码</param>
        /// <param name="excludeProductCode">排除的商品编码（用于更新时）</param>
        /// <returns>是否存在</returns>
        Task<ApiResponse<bool>> CheckBarcodeExistsAsync(string barcode, string? excludeProductCode = null);

        /// <summary>
        /// 生成下一个商品货号
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="prefixCode">前缀代码（可选）</param>
        /// <returns>生成的商品货号</returns>
        Task<ApiResponse<string>> GenerateNextProductNoAsync(string supplierCode, string? prefixCode = null);

        /// <summary>
        /// 生成套装商品货号
        /// </summary>
        /// <param name="baseProductNo">基础商品货号</param>
        /// <param name="setType">套装类型（套10、套15等）</param>
        /// <param name="setIndex">套装序号（1-N）</param>
        /// <returns>生成的套装货号</returns>
        Task<ApiResponse<string>> GenerateNextSetProductNoAsync(string baseProductNo, int setType, int setIndex);

        /// <summary>
        /// 生成商品条形码
        /// </summary>
        /// <param name="supplierCode">供应商编码</param>
        /// <param name="productType">商品类型</param>
        /// <returns>生成的条形码</returns>
        Task<ApiResponse<string>> GenerateProductBarcodeAsync(string supplierCode, int productType);

        /// <summary>
        /// 批量验证商品数据
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>验证结果</returns>
        Task<ApiResponse<object>> BatchValidateProductsAsync(BatchCreateDomesticProductDto dto);

        /// <summary>
        /// 批量创建国内商品
        /// </summary>
        /// <param name="dto">批量创建DTO</param>
        /// <returns>创建结果</returns>
        Task<ApiResponse<List<DomesticProductDto>>> BatchCreateDomesticProductsAsync(BatchCreateDomesticProductDto dto);

        /// <summary>
        /// 批量删除国内商品
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> BatchDeleteDomesticProductsAsync(List<string> productCodes);

        /// <summary>
        /// 批量更新商品状态
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <param name="isActive">是否启用</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<bool>> BatchUpdateProductStatusAsync(List<string> productCodes, bool isActive);

        /// <summary>
        /// 根据商品类型获取统计信息
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <returns>统计信息</returns>
        Task<ApiResponse<Dictionary<int, int>>> GetProductTypeStatisticsAsync(string? supplierCode = null);

        /// <summary>
        /// 获取商品价格统计
        /// </summary>
        /// <param name="supplierCode">供应商编码（可选）</param>
        /// <param name="productType">商品类型（可选）</param>
        /// <returns>价格统计</returns>
        Task<ApiResponse<Dictionary<string, decimal?>>> GetProductPriceStatisticsAsync(string? supplierCode = null, int? productType = null);

        /// <summary>
        /// 批量检测商品信息 - 通过货号和供应商编码匹配现有数据
        /// </summary>
        /// <param name="dto">批量检测DTO</param>
        /// <returns>检测结果</returns>
        Task<ApiResponse<List<BatchProductDetectionResultDto>>> BatchDetectProductsAsync(BatchProductDetectionDto dto);

        /// <summary>
        /// 批量创建和更新商品
        /// </summary>
        /// <param name="dto">批量操作DTO</param>
        /// <returns>操作结果</returns>
        Task<ApiResponse<BatchProductOperationResultDto>> BatchCreateAndUpdateProductsAsync(BatchProductOperationDto dto);

        // 已移除自动导入方法，避免绕过前端确认流程

        /// <summary>
        /// 修复重复的图片URL
        /// </summary>
        /// <param name="dryRun">是否仅模拟运行（不实际修改数据库）</param>
        /// <returns>修复结果统计</returns>
        Task<ApiResponse<ImageUrlFixResult>> FixDuplicateImageUrlsAsync(bool dryRun = true);

        // ==================== React AG Grid 专用方法 ====================

        /// <summary>
        /// 获取 AG Grid 表格数据（支持服务端过滤、排序、分页）
        /// </summary>
        /// <param name="request">AG Grid 请求</param>
        /// <returns>AG Grid 响应</returns>
        Task<GridResponseDto<DomesticProductDto>> GetGridDataAsync(GridRequestDto request);

        /// <summary>
        /// 批量删除国内商品（通过商品编码）
        /// </summary>
        /// <param name="productCodes">商品编码列表</param>
        /// <returns>删除结果</returns>
        Task<ApiResponse<bool>> BatchDeleteAsync(List<string> productCodes);

        /// <summary>
        /// 获取套装商品信息列表
        /// </summary>
        /// <param name="productCode">套装商品编码</param>
        /// <returns>套装信息列表</returns>
        Task<ApiResponse<List<DomesticSetProductDto>>> GetSetItemsAsync(string productCode);

        /// <summary>
        /// 获取商品数量信息（套装/多码商品的数量统计）
        /// </summary>
        /// <param name="productCode">商品编码</param>
        /// <returns>商品数量信息</returns>
        Task<ApiResponse<int>> GetProductQuantityAsync(string productCode);

        /// <summary>
        /// 更新套装商品信息
        /// </summary>
        /// <param name="productCode">套装商品编码</param>
        /// <param name="items">套装信息列表</param>
        /// <returns>更新结果</returns>
        Task<ApiResponse<bool>> UpdateSetItemsAsync(string productCode, List<SetItemUpdateDto> items);

        /// <summary>
        /// 批量创建套装商品（统一规格）
        /// </summary>
        /// <param name="dto">批量创建套装商品DTO</param>
        /// <returns>创建结果</returns>
        Task<ApiResponse<BatchCreateSetProductsResultDto>> BatchCreateSetProductsAsync(BatchCreateSetProductsDto dto);
    }
}
