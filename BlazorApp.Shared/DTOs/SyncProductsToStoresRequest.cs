using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class SyncProductsToStoresRequest
    {
        [Required(ErrorMessage = "商品编码列表不能为空")]
        public List<string> ProductCodes { get; set; } = new();

        [Required(ErrorMessage = "目标分店编码列表不能为空")]
        public List<string> StoreCodes { get; set; } = new();

        /// <summary>
        /// 前端兼容字段选择列表；当传入该列表时，只同步这里勾选的字段。
        /// </summary>
        public List<string> Fields { get; set; } = new();

        public bool SyncPurchasePrice { get; set; } = true;

        public bool SyncRetailPrice { get; set; } = true;

        public bool SyncIsAutoPricing { get; set; } = true;

        public bool SyncIsSpecialProduct { get; set; } = true;

        public bool SyncDiscountRate { get; set; } = true;

        /// <summary>
        /// 兼容前端只传 fields 的场景，避免 bool 默认值把未勾选字段一并同步。
        /// </summary>
        public SyncProductsToStoresRequest NormalizeFieldSelection()
        {
            if (Fields == null || Fields.Count == 0)
            {
                return this;
            }

            var selectedFields = Fields
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            SyncPurchasePrice = selectedFields.Contains("purchasePrice");
            SyncRetailPrice = selectedFields.Contains("retailPrice");
            SyncIsAutoPricing = selectedFields.Contains("isAutoPricing");
            SyncIsSpecialProduct = selectedFields.Contains("isSpecialProduct");
            // 商品管理页当前没有折扣率选项，避免 fields 契约误导调用方以为已支持该字段。
            SyncDiscountRate = false;
            return this;
        }
    }
}
