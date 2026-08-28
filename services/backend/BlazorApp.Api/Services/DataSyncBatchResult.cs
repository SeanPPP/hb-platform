namespace BlazorApp.Api.Services;

/// <summary>
/// 保留旧公开类型身份的兼容结果；DataSync 垂直切片使用自己的内部结果类型。
/// </summary>
public class BatchResult
{
    public bool IsSuccess { get; set; }
    public int ProcessedCount { get; set; }
    public int PageNumber { get; set; }
}
