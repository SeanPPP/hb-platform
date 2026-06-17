using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 货柜明细表 - 用于记录货柜中每个商品的装载详情
    ///
    /// 功能说明：
    /// 1. 记录货柜中每个商品的装载数量和价格信息
    /// 2. 支持套装商品和混装商品的管理
    /// 3. 计算单个商品在货柜中的总金额和总体积
    /// 4. 通过导航属性关联主表和商品信息
    ///
    /// 数据库表名：ContainerDetail
    /// 创建时间：2024年
    /// 维护团队：HB Platform 开发团队
    /// </summary>
    [SugarTable("ContainerDetail")]
    public class ContainerDetail : BaseEntity
    {
        /// <summary>
        /// 明细编码（主键）
        /// 说明：使用UUID7格式生成的唯一标识符，确保全局唯一性
        /// 格式：类似 01234567-89ab-cdef-0123-456789abcdef
        /// 用途：作为明细记录的唯一标识，用于数据关联和业务操作
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string DetailCode { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 货柜编码（外键）
        /// 说明：关联到Container表的ContainerCode字段
        /// 用途：建立主从关系，标识明细所属的货柜
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string ContainerCode { get; set; } = string.Empty;

        /// <summary>
        /// 商品编码（外键）
        /// 说明：关联到DomesticProduct表的ProductCode字段
        /// 用途：标识装载的具体商品，获取商品基础信息
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? ProductCode { get; set; }

        /// <summary>
        /// 目标仓库分类GUID
        /// 说明：货柜明细可先保存目标分类，后续创建新商品时写入本地商品仓库分类。
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? TargetWarehouseCategoryGUID { get; set; }

        /// <summary>
        /// 装柜类型
        /// 说明：商品在货柜中的装载方式
        /// 枚举值：
        /// - "单品" - 独立装载的商品
        /// - "套装" - 作为套装的一部分
        /// - "混装" - 与其他商品混合装载
        /// - "散装" - 散装商品
        /// 用途：装载管理、成本分摊、库存处理
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 20)]
        [Display(Name = "装柜类型")]
        public string? LoadingType { get; set; }

        /// <summary>
        /// 混装组编码
        /// 说明：标识混装商品的分组，同一组的商品在同一包装中
        /// 格式：UUID或业务编码
        /// 用途：混装商品管理、成本分摊、拆包处理
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "混装组编码")]
        public string? MixedGroupCode { get; set; }

        /// <summary>
        /// 商品类型
        /// 说明：商品的分类标识
        /// 枚举值：
        /// - "普通商品" - 标准商品
        /// - "套装商品" - 套装主商品
        /// - "套装子商品" - 套装的组成部分
        /// - "多码商品" - 多规格商品
        /// 用途：商品管理、库存处理、销售策略
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 20)]
        [Display(Name = "商品类型")]
        public string? ProductType { get; set; }

        /// <summary>
        /// 套装数量
        /// 说明：如果是套装商品，表示套装的数量
        /// 精度：2位小数，支持分数套装
        /// 用途：套装商品管理、库存计算、销售统计
        /// 示例：2.5表示2.5套
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "套装数量")]
        public decimal? SetQuantity { get; set; }

        /// <summary>
        /// 装柜件数
        /// 说明：该商品在货柜中的包装件数
        /// 精度：2位小数，适应不同包装规格
        /// 用途：装载统计、运输管理、仓储处理
        /// 示例：10.5表示10.5件包装
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "装柜件数")]
        public decimal? LoadingPieces { get; set; }

        /// <summary>
        /// 装柜数量
        /// 说明：该商品在货柜中的实际数量（最小单位）
        /// 精度：2位小数，支持分数数量
        /// 用途：库存管理、销售统计、成本核算
        /// 示例：1000.50表示1000.5个单位
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "装柜数量")]
        public decimal? LoadingQuantity { get; set; }

        /// <summary>
        /// 国内价格
        /// 说明：商品的国内采购价格（人民币/单位）
        /// 精度：2位小数，标准货币精度
        /// 用途：成本核算、利润计算、价格分析
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "国内价格")]
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 调整浮率
        /// 说明：价格调整系数，用于特殊情况的价格调整
        /// 精度：4位小数，支持精确的百分比计算
        /// 用途：价格调整、促销管理、成本控制
        /// 示例：1.05表示上调5%，0.95表示下调5%
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 4)]
        [Display(Name = "调整浮率")]
        public decimal? AdjustmentRate { get; set; }

        /// <summary>
        /// 进口价格
        /// 说明：商品的进口销售价格（人民币/单位）
        /// 精度：2位小数，标准货币精度
        /// 用途：销售定价、利润分析、市场策略
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "进口价格")]
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 贴牌价格
        /// 说明：商品的贴牌销售价格（人民币/单位）
        /// 精度：2位小数，标准货币精度
        /// 用途：贴牌业务、价格管理、利润分析
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "贴牌价格")]
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 单件装箱数
        /// 说明：单个包装中包含的商品数量
        /// 精度：2位小数，支持分数包装
        /// 用途：包装管理、运输计算、库存处理
        /// 示例：24表示一箱24个，12.5表示一箱12.5个
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "单件装箱数")]
        public decimal? PackingQuantity { get; set; }

        /// <summary>
        /// 单件体积
        /// 说明：单个商品的体积（立方米）
        /// 精度：3位小数，精确计算装载体积
        /// 用途：装载计算、运费核算、仓储规划
        /// 示例：0.025表示0.025立方米
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 3)]
        [Display(Name = "单件体积")]
        public decimal? UnitVolume { get; set; }

        /// <summary>
        /// 合计装柜金额
        /// 说明：该商品在货柜中的总金额（数量×单价）
        /// 精度：2位小数，标准货币精度
        /// 用途：成本统计、利润分析、财务报表
        /// 计算公式：装柜数量 × 国内价格 × 调整浮率
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "合计装柜金额")]
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 合计装柜体积
        /// 说明：该商品在货柜中的总体积（立方米）
        /// 精度：3位小数，精确计算装载体积
        /// 用途：装载统计、运费分摊、空间利用分析
        /// 计算公式：装柜件数 × 单件体积
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 3)]
        [Display(Name = "合计装柜体积")]
        public decimal? TotalVolume { get; set; }

        /// <summary>
        /// 运输成本
        /// 说明：该商品分摊的运输费用（澳币）
        /// 精度：2位小数，标准货币精度
        /// 用途：成本核算、利润分析、定价策略
        /// 分摊方式：通常按体积比例分摊
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "运输成本")]
        public decimal? TransportCost { get; set; }

        /// <summary>
        /// 明细状态
        /// 说明：明细记录的状态
        /// 枚举值：
        /// 0 - 正常（Normal）
        /// 1 - 已确认（Confirmed）
        /// 2 - 已装柜（Loaded）
        /// 3 - 已到货（Arrived）
        /// 4 - 已入库（Stored）
        /// 5 - 异常（Exception）
        /// 6 - 已取消（Cancelled）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "状态")]
        public int? Status { get; set; }

        /// <summary>
        /// 是否上架
        /// 说明：控制明细记录是否上架销售
        /// true - 已上架（可销售）
        /// false - 已下架（不可销售）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "是否上架")]
        public bool? IsActive { get; set; }

        /// <summary>
        /// 备注
        /// 说明：明细相关的备注信息
        /// 长度：最大500个字符
        /// 用途：记录特殊情况、质量问题、处理说明等
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        [Display(Name = "备注")]
        public string? Remarks { get; set; }

        #region 导航属性

        /// <summary>
        /// 所属货柜
        /// 说明：多对一关系，多个明细属于一个货柜
        /// 用途：获取货柜主表信息
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ContainerCode), nameof(Container.ContainerCode))]
        [SugarColumn(IsIgnore = true)]
        public Container? Container { get; set; }

        /// <summary>
        /// 关联国内商品
        /// 说明：多对一关系，多个明细可以关联同一个商品
        /// 用途：获取商品基础信息
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(DomesticProduct.ProductCode))]
        [SugarColumn(IsIgnore = true)]
        public DomesticProduct? Product { get; set; }

        /// <summary>
        /// 关联本地商品
        /// 说明：多对一关系，多个明细可以关联同一个商品
        /// 用途：获取商品基础信息
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(Product.ProductCode))]
        [SugarColumn(IsIgnore = true)]
        public Product? LocalProduct { get; set; }

        #endregion

        #region 计算属性

        /// <summary>
        /// 状态显示名称
        /// 说明：将数字状态转换为可读的中文名称
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string StatusDisplayName =>
            Status switch
            {
                0 => "正常",
                1 => "已确认",
                2 => "已装柜",
                3 => "已到货",
                4 => "已入库",
                5 => "异常",
                6 => "已取消",
                _ => "未知状态",
            };

        /// <summary>
        /// 实际单价
        /// 说明：考虑调整浮率后的实际单价
        /// 计算公式：国内价格 × 调整浮率
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? ActualUnitPrice
        {
            get
            {
                if (DomesticPrice.HasValue)
                {
                    var rate = AdjustmentRate ?? 1m;
                    return Math.Round(DomesticPrice.Value * rate, 2);
                }
                return null;
            }
        }

        /// <summary>
        /// 计算总金额
        /// 说明：根据数量和实际单价计算总金额
        /// 计算公式：装柜数量 × 实际单价
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? CalculatedTotalAmount
        {
            get
            {
                if (LoadingQuantity.HasValue && ActualUnitPrice.HasValue)
                {
                    return Math.Round(LoadingQuantity.Value * ActualUnitPrice.Value, 2);
                }
                return null;
            }
        }

        /// <summary>
        /// 计算总体积
        /// 说明：根据装柜件数和单件体积计算总体积
        /// 计算公式：装柜件数 × 单件体积
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? CalculatedTotalVolume
        {
            get
            {
                if (LoadingPieces.HasValue && UnitVolume.HasValue)
                {
                    return Math.Round(LoadingPieces.Value * UnitVolume.Value, 3);
                }
                return null;
            }
        }

        /// <summary>
        /// 利润率
        /// 说明：基于进口价格的利润率
        /// 计算公式：(进口价格 - 实际单价) / 实际单价 × 100%
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? ProfitRate
        {
            get
            {
                if (ImportPrice.HasValue && ActualUnitPrice.HasValue && ActualUnitPrice.Value > 0)
                {
                    return Math.Round(
                        (ImportPrice.Value - ActualUnitPrice.Value) / ActualUnitPrice.Value * 100,
                        2
                    );
                }
                return null;
            }
        }

        /// <summary>
        /// 是否异常
        /// 说明：判断明细是否存在异常情况
        /// 异常条件：状态为异常、数量为0或负数、价格为0或负数
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool HasException
        {
            get
            {
                return Status == 5
                    || LoadingQuantity <= 0
                    || DomesticPrice <= 0
                    || string.IsNullOrWhiteSpace(ProductCode);
            }
        }

        /// <summary>
        /// 包装箱数
        /// 说明：根据装柜数量和单件装箱数计算需要的包装箱数
        /// 计算公式：装柜数量 ÷ 单件装箱数（向上取整）
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? PackageBoxes
        {
            get
            {
                if (
                    LoadingQuantity.HasValue
                    && PackingQuantity.HasValue
                    && PackingQuantity.Value > 0
                )
                {
                    return Math.Ceiling(LoadingQuantity.Value / PackingQuantity.Value);
                }
                return null;
            }
        }

        #endregion

        #region 业务方法

        /// <summary>
        /// 更新计算字段
        /// 说明：根据基础数据重新计算派生字段
        /// 用途：数据一致性维护、批量更新
        /// </summary>
        public void UpdateCalculatedFields()
        {
            TotalAmount = CalculatedTotalAmount;
            TotalVolume = CalculatedTotalVolume;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 验证数据完整性
        /// 说明：检查明细数据是否完整和合理
        /// 返回：验证结果和错误信息
        /// </summary>
        public (bool IsValid, List<string> Errors) ValidateData()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(ContainerCode))
                errors.Add("货柜编码不能为空");

            if (string.IsNullOrWhiteSpace(ProductCode))
                errors.Add("商品编码不能为空");

            if (LoadingQuantity <= 0)
                errors.Add("装柜数量必须大于0");

            if (DomesticPrice <= 0)
                errors.Add("国内价格必须大于0");

            if (UnitVolume <= 0)
                errors.Add("单件体积必须大于0");

            if (PackingQuantity <= 0)
                errors.Add("单件装箱数必须大于0");

            return (errors.Count == 0, errors);
        }

        #endregion
    }
}
