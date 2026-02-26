using System;
using System.Text.RegularExpressions;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// 图片URL辅助工具类
    /// 用于处理和修复重复的图片URL问题
    /// </summary>
    public static class ImageUrlHelper
    {
        private static readonly string[] CDN_DOMAINS = new[]
        {
            "hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com",
            "hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com"
        };

        /// <summary>
        /// 检查字符串是否为URL
        /// </summary>
        public static bool IsUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 修复重复的URL
        /// 例如：https://domain.com/path/https://domain.com/path/file.jpg
        /// 修复为：https://domain.com/path/file.jpg
        /// </summary>
        public static string? FixDuplicateUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            // 检查是否包含重复的http协议头
            var httpCount = Regex.Matches(url, @"https?://", RegexOptions.IgnoreCase).Count;
            
            if (httpCount <= 1)
            {
                // 只有一个http协议头，没有重复问题
                return url;
            }

            // 有多个http协议头，找到最后一个http://或https://的位置
            int lastHttpIndex = -1;
            int lastHttpsIndex = url.LastIndexOf("https://", StringComparison.OrdinalIgnoreCase);
            int lastHttpIndex2 = url.LastIndexOf("http://", StringComparison.OrdinalIgnoreCase);
            
            // 取最后出现的位置
            lastHttpIndex = Math.Max(lastHttpsIndex, lastHttpIndex2);
            
            if (lastHttpIndex > 0)
            {
                // 从最后一个http/https位置开始到结尾就是正确的URL
                return url.Substring(lastHttpIndex);
            }

            return url;
        }

        /// <summary>
        /// 从HB货号生成图片URL
        /// 确保货号不是完整URL，避免重复拼接
        /// </summary>
        public static string? GenerateImageUrl(string? productNo)
        {
            if (string.IsNullOrWhiteSpace(productNo))
                return null;

            // 如果已经是完整的URL，直接返回
            if (IsUrl(productNo))
                return productNo;

            // 使用默认CDN地址生成URL
            return $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{productNo}.jpg";
        }

        /// <summary>
        /// 从图片URL中提取货号
        /// </summary>
        public static string? ExtractProductNoFromUrl(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return null;

            // 先修复可能的重复URL
            imageUrl = FixDuplicateUrl(imageUrl);

            if (string.IsNullOrWhiteSpace(imageUrl))
                return null;

            try
            {
                // 提取文件名（不包含扩展名）
                var uri = new Uri(imageUrl);
                var fileName = System.IO.Path.GetFileNameWithoutExtension(uri.AbsolutePath);
                return fileName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 验证并修复商品图片URL
        /// </summary>
        public static string? ValidateAndFixImageUrl(string? imageUrl, string? hbProductNo)
        {
            // 如果有图片URL，先尝试修复重复
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                var fixedUrl = FixDuplicateUrl(imageUrl);
                if (!string.IsNullOrWhiteSpace(fixedUrl))
                    return fixedUrl;
            }

            // 如果没有图片URL，尝试从货号生成
            if (!string.IsNullOrWhiteSpace(hbProductNo))
            {
                return GenerateImageUrl(hbProductNo);
            }

            return null;
        }
    }
}

