using SqlSugar;
using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 仓库商品列表DTO（用于列表显示）
    /// </summary>
    public class WarehouseProductListDto
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;
      
       
        // === Product基础信息字段 ===


      

        /// <summary>
        /// 本地供应商代码（来自Product表）
        /// </summary>
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 国内供应商代码（来自 ChinaSupplier / DomesticProduct）
        /// </summary>
        public string? DomesticSupplierCode { get; set; }

        /// <summary>
        /// 国内供应商名称（来自 ChinaSupplier）
        /// </summary>
        public string? DomesticSupplierName { get; set; }

       
        /// <summary>
        /// 项目编号（来自Product表）
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 产品条码（来自Product表，作为主条码）
        /// </summary>
        public string? ProductBarcode { get; set; }

        /// <summary>
        /// 产品名称（来自Product表，作为主条码）
        /// </summary>
        public string? ProductBaseName { get; set; }


        /// <summary>
        /// 单件体积
        /// </summary>

        public decimal? Volume { get; set; }

        /// <summary>
        /// 产品类型（来自Product表）
        /// </summary>
        public int? ProductType { get; set; }

         /// <summary>
        /// 产品类型
        /// </summary>
        public string ProductTypeDisplay
        {
            get
            {
                if (ProductType == 0) return "单品";
                if (ProductType == 1) return "多码";
                if (ProductType == 2) return "套装";
                return "未知";
            }
        }

        /// <summary>
        /// 采购价格（来自Product表）
        /// </summary>
        public decimal? PurchasePrice { get; set; }

        /// <summary>
        /// 零售价格（来自Product表）
        /// </summary>
        public decimal? RetailPrice { get; set; }

        /// <summary>
        /// 是否自动定价（来自Product表）
        /// </summary>
        public bool IsAutoPricing { get; set; }

        /// <summary>
        /// 产品图片路径（来自Product表）
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 是否特殊产品（来自Product表）
        /// </summary>
        public bool IsSpecialProduct { get; set; }


        // === WarehouseProduct仓库信息字段 ===
        /// <summary>
        /// 产品类别GUID（来自 WarehouseProduct表）
        /// </summary>
        public string? ProductCategoryGUID { get; set; }

        /// <summary>
        /// 产品类别GUID（来自 WarehouseCategory表）
        /// </summary>
        public string? ProductCategoryName { get; set; }    
        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 中包数（来自Product表）
        /// </summary>
        public int? MiddlePackageQuantity { get; set; }

        /// <summary>
        /// PackingQty（前端字段名，来自 WarehouseProduct.PackingQuantity）
        /// </summary>
        public int? PackingQty { get; set; }

        /// <summary>
        /// 单件装箱数（后端字段名）
        /// </summary>
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 库存数量
        /// </summary>
        public int? StockQuantity { get; set; }

        /// <summary>
        /// 最小订货量
        /// </summary>
        public int? MinOrderQuantity { get; set; }

        /// <summary>
        /// 库存金额
        /// </summary>
        public decimal? StockValue { get; set; }

        /// <summary>
        /// 库存预警数
        /// </summary>
        public int? StockAlertQuantity { get; set; }

        /// <summary>
        /// 是否库存预警
        /// </summary>
        public bool IsStockAlert => StockQuantity.HasValue && StockAlertQuantity.HasValue && StockQuantity <= StockAlertQuantity;

        /// <summary>
        /// 使用状态
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 库存状态描述
        /// </summary>
        public string StockStatus
        {
            get
            {
                if (!StockQuantity.HasValue) return "未知";
                if (StockQuantity <= 0) return "缺货";
                if (IsStockAlert) return "库存预警";
                return "正常";
            }
        }

        /// <summary>
        /// 仓库位置列表
        /// </summary>
        public List<LocationDto>? Locations { get; set; }
    }

    /// <summary>
    /// 仓库商品分页结果DTO
    /// </summary>
    public class WarehouseProductPagedResultDto
    {
        /// <summary>
        /// 商品列表
        /// </summary>
        public List<WarehouseProductListDto> Items { get; set; } = new List<WarehouseProductListDto>();

        /// <summary>
        /// 总记录数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);

        /// <summary>
        /// 是否有上一页
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// 是否有下一页
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;

        /// <summary>
        /// 查询统计信息
        /// </summary>
        public WarehouseProductStatsDto? Stats { get; set; }
    }

    /// <summary>
    /// 仓库商品统计信息DTO
    /// </summary>
    public class WarehouseProductStatsDto
    {
        /// <summary>
        /// 总商品数量
        /// </summary>
        public int TotalProducts { get; set; }

        /// <summary>
        /// 总库存数量
        /// </summary>
        public int TotalStockQuantity { get; set; }

        /// <summary>
        /// 总库存金额
        /// </summary>
        public decimal TotalStockValue { get; set; }

        /// <summary>
        /// 库存预警商品数量
        /// </summary>
        public int StockAlertCount { get; set; }

        /// <summary>
        /// 缺货商品数量
        /// </summary>
        public int OutOfStockCount { get; set; }

        /// <summary>
        /// 启用商品数量
        /// </summary>
        public int ActiveProductCount { get; set; }
    }
}
