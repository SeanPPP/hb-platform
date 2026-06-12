using System;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 分店本地供应商进货单详情表
    /// 用途：记录进货单每一行商品的明细数据（商品、数量、价格、金额及定价相关信息）
    /// </summary>
    [SugarTable("StoreLocalSupplierInvoiceDetails")]
    public class StoreLocalSupplierInvoiceDetails : BaseEntity
    {
        /// <summary>
        /// 明细GUID（主键）
        /// 来源：HQ详情 HGUID 或本地自动生成
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string DetailGUID { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// 关联主表GUID（InvoiceGUID）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? InvoiceGUID { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? StoreCode { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 商品标签GUID
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ProductTagGUID { get; set; }

        /// <summary>
        /// 商品分类码GUID
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 分店商品编码（本地分店的商品编码）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? StoreProductCode { get; set; }

        //导航属性分店商品价格
        [Navigate(NavigateType.OneToOne, nameof(StoreProductCode), nameof(StoreRetailPrice.UUID))]
        public StoreRetailPrice? StoreProduct { get; set; }

        /// <summary>
        /// 商品编码（全局商品编码）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ProductCode { get; set; }

        //导航属性 已存在的商品信息
        [Navigate(
            NavigateType.OneToOne,
            nameof(ProductCode),
            nameof(DomesticSetProduct.ProductCode)
        )]
        public Product? Product { get; set; }

        /// <summary>
        /// 小货号/项目编号
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 条形码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? Barcode { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        public string? ProductName { get; set; }

        /// <summary>
        /// 规格
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        public string? Specification { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 20)]
        public string? Unit { get; set; }

        /// <summary>
        /// 数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? Quantity { get; set; }

        /// <summary>
        /// 上次进货价
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? LastPurchasePrice { get; set; }

        /// <summary>
        /// 本次进货价
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 零售价
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 合计金额（数量 * 进货价 或折扣后金额）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? Amount { get; set; }

        /// <summary>
        /// 已存在商品数（用于导入/去重统计）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? ExistingProductCount { get; set; }

        /// <summary>
        /// 条码状态：0=未检测，1=正常，2=异常
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? BarcodeStatus { get; set; }

        /// <summary>
        /// 条码匹配数量
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? BarcodeMatchCount { get; set; }

        /// <summary>
        /// 商品图片（存储路径或URL）
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 活动类型（如促销/满减/折扣）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? ActivityType { get; set; }

        /// <summary>
        /// 折扣率（0-1 或百分比）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? DiscountRate { get; set; }

        /// <summary>
        /// 是否自动定价（根据规则自动生成零售价）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public bool? AutoPricing { get; set; }

        /// <summary>
        /// 定价浮率（自动定价的浮动参数）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? PricingFloatRate { get; set; }

        /// <summary>
        /// 新自动零售价（根据规则计算）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? NewAutoRetailPrice { get; set; }

        /// <summary>
        /// 是否特殊商品（特殊规则/价格）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public bool? IsSpecialProduct { get; set; }

        /// <summary>
        /// 老库分店商品编码（历史兼容字段）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? OldStoreProductCode { get; set; }
    }
}
