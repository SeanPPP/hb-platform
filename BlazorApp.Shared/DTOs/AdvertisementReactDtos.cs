namespace BlazorApp.Shared.DTOs
{
    public class AdvertisementGridRequestDto : GridRequestDto
    {
        public string? Title { get; set; }
        public string? StoreCode { get; set; }
        public string? MediaType { get; set; }
        public bool? IsEnabled { get; set; }
        public DateTime? EffectiveStart { get; set; }
        public DateTime? EffectiveEnd { get; set; }
        public int? PageNumber { get; set; }
    }

    public class AdvertisementListDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string MediaUrl { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string ObjectKey { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime EffectiveStart { get; set; }
        public DateTime EffectiveEnd { get; set; }
        public bool IsEnabled { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
        public List<AdvertisementStoreItemDto> Stores { get; set; } = new();
    }

    public class AdvertisementDetailDto : AdvertisementListDto
    {
    }

    public class AdvertisementStoreItemDto
    {
        public string StoreCode { get; set; } = string.Empty;
    }

    public class CreateAdvertisementDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string MediaUrl { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string ObjectKey { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime EffectiveStart { get; set; }
        public DateTime EffectiveEnd { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int SortOrder { get; set; }
        public List<AdvertisementStoreItemDto> Stores { get; set; } = new();
    }

    public class UpdateAdvertisementDto : CreateAdvertisementDto
    {
    }

    public class AdvertisementEnableRequestDto
    {
        public bool IsEnabled { get; set; }
    }

    public class AdvertisementUploadSignatureRequestDto
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }

    public class AdvertisementUploadSignatureResponseDto
    {
        public string ObjectKey { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string UploadUrl { get; set; } = string.Empty;
        public string MediaUrl { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
    }
}
