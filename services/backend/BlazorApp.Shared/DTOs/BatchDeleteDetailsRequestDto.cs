namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 批量删除货柜明细请求 DTO（React）
    /// </summary>
    public class BatchDeleteDetailsRequestDto
    {
        /// <summary>
        /// 明细的 HGUID/DetailCode 列表
        /// </summary>
        public List<string> Hguids { get; set; } = new List<string>();
    }
}