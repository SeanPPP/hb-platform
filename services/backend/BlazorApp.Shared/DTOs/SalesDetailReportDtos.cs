namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 销售明细四栏的一致性读取结果。一次请求内各栏使用同一统计快照。
/// </summary>
public sealed class SalesDetailReportDto
{
    public SalesDetailSectionResultDto? Summary { get; set; }
    public SalesDetailSectionResultDto? Suppliers { get; set; }
    public SalesDetailSectionResultDto? Branches { get; set; }
    public SalesDetailSectionResultDto? Products { get; set; }
}
