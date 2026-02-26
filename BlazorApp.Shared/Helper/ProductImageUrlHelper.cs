using System;

namespace BlazorApp.Shared.Helper
{
    /// <summary>
    /// 商品图片地址生成工具。
    /// </summary>
    public static class ProductImageUrlHelper
    {
        /// <summary>
        /// 图片基础路径前缀。
        /// 示例：
        /// https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/ + 货号 + .jpg
        /// </summary>
        private const string BaseImageUrl =
            "https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/";

        /// <summary>
        /// 根据商品货号生成图片地址。
        /// </summary>
        /// <param name="itemNumber">商品货号（例如：YW2000001 或 HB内部货号等）</param>
        /// <returns>完整的图片地址。</returns>
        public static string GenerateImageUrl(string itemNumber)
        {
            if (string.IsNullOrWhiteSpace(itemNumber))
                throw new ArgumentException("商品货号不能为空", nameof(itemNumber));

            var trimmed = itemNumber.Trim();
            return $"{BaseImageUrl}{trimmed}.jpg";
        }

        /// <summary>
        /// 在保存商品图片时使用：
        /// 如果当前图片地址为空，则按约定规则使用货号生成默认图片地址；
        /// 如果已有图片地址，则直接返回原值。
        /// </summary>
        /// <param name="currentImageUrl">当前图片地址（可能为空或空字符串）。</param>
        /// <param name="itemNumber">商品货号。</param>
        /// <returns>最终使用的图片地址（可能为 null，如果货号本身也无效）。</returns>
        public static string? EnsureImageUrl(string? currentImageUrl, string itemNumber)
        {
            // 已有图片地址且是完整 http(s) 地址，则直接返回
            if (!string.IsNullOrWhiteSpace(currentImageUrl))
            {
                var trimmed = currentImageUrl.Trim();
                if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }
            }

            // 没有图片且货号无效，则返回 null，由调用方决定如何处理
            if (string.IsNullOrWhiteSpace(itemNumber))
                return null;

            // 按规则生成默认图片地址
            return GenerateImageUrl(itemNumber);
        }
    }
}

