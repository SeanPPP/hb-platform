using BlazorApp.Shared.Helper;
using SqlSugar;
using UUIDNext;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 产品实体类，表示系统中的产品信息
    /// </summary>
    [SugarTable("Product")]
    public class Product : BaseEntity
    {
        /// <summary>
        /// 产品全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false, Length = 50)]
        public string UUID { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 产品类别GUID
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ProductCode { get; set; }

        /// <summary>
        /// 产品类别GUID
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 本地供应商代码 不是中国供应商代码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 产品货号
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 条码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? Barcode { get; set; }

        /// <summary>
        /// 产品名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// 英文名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        public string? EnglishName { get; set; }

        /// <summary>
        /// 产品类型
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public int? ProductType { get; set; }

        /// <summary>
        ///
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// 采购价格/进口价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 零售价格
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 是否自动定价
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsAutoPricing { get; set; } = false;

        /// <summary>
        /// 产品图片路径
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 是否激活状态
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 是否特殊产品
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsSpecialProduct { get; set; } = false;

        [SugarColumn(IsNullable = true, Length = 50)]
        public string? WarehouseCategoryGUID { get; set; }

        // 导航属性
        [Navigate(NavigateType.OneToOne, nameof(WarehouseCategoryGUID))]
        public WarehouseCategory? WarehouseCategory { get; set; }
    }
}
