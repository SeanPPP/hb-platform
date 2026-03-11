namespace BlazorApp.Api.Models
{
    public class TencentCloudSettings
    {
        public string SecretId { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string BucketName { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
}
