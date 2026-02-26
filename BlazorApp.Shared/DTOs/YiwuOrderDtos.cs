namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 订单统计DTO
    /// </summary>
    public class OrderStatisticsDto
    {
        /// <summary>
        /// 订单总数
        /// </summary>
        public int TotalOrders { get; set; }

        /// <summary>
        /// 草稿订单数
        /// </summary>
        public int DraftOrders { get; set; }

        /// <summary>
        /// 已确认订单数
        /// </summary>
        public int ConfirmedOrders { get; set; }

        /// <summary>
        /// 已取消订单数
        /// </summary>
        public int CancelledOrders { get; set; }

        /// <summary>
        /// 总金额
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// 总体积
        /// </summary>
        public decimal TotalVolume { get; set; }

        /// <summary>
        /// 今日新增订单数
        /// </summary>
        public int TodayNewOrders { get; set; }

            /// <summary>
    /// 供应商数量
    /// </summary>
    public int SupplierCount { get; set; }
}

    /// <summary>
    /// 批量导出请求DTO
    /// </summary>
    public class BatchExportRequest
    {
        /// <summary>
        /// 要导出的订单ID列表
        /// </summary>
        public List<int> OrderIds { get; set; } = new();

        /// <summary>
        /// 最大并发数（默认10）
        /// </summary>
        public int? MaxConcurrency { get; set; } = 10;
    }

    /// <summary>
    /// 义乌订单创建DTO
    /// </summary>
    public class CreateYiwuOrderDto
    {
        /// <summary>
        /// 供应商编码
        /// </summary>
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remarks { get; set; }

        /// <summary>
        /// 订单明细列表
        /// </summary>
        public List<CreateYiwuOrderDetailDto> OrderDetails { get; set; } = new();
    }

    /// <summary>
    /// 义乌订单明细创建DTO
    /// </summary>
    public class CreateYiwuOrderDetailDto
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// HB货号
        /// </summary>
        public string? HBProductNo { get; set; }

        /// <summary>
        /// 条形码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? EnglishName { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 商品图片
        /// </summary>
        public string? ProductImage { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? UnitVolume { get; set; }

        /// <summary>
        /// 中包数量
        /// </summary>
        public int? MiddlePackQuantity { get; set; }

        /// <summary>
        /// 使用状态
        /// </summary>
        public int? UsageStatus { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 供应商名称
        /// </summary>
        public string? SupplierName { get; set; }

        /// <summary>
        /// 订货总数量
        /// </summary>
        public int? OrderQuantity { get; set; }

        /// <summary>
        /// 订货箱数
        /// </summary>
        public int? OrderBoxes { get; set; }
    }

    /// <summary>
    /// 义乌订单更新DTO
    /// </summary>
    public class UpdateYiwuOrderDto
    {
        /// <summary>
        /// 订单ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 供应商编码
        /// </summary>
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 订单状态 0:草稿 1:已确认 2:已取消
        /// </summary>
        public int? OrderStatus { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// PDA订单明细转义乌订单DTO
    /// </summary>
    public class PDAToYiwuOrderDto
    {
        /// <summary>
        /// 是否自动创建订单
        /// </summary>
        public bool AutoCreate { get; set; } = true;

        /// <summary>
        /// 是否更新PDA明细的订单编号
        /// </summary>
        public bool UpdatePDAOrderNo { get; set; } = true;

        /// <summary>
        /// 筛选的供应商编码列表（为空表示所有供应商）
        /// </summary>
        public List<string>? SupplierCodes { get; set; }
    }

    /// <summary>
    /// 导出选项DTO
    /// </summary>
    public class ExportOptionsDto
    {
        /// <summary>
        /// 是否包含图片
        /// </summary>
        public bool IncludeImages { get; set; } = false;

        /// <summary>
        /// 导出格式 (Excel, PDF)
        /// </summary>
        public string Format { get; set; } = "Excel";

        /// <summary>
        /// 是否包含明细信息
        /// </summary>
        public bool IncludeDetails { get; set; } = true;

        /// <summary>
        /// 图片下载超时时间（秒）
        /// </summary>
        public int ImageDownloadTimeout { get; set; } = 30;
    }
}