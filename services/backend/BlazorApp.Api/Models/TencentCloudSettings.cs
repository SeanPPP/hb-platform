namespace BlazorApp.Api.Models
{
    public class TencentCloudSettings
    {
        public string SecretId { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;

        /// <summary>
        /// 仓库图片上传专用桶（默认 hotbargain-yw-2023-1300114625，ap-shanghai）
        /// </summary>
        public string ImageBucketName { get; set; } = string.Empty;

        /// <summary>
        /// 仓库图片上传专用区域
        /// </summary>
        public string ImageRegion { get; set; } = string.Empty;
    }
}
