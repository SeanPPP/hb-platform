using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 仓库商品批量更新的图片地址选项。
/// </summary>
public sealed class WarehouseProductBatchUpdateOptionsDto
{
    public bool GenerateImageUrls { get; set; }

    public string? ImageBaseUrl { get; set; }

    public bool SyncImageToHq { get; set; }
}

/// <summary>
/// 已在本地成功落库、可继续同步至 HQ 的图片地址。
/// </summary>
public sealed class ProductHqImageUpdateItemDto
{
    public string ProductCode { get; set; } = string.Empty;

    public string ImageUrl { get; set; } = string.Empty;
}

/// <summary>
/// HQ 商品图片专用同步结果。
/// </summary>
public sealed class ProductHqImageSyncResultDto
{
    public bool Requested { get; set; }

    public bool Success { get; set; } = true;

    public int UpdatedCount { get; set; }

    public int FailedCount { get; set; }

    public string? ErrorCode { get; set; }

    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// 仓库商品批量更新结果；不污染其他批量入口共用的结果 DTO。
/// </summary>
public sealed class WarehouseProductBatchUpdateResultDto : BatchOperationResultDto
{
    public int ImageUpdatedCount { get; set; }

    public ProductHqImageSyncResultDto HqImageSync { get; set; } = new();

    /// <summary>
    /// 仅供控制器在本地事务提交后继续同步 HQ，不向前端输出。
    /// </summary>
    [JsonIgnore]
    public List<ProductHqImageUpdateItemDto> ImageUpdates { get; set; } = new();
}

/// <summary>
/// 仓库商品批量修改后台任务状态。
/// </summary>
public static class WarehouseProductBatchUpdateJobStatusConstants
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string PartiallySucceeded = "PartiallySucceeded";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

/// <summary>
/// 创建仓库商品批量修改后台任务的请求快照。
/// </summary>
public sealed class WarehouseProductBatchUpdateJobRequestDto
{
    public List<UpdateItemDto> Items { get; set; } = new();

    public bool? SyncStorePurchasePrice { get; set; }

    public bool GenerateImageUrls { get; set; }

    public string? ImageBaseUrl { get; set; }

    public bool SyncImageToHq { get; set; }
}

/// <summary>
/// 仓库商品批量修改后台任务快照。
/// </summary>
public sealed class WarehouseProductBatchUpdateJobDto
{
    public string JobId { get; set; } = string.Empty;

    public string OperationId { get; set; } = string.Empty;

    public string Status { get; set; } = WarehouseProductBatchUpdateJobStatusConstants.Queued;

    public bool IsDuplicateRequest { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? Message { get; set; }

    public WarehouseProductBatchUpdateResultDto? Result { get; set; }
}
