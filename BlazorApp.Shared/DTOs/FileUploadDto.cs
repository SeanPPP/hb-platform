namespace BlazorApp.Shared.DTOs
{
    public class UploadResult
    {
        public string ObjectKey { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }

    public class UploadProgress
    {
        public long UploadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double ProgressPercentage => TotalBytes > 0 ? (double)UploadedBytes / TotalBytes * 100 : 0;
    }

    public class DirectUploadRequest
    {
        public string? ObjectKey { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";
        public long FileSize { get; set; }
    }

    public class DirectUploadSignature
    {
        public string Url { get; set; } = string.Empty;
        public string ObjectKey { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new();
    }

    public class MultipartUploadInit
    {
        public string UploadId { get; set; } = string.Empty;
        public string ObjectKey { get; set; } = string.Empty;
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
    }

    public class PartUploadRequest
    {
        public string ObjectKey { get; set; } = string.Empty;
        public string UploadId { get; set; } = string.Empty;
        public int PartNumber { get; set; }
    }

    public class PartUploadSignature
    {
        public string Url { get; set; } = string.Empty;
        public int PartNumber { get; set; }
    }

    public class PartETag
    {
        public int PartNumber { get; set; }
        public string ETag { get; set; } = string.Empty;
    }

    public class CompleteMultipartRequest
    {
        public string ObjectKey { get; set; } = string.Empty;
        public string UploadId { get; set; } = string.Empty;
        public List<PartETag> Parts { get; set; } = new();
        public long FileSize { get; set; }
    }

    // ========== 批量图片上传 ==========

    public class BatchPresignedUrlRequest
    {
        public List<PresignedFileInfo> Files { get; set; } = new();
    }

    public class PresignedFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentType { get; set; } = "image/jpeg";
    }

    public class BatchPresignedUrlResult
    {
        public List<PresignedUrlItem> Results { get; set; } = new();
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class PresignedUrlItem
    {
        public string ObjectKey { get; set; } = string.Empty;
        public string PresignedUrl { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? Error { get; set; }
    }
}
