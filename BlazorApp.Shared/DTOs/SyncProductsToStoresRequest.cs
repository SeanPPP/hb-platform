using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    public class SyncProductsToStoresRequest
    {
        [Required(ErrorMessage = "商品编码列表不能为空")]
        public List<string> ProductCodes { get; set; } = new();

        [Required(ErrorMessage = "目标分店编码列表不能为空")]
        public List<string> StoreCodes { get; set; } = new();

        public bool SyncPurchasePrice { get; set; } = true;

        public bool SyncRetailPrice { get; set; } = true;

        public bool SyncIsAutoPricing { get; set; } = true;

        public bool SyncIsSpecialProduct { get; set; } = true;

        public bool SyncDiscountRate { get; set; } = true;
    }
}
