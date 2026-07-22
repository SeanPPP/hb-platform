namespace BlazorApp.Shared.DTOs
{
    public class WpfAppReleaseDto
    {
        public Guid Id { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string CosObjectKey { get; set; } = string.Empty;
        public string InstallerType { get; set; } = string.Empty;
        public string? InstallerArguments { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool IsActive { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsRollback { get; set; }
        public bool ForceUpdate { get; set; }
        public string? MinimumSupportedVersion { get; set; }
        public string? TargetVersion { get; set; }
        public string TargetScope { get; set; } = "all";
        public List<string> TargetStoreGuids { get; set; } = new();
        public List<int> TargetDeviceRegistrationIds { get; set; } = new();
        public List<WpfUpdateTargetStoreSummaryDto> TargetStoreSummaries { get; set; } = new();
        public List<WpfUpdateTargetDeviceSummaryDto> TargetDeviceSummaries { get; set; } = new();
        public DateTime? PolicyUpdatedAt { get; set; }
        public string? PolicyUpdatedBy { get; set; }
        public DateTime PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class WpfAppReleaseQuery
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? Channel { get; set; } = "production";
        public bool IncludeDisabled { get; set; }
    }

    public class WpfAppReleaseCreateRequest
    {
        public string? Channel { get; set; } = "production";
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        // 中文注释：创建发布时服务端会忽略调用方传入的 CosObjectKey，并按 channel/version/fileName 自行生成确定性对象 key；保留字段仅用于兼容现有前端和调用方。
        public string? CosObjectKey { get; set; }
        public string? InstallerType { get; set; } = "exe";
        public string? InstallerArguments { get; set; }
        public string? ReleaseNotes { get; set; }
        public DateTime? PublishedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class WpfAppReleaseUpdateRequest
    {
        public string? DownloadUrl { get; set; }
        public string? Sha256 { get; set; }
        public string? InstallerType { get; set; }
        public string? InstallerArguments { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool? IsActive { get; set; }
    }

    public class WpfUpdatePolicyDto
    {
        public Guid Id { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string TargetVersion { get; set; } = string.Empty;
        public string? MinimumSupportedVersion { get; set; }
        public bool ForceUpdate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string TargetScope { get; set; } = "all";
        public List<string> TargetStoreGuids { get; set; } = new();
        public List<int> TargetDeviceRegistrationIds { get; set; } = new();
        public List<WpfUpdateTargetStoreSummaryDto> TargetStoreSummaries { get; set; } = new();
        public List<WpfUpdateTargetDeviceSummaryDto> TargetDeviceSummaries { get; set; } = new();
        public DateTime? PolicyUpdatedAt { get; set; }
        public string? PolicyUpdatedBy { get; set; }
    }

    public class WpfUpdatePolicyRequest
    {
        public string? Channel { get; set; } = "production";
        public string TargetVersion { get; set; } = string.Empty;
        public string MinimumSupportedVersion { get; set; } = string.Empty;
        public bool ForceUpdate { get; set; }
        public bool RollbackConfirmed { get; set; }
        public string? TargetScope { get; set; } = "all";
        public List<string> TargetStoreGuids { get; set; } = new();
        public List<int> TargetDeviceRegistrationIds { get; set; } = new();
    }

    public class WpfUpdateCheckResponse
    {
        public bool UpdateAvailable { get; set; }
        public bool ForceUpdate { get; set; }
        public bool IsRollback { get; set; }
        public bool CheckFailed { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string? TargetVersion { get; set; }
        public string? MinimumSupportedVersion { get; set; }
        public string? DownloadUrl { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public string? Sha256 { get; set; }
        public string? InstallerType { get; set; }
        public string? InstallerArguments { get; set; }
        public string? ReleaseNotes { get; set; }
    }

    /// <summary>
    /// 经中心端严格设备身份校验后的更新检查上下文，不直接由外部请求绑定。
    /// </summary>
    public class WpfUpdateCheckDeviceIdentity
    {
        public int DeviceRegistrationId { get; set; }
        public string? StoreCode { get; set; }
    }

    public class WpfUpdateTargetStoreOptionDto
    {
        public string StoreGuid { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string StoreName { get; set; } = string.Empty;
    }

    public class WpfUpdateTargetStoreOptionsResponse
    {
        public List<WpfUpdateTargetStoreOptionDto> Items { get; set; } = new();
    }

    /// <summary>
    /// 已选分店的只读展示摘要。分店已停用或历史记录不可再匹配时，仍保留 StoreGuid。
    /// </summary>
    public class WpfUpdateTargetStoreSummaryDto
    {
        public string StoreGuid { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
    }

    /// <summary>
    /// 已选设备的只读展示摘要；绝不包含硬件识别码或设备授权码。
    /// </summary>
    public class WpfUpdateTargetDeviceSummaryDto
    {
        public int DeviceRegistrationId { get; set; }
        public string? SystemDeviceNumber { get; set; }
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? Remarks { get; set; }
    }

    public class WpfUpdateTargetDeviceOptionDto
    {
        public int DeviceRegistrationId { get; set; }
        public string SystemDeviceNumber { get; set; } = string.Empty;
        public string? StoreCode { get; set; }
        public string? StoreName { get; set; }
        public string? Remarks { get; set; }
    }

    public class WpfAppReleaseUploadInitRequest
    {
        public string? Channel { get; set; } = "production";
        public string Version { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public long FileSize { get; set; }
        public string? Sha256 { get; set; }
        public bool Multipart { get; set; }
    }

    public class WpfAppReleaseUploadInitResponse
    {
        public string ObjectKey { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
        public DirectUploadSignature? DirectUpload { get; set; }
        public MultipartUploadInit? MultipartUpload { get; set; }
    }
}
