using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 更新套装商品子项请求 DTO
    /// </summary>
    public class UpdateSetItemsRequestDto
    {
        /// <summary>
        /// 套装子项列表
        /// </summary>
        public List<SetItemUpdateDto> Items { get; set; } = new List<SetItemUpdateDto>();
    }

    /// <summary>
    /// 套装子项更新 DTO
    /// </summary>
    public class SetItemUpdateDto
    {
        /// <summary>
        /// 套装商品编码（SetProductCode）- 主键
        /// 如果为空则创建新记录，否则更新现有记录
        /// </summary>
        public string? SetProductCode { get; set; }

        /// <summary>
        /// 商品名称（仅用于显示）
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 套装货号
        /// </summary>
        public string? SetProductNo { get; set; }

        /// <summary>
        /// 套装条码
        /// </summary>
        public string? SetBarcode { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        public decimal? OEMPrice { get; set; }
    }
}

