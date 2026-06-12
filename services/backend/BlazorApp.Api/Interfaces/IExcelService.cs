using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// Excel处理服务接口
    /// </summary>
    public interface IExcelService
    {
        /// <summary>
        /// 解析Excel文件并批量添加到购物车
        /// </summary>
        /// <param name="request">Excel上传请求</param>
        /// <returns>处理结果</returns>
        Task<ExcelUploadResponseDto> ProcessExcelUploadAsync(ExcelUploadRequestDto request);

        /// <summary>
        /// 解析Excel文件内容
        /// </summary>
        /// <param name="excelData">Excel文件数据（Base64编码）</param>
        /// <param name="fileName">文件名</param>
        /// <returns>解析结果</returns>
        Task<List<ExcelParseItemDto>> ParseExcelAsync(string excelData, string fileName);

        /// <summary>
        /// 批量查询商品信息
        /// </summary>
        /// <param name="itemNumbers">货号列表</param>
        /// <param name="userGuid">用户GUID</param>
        /// <returns>查询结果</returns>
        Task<BatchQueryProductsResponseDto> BatchQueryProductsAsync(List<string> itemNumbers, string userGuid);
    }
}