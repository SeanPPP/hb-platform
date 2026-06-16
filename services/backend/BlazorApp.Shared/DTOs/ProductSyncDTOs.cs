using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    #region 检测相关DTO

    /// <summary>
    /// 商品检测结果DTO
    /// 用于返回商品是否存在以及仓库商品信息
    /// </summary>
    public class ProductDetectionResultDto
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 条码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 是否已存在（true=已存在，false=新商品）
        /// </summary>
        public bool Exists { get; set; }

        /// <summary>
        /// 检测结果描述（"新商品" / "已存在"）
        /// </summary>
        public string DetectionResult { get; set; } = string.Empty;

        /// <summary>
        /// 仓库商品的贴牌价格（如果存在）
        /// </summary>
        public decimal? WarehouseOEMPrice { get; set; }

        /// <summary>
        /// 仓库商品的进口价格（如果存在）
        /// </summary>
        public decimal? WarehouseImportPrice { get; set; }

        /// <summary>
        /// 仓库商品的国内价格（如果存在）
        /// </summary>
        public decimal? WarehouseDomesticPrice { get; set; }

        /// <summary>
        /// 仓库商品的单件体积（如果存在）
        /// </summary>
        public decimal? WarehouseVolume { get; set; }

        /// <summary>
        /// 仓库商品的上架状态（如果存在）
        /// </summary>
        public bool? WarehouseIsActive { get; set; }

        /// <summary>
        /// 仓库商品的英文名称（如果存在）
        /// 用于检测时自动填充英文名称到货柜明细
        /// </summary>
        public string? WarehouseEnglishName { get; set; }
    }

    /// <summary>
    /// 批量商品检测请求DTO
    /// </summary>
    public class BatchProductDetectionRequest
    {
        /// <summary>
        /// 要检测的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ProductDetectionItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商品检测项
    /// </summary>
    public class ProductDetectionItem
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        [Required(ErrorMessage = "货号不能为空")]
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 条码（可选）
        /// </summary>
        public string? Barcode { get; set; }
    }

    #endregion

    #region 创建相关DTO

    /// <summary>
    /// 批量创建商品请求DTO
    /// </summary>
    public class BatchProductCreateRequest
    {
        /// <summary>
        /// 要创建的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ProductCreateItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商品创建项
    /// </summary>
    public class ProductCreateItem
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号
        /// </summary>
        [Required(ErrorMessage = "货号不能为空")]
        public string ItemNumber { get; set; } = string.Empty;

        /// <summary>
        /// 条码
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 中文名称
        /// </summary>
        public string? ChineseName { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? EnglishName { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格（必填）
        /// </summary>
        [Required(ErrorMessage = "贴牌价格不能为空")]
        [Range(0.01, double.MaxValue, ErrorMessage = "贴牌价格必须大于0")]
        public decimal OEMPrice { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? Volume { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 商品图片URL
        /// </summary>
        public string? ImageUrl { get; set; }
    }

    #endregion

    #region 更新相关DTO

    /// <summary>
    /// 批量更新商品请求DTO
    /// </summary>
    public class BatchProductUpdateRequest
    {
        /// <summary>
        /// 要更新的商品列表
        /// </summary>
        [Required(ErrorMessage = "商品列表不能为空")]
        public List<ProductUpdateItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 商品更新项
    /// </summary>
    public class ProductUpdateItem
    {
        /// <summary>
        /// 商品编码（必填）
        /// </summary>
        [Required(ErrorMessage = "商品编码不能为空")]
        public string ProductCode { get; set; } = string.Empty;

        /// <summary>
        /// 货号（可选，用于商品编码匹配不到时的备选匹配）
        /// </summary>
        public string? ItemNumber { get; set; }

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

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? Volume { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 上架状态
        /// </summary>
        public bool IsActive { get; set; } = true;
    }

    #endregion

    #region 响应DTO

    /// <summary>
    /// 批量商品操作响应DTO
    /// </summary>
    public class BatchProductOperationResponse
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 成功处理的数量
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 失败的数量
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 跳过的数量（用于二次检查时跳过已存在的商品）
        /// </summary>
        public int SkippedCount { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 跳过的商品列表
        /// </summary>
        public List<string> SkippedItems { get; set; } = new();

        /// <summary>
        /// 返回数据（用于检测结果）
        /// </summary>
        public object? Data { get; set; }
    }

    #endregion

    #region 仓库商品库存同步DTO

    /// <summary>
    /// 仓库商品库存同步结果DTO
    /// 用于从HQ商品库存表同步到本地仓库商品表的结果返回
    /// </summary>
    public class WarehouseProductSyncResultDto
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 结果消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// HQ商品总数
        /// </summary>
        public int TotalHqProducts { get; set; }

        /// <summary>
        /// 本地商品总数（同步前）
        /// </summary>
        public int TotalLocalProductsBefore { get; set; }

        /// <summary>
        /// 新增商品数
        /// </summary>
        public int AddedCount { get; set; }

        /// <summary>
        /// 更新商品数
        /// </summary>
        public int UpdatedCount { get; set; }

        /// <summary>
        /// 删除商品数
        /// </summary>
        public int DeletedCount { get; set; }

        /// <summary>
        /// 错误数量
        /// </summary>
        public int ErrorCount { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public long DurationMs { get; set; }
    }

    #endregion

    #region 从HQ同步商品DTO

    /// <summary>
    /// POS 商品推送到 HQ 请求。
    /// </summary>
    public class PushProductsToHqRequest
    {
        /// <summary>
        /// 需要推送的商品编码列表。
        /// </summary>
        public List<string> ProductCodes { get; set; } = new();

        /// <summary>
        /// 带价格与候选信息的推送明细。
        /// 兼容旧调用：当前端仍只传 ProductCodes 时，这里可以为空。
        /// </summary>
        public List<PushProductsToHqItem> Items { get; set; } = new();
    }

    /// <summary>
    /// POS 商品推送到 HQ 后台任务状态常量。
    /// </summary>
    public static class ProductPushToHqJobStatusConstants
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Succeeded = "Succeeded";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// POS 商品推送到 HQ 后台任务快照。
    /// </summary>
    public class PushProductsToHqJobDto
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = ProductPushToHqJobStatusConstants.Queued;
        public bool IsDuplicateRequest { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Message { get; set; }
        public PushProductsToHqResult? Result { get; set; }
    }

    /// <summary>
    /// POS 商品推送到 HQ 的单项明细。
    /// </summary>
    public class PushProductsToHqItem
    {
        /// <summary>
        /// 商品编码，优先用于命中本地商品。
        /// </summary>
        public string? ProductCode { get; set; }

        /// <summary>
        /// 本地供应商编码，当 ProductCode 缺失时参与候选匹配。
        /// </summary>
        public string? LocalSupplierCode { get; set; }

        /// <summary>
        /// 货号，当 ProductCode 缺失时参与候选匹配。
        /// </summary>
        public string? ItemNumber { get; set; }

        /// <summary>
        /// 货柜明细商品名称。
        /// </summary>
        public string? ProductName { get; set; }

        /// <summary>
        /// 货柜明细英文名称。
        /// </summary>
        public string? EnglishName { get; set; }

        /// <summary>
        /// 货柜明细条码。
        /// </summary>
        public string? Barcode { get; set; }

        /// <summary>
        /// 货柜明细商品图片地址。
        /// </summary>
        public string? ImageUrl { get; set; }

        /// <summary>
        /// 国内价格。
        /// </summary>
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 进口价格。
        /// </summary>
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格。
        /// </summary>
        public decimal? OemPrice { get; set; }

        /// <summary>
        /// 仓库启用状态。
        /// 仅兼容旧请求字段；货柜发送 HQ 服务会忽略该字段，不再据此更新 HQ/POS 商品启用状态。
        /// </summary>
        [Obsolete("仅兼容旧请求字段，货柜发送 HQ 服务会忽略 WarehouseIsActive。")]
        public bool? WarehouseIsActive { get; set; }

        /// <summary>
        /// 前端页面上的新商品状态提示。
        /// 后端不会信任该字段做最终判断，会实时查询本地 Product 后决定是否允许推送。
        /// </summary>
        public bool IsNewProduct { get; set; }
    }

    /// <summary>
    /// 从 HQ 按选中商品同步到本地请求。
    /// </summary>
    public class SyncSelectedProductsFromHqRequest
    {
        /// <summary>
        /// 商品管理页选中的本地商品编码列表。
        /// </summary>
        [Required(ErrorMessage = "商品编码列表不能为空")]
        public List<string> ProductCodes { get; set; } = new();
    }

    /// <summary>
    /// POS 商品推送到 HQ 结果。
    /// SuccessCount/FailedCount 按商品编码统计，明细表写入量通过 AffectedRowCount 和各明细字段统计。
    /// </summary>
    public class PushProductsToHqResult : HqProductSyncResult
    {
        /// <summary>
        /// 成功推送的商品数。
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// 推送失败或未找到的商品数。
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// 请求推送的商品总数。
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// HQ 商品库存表新增数量。
        /// </summary>
        public int WarehouseInventoriesCreated { get; set; }

        /// <summary>
        /// HQ 商品库存表更新数量。
        /// </summary>
        public int WarehouseInventoriesUpdated { get; set; }

        /// <summary>
        /// HQ 商品、分店价格、多码表实际新增或更新的记录总数。
        /// </summary>
        public int AffectedRowCount =>
            ProductsAdded
            + ProductsUpdated
            + StoreRetailPricesCreated
            + StoreRetailPricesUpdated
            + ProductSetCodesCreated
            + ProductSetCodesUpdated
            + StoreMultiCodesCreated
            + StoreMultiCodesUpdated
            + WarehouseInventoriesCreated
            + WarehouseInventoriesUpdated;
    }

    /// <summary>
    /// 从HQ同步商品的结果DTO
    /// </summary>
    public class HqProductSyncResult
    {
        /// <summary>
        /// HQ商品总数
        /// </summary>
        public int TotalHqProducts { get; set; }

        /// <summary>
        /// 本地商品总数
        /// </summary>
        public int TotalLocalProducts { get; set; }

        /// <summary>
        /// 新增商品数
        /// </summary>
        public int ProductsAdded { get; set; }

        /// <summary>
        /// 更新商品数
        /// </summary>
        public int ProductsUpdated { get; set; }

        /// <summary>
        /// 删除商品数
        /// </summary>
        public int ProductsDeleted { get; set; }

        /// <summary>
        /// 软删除商品数
        /// </summary>
        public int ProductsSoftDeleted
        {
            get => ProductsDeleted;
            set => ProductsDeleted = value;
        }

        /// <summary>
        /// 新增分店零售价数
        /// </summary>
        public int StoreRetailPricesCreated { get; set; }

        /// <summary>
        /// 更新分店零售价数
        /// </summary>
        public int StoreRetailPricesUpdated { get; set; }

        /// <summary>
        /// 删除分店零售价数
        /// </summary>
        public int StoreRetailPricesDeleted { get; set; }

        /// <summary>
        /// 新增套装编码数
        /// </summary>
        public int ProductSetCodesCreated { get; set; }

        /// <summary>
        /// 新增套装编码数
        /// </summary>
        public int ProductSetCodesAdded
        {
            get => ProductSetCodesCreated;
            set => ProductSetCodesCreated = value;
        }

        /// <summary>
        /// 更新套装编码数
        /// </summary>
        public int ProductSetCodesUpdated { get; set; }

        /// <summary>
        /// 删除套装编码数
        /// </summary>
        public int ProductSetCodesDeleted { get; set; }

        /// <summary>
        /// 软删除套装编码数
        /// </summary>
        public int ProductSetCodesSoftDeleted
        {
            get => ProductSetCodesDeleted;
            set => ProductSetCodesDeleted = value;
        }

        /// <summary>
        /// 新增分店多码商品数
        /// </summary>
        public int StoreMultiCodesCreated { get; set; }

        /// <summary>
        /// 更新分店多码商品数
        /// </summary>
        public int StoreMultiCodesUpdated { get; set; }

        /// <summary>
        /// 删除分店多码商品数
        /// </summary>
        public int StoreMultiCodesDeleted { get; set; }

        /// <summary>
        /// 错误信息列表
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// 全量同步运行ID
        /// </summary>
        public long? SyncRunId { get; set; }

        /// <summary>
        /// HQ来源行数
        /// </summary>
        public long SourceRowCount { get; set; }

        /// <summary>
        /// 影子表行数
        /// </summary>
        public long ShadowRowCount { get; set; }

        /// <summary>
        /// Product是否完成切换
        /// </summary>
        public bool ProductsSwapped { get; set; }

        /// <summary>
        /// 切换前备份表名
        /// </summary>
        public string? BackupTableName { get; set; }
    }

    #endregion
}
