namespace BlazorApp.Shared.DTOs;

/// <summary>
/// 仓库商品价格批量同步请求。
/// </summary>
public sealed class WarehouseStorePriceSyncRequestDto
{
    public List<string> ProductCodes { get; set; } = new();

    public bool ApplyToAllProducts { get; set; }

    public List<string> TargetStoreCodes { get; set; } = new();

    public bool SyncToHq { get; set; }
}

/// <summary>
/// 可接收仓库价格的本地分店。
/// </summary>
public sealed class WarehouseStorePriceSyncTargetStoreDto
{
    public string StoreCode { get; set; } = string.Empty;

    public string StoreName { get; set; } = string.Empty;
}

/// <summary>
/// 仓库价格同步的逐商品或阶段错误。
/// </summary>
public sealed class WarehouseStorePriceSyncErrorDto
{
    public string Stage { get; set; } = string.Empty;

    public string? ProductCode { get; set; }

    public string? StoreCode { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 仓库商品价格批量同步结果。
/// </summary>
public sealed class WarehouseStorePriceSyncResultDto
{
    public int RequestedProductCount { get; set; }

    public int EligibleProductCount { get; set; }

    public int SkippedProductCount { get; set; }

    public int TargetStoreCount { get; set; }

    public int LocalCreatedCount { get; set; }

    public int LocalUpdatedCount { get; set; }

    public int HqCreatedCount { get; set; }

    public int HqUpdatedCount { get; set; }

    public int HqProvisionedProductCount { get; set; }

    public bool LocalCommitted { get; set; }

    public bool? HqSucceeded { get; set; }

    public List<string> TargetStoreCodes { get; set; } = new();

    public List<WarehouseStorePriceSyncErrorDto> Errors { get; set; } = new();
}

public static class WarehouseStorePriceSyncJobStatusConstants
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string PartiallySucceeded = "PartiallySucceeded";
    public const string Failed = "Failed";
}

/// <summary>
/// 进程内仓库价格同步任务快照。
/// </summary>
public sealed class WarehouseStorePriceSyncJobDto
{
    public string JobId { get; set; } = string.Empty;

    public string Status { get; set; } = WarehouseStorePriceSyncJobStatusConstants.Pending;

    public bool IsDuplicateRequest { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? Message { get; set; }

    public WarehouseStorePriceSyncResultDto? Result { get; set; }
}

/// <summary>
/// HQ 分店预校验结果。
/// </summary>
public sealed class WarehouseStorePriceHqValidationResultDto
{
    public List<string> CanonicalTargetStoreCodes { get; set; } = new();

    public List<WarehouseStorePriceSyncErrorDto> Errors { get; set; } = new();
}

/// <summary>
/// 传给 HQ 专用同步路径的仓库价格快照。
/// </summary>
public sealed class WarehouseStorePriceHqProductDto
{
    public string ProductCode { get; set; } = string.Empty;

    public decimal ImportPrice { get; set; }

    public decimal OemPrice { get; set; }
}

/// <summary>
/// HQ 专用同步请求，仅由后端内部构造。
/// </summary>
public sealed class WarehouseStorePriceHqSyncRequestDto
{
    public List<WarehouseStorePriceHqProductDto> Products { get; set; } = new();

    public List<string> TargetStoreCodes { get; set; } = new();

    public string UpdatedBy { get; set; } = "system";
}

/// <summary>
/// HQ 专用同步结果；新增和更新数量只统计分店零售价记录。
/// </summary>
public sealed class WarehouseStorePriceHqSyncResultDto
{
    public int HqCreatedCount { get; set; }

    public int HqUpdatedCount { get; set; }

    public int HqProvisionedProductCount { get; set; }

    public List<WarehouseStorePriceSyncErrorDto> Errors { get; set; } = new();
}
