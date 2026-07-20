using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs;

public sealed class PreorderTemplateQueryDto
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Keyword { get; set; }
}

public sealed class SavePreorderTemplateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? Notes { get; set; }
    public int? ExpectedRevision { get; set; }
    public List<SavePreorderTemplateItemDto> Items { get; set; } = new();
    public List<string> StoreGuids { get; set; } = new();
}

public sealed class SavePreorderTemplateItemDto
{
    public string ProductCode { get; set; } = string.Empty;
    public int MinimumOrderQuantity { get; set; }
    public int SortOrder { get; set; }
}

public class PreorderTemplateSummaryDto
{
    public string TemplateGuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int Revision { get; set; }
    public string? Notes { get; set; }
    public int ItemCount { get; set; }
    public int StoreCount { get; set; }
    public int ActivationCount { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class PreorderTemplateDetailDto : PreorderTemplateSummaryDto
{
    public List<PreorderTemplateItemDto> Items { get; set; } = new();
    public List<PreorderStoreDto> Stores { get; set; } = new();
}

public sealed class PreorderTemplateItemDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImage { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal RetailPrice { get; set; }
    public int MinimumOrderQuantity { get; set; }
    public int SortOrder { get; set; }
}

public sealed class PreorderStoreDto
{
    public string StoreGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
}

public sealed class ResolvePreorderItemsRequestDto
{
    public List<ResolvePreorderItemRowDto> Rows { get; set; } = new();
}

public class ResolvePreorderItemRowDto
{
    public int LineNumber { get; set; }
    public string ItemNumber { get; set; } = string.Empty;
    public int MinimumOrderQuantity { get; set; }
}

public sealed class ResolvePreorderItemsResultDto
{
    public List<ResolvedPreorderItemRowDto> Rows { get; set; } = new();
    public bool IsValid => Rows.Count > 0 && Rows.All(row => row.Status == "Resolved");
}

public sealed class ResolvedPreorderItemRowDto : ResolvePreorderItemRowDto
{
    public string Status { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public string? ProductImage { get; set; }
    public decimal? ImportPrice { get; set; }
    public decimal? RetailPrice { get; set; }
}

public sealed class ActivatePreorderTemplateDto
{
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public DateOnly? EstimatedArrivalDate { get; set; }
    public int? ExpectedRevision { get; set; }
    public List<string>? StoreGuids { get; set; }
}

public sealed class ClosePreorderActivationDto
{
    public DateTime? EndAtUtc { get; set; }
}

public sealed class UpdatePreorderActivationStoresDto
{
    public List<string> ExpectedStoreGuids { get; set; } = new();
    public List<string> StoreGuids { get; set; } = new();
}

public sealed class UpdatePreorderActivationEstimatedArrivalDateDto
{
    public DateOnly? ExpectedEstimatedArrivalDate { get; set; }
    public DateOnly? EstimatedArrivalDate { get; set; }
}

public class PreorderActivationSummaryDto
{
    public string ActivationGuid { get; set; } = string.Empty;
    public string TemplateGuid { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public int PeriodNumber { get; set; }
    public string ActivationCode { get; set; } = string.Empty;
    public int SourceTemplateRevision { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }

    // 关键合同：历史批次也必须显式返回 null，不能被全局 WhenWritingNull 省略。
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateOnly? EstimatedArrivalDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TargetStoreCount { get; set; }
    public int RespondedStoreCount { get; set; }
}

public sealed class PreorderActivationDetailDto : PreorderActivationSummaryDto
{
    public string StoreCode { get; set; } = string.Empty;
    public string? OrderGuid { get; set; }
    public string? OrderNo { get; set; }
    public string? OrderStatus { get; set; }
    public int DraftRevision { get; set; }
    public string? WarehouseNotes { get; set; }
    public List<PreorderActivationItemDto> Items { get; set; } = new();
    public List<PreorderStoreDto> Stores { get; set; } = new();
}

public sealed class PreorderActivationItemDto
{
    public string ActivationItemGuid { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? ProductImage { get; set; }
    public decimal ImportPrice { get; set; }
    public decimal RetailPrice { get; set; }
    public int MinimumOrderQuantity { get; set; }
    public int PackCount { get; set; }
    public int OrderedQuantity { get; set; }
}

public class SavePreorderDraftDto
{
    public string StoreCode { get; set; } = string.Empty;
    public int ExpectedDraftRevision { get; set; }
    public List<SavePreorderDraftItemDto> Items { get; set; } = new();
}

public sealed class SubmitPreorderDto : SavePreorderDraftDto
{
    public bool ConfirmNoDemand { get; set; }
}

public sealed class SavePreorderDraftItemDto
{
    public string ActivationItemGuid { get; set; } = string.Empty;
    public int PackCount { get; set; }
}

public sealed class PreorderActiveResultDto
{
    public string StoreCode { get; set; } = string.Empty;
    public bool NormalOrderBlocked { get; set; }
    public List<PreorderActivationSummaryDto> Activations { get; set; } = new();
}

public sealed class PreorderOrderSummaryDto
{
    public string OrderGuid { get; set; } = string.Empty;
    public string ActivationGuid { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string StoreGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DraftRevision { get; set; }
    public string? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int SkuCount { get; set; }
    public long TotalPackCount { get; set; }
    public long TotalQuantity { get; set; }
    public decimal TotalImportAmount { get; set; }
    public decimal TotalRetailAmount { get; set; }
    public string? WarehouseNotes { get; set; }
}

public sealed class UpdatePreorderOrderStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string? WarehouseNotes { get; set; }
    public string? ExpectedStatus { get; set; }
    public int? ExpectedDraftRevision { get; set; }
}

public sealed class PreorderProductStatisticsDto
{
    public string ActivationItemGuid { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ItemNumber { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int MinimumOrderQuantity { get; set; }
    public int OrderingStoreCount { get; set; }
    public long TotalPackCount { get; set; }
    public long TotalQuantity { get; set; }
    public decimal TotalImportAmount { get; set; }
    public decimal TotalRetailAmount { get; set; }
}

public sealed class PreorderActivationStatisticsDto
{
    public string ActivationGuid { get; set; } = string.Empty;
    public int TargetStoreCount { get; set; }
    public int SubmittedCount { get; set; }
    public int NoDemandCount { get; set; }
    public int ProcessingCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public int PendingCount { get; set; }
    public List<PreorderProductStatisticsDto> Products { get; set; } = new();
    public List<PreorderOrderSummaryDto> Orders { get; set; } = new();
    public List<PreorderStoreProductQuantityDto> StoreProductQuantities { get; set; } = new();
    public List<PreorderStoreDto> PendingStores { get; set; } = new();
}

public sealed class PreorderStoreProductQuantityDto
{
    public string StoreGuid { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string ActivationItemGuid { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PackCount { get; set; }
    public int OrderedQuantity { get; set; }
}

public sealed class PreorderExportFileDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileName { get; set; } = string.Empty;
}

public sealed class PreorderGateResult
{
    public bool IsBlocked { get; set; }
    public int PendingCount { get; set; }
    public List<PreorderActivationSummaryDto> Activations { get; set; } = new();
}
