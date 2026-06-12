using SqlSugar;
using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 货柜主表 - 用于管理货柜装载和运输信息
    /// 
    /// 功能说明：
    /// 1. 记录货柜的基本信息和装载情况
    /// 2. 跟踪货柜的运输状态和时间节点
    /// 3. 统计货柜的总体积、总金额等汇总信息
    /// 4. 通过导航属性关联货柜明细信息
    /// 
    /// 数据库表名：Container
    /// 创建时间：2024年
    /// 维护团队：HB Platform 开发团队
    /// </summary>
    [SugarTable("Container")]
    public class Container : BaseEntity
    {
        /// <summary>
        /// 货柜编码（主键）
        /// 说明：使用UUID7格式生成的唯一标识符，确保全局唯一性
        /// 格式：类似 01234567-89ab-cdef-0123-456789abcdef
        /// 用途：作为货柜的唯一标识，用于关联明细表和业务操作
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string ContainerCode { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 货柜编号
        /// 说明：货柜的业务编号，用于业务人员识别
        /// 格式：如 HG2024001、CONT-001等
        /// 用途：业务展示、报表统计、对外沟通
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "货柜编号")]
        public string? ContainerNumber { get; set; }

        /// <summary>
        /// 装柜日期
        /// 说明：货物装入货柜的日期
        /// 用途：运输计划、到货预估、业务跟踪
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "装柜日期")]
        public DateTime? LoadingDate { get; set; }

        /// <summary>
        /// 预计到岸日期
        /// 说明：货柜预计到达目的港的日期
        /// 用途：库存计划、销售预期、客户沟通
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "预计到岸日期")]
        public DateTime? EstimatedArrivalDate { get; set; }

        /// <summary>
        /// 实际到货日期
        /// 说明：货柜实际到达并完成清关的日期
        /// 用途：库存更新、成本核算、绩效分析
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "实际到货日期")]
        public DateTime? ActualArrivalDate { get; set; }

        /// <summary>
        /// 合计件数
        /// 说明：货柜中所有商品的总件数
        /// 精度：支持小数，适应不同计量单位
        /// 用途：装载统计、运输计费、库存管理
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "合计件数")]
        public decimal? TotalPieces { get; set; }

        /// <summary>
        /// 合计数量
        /// 说明：货柜中所有商品的总数量（按最小单位计算）
        /// 精度：支持小数，适应不同商品规格
        /// 用途：销售统计、库存管理、成本核算
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "合计数量")]
        public decimal? TotalQuantity { get; set; }

        /// <summary>
        /// 合计金额
        /// 说明：货柜中所有商品的总金额（人民币）
        /// 精度：2位小数，标准货币精度
        /// 用途：成本控制、利润分析、财务报表
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "合计金额")]
        public decimal? TotalAmount { get; set; }

        /// <summary>
        /// 总体积
        /// 说明：货柜中所有商品的总体积（立方米）
        /// 精度：3位小数，精确计算装载率
        /// 用途：装载优化、运费计算、仓储规划
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 3)]
        [Display(Name = "总体积")]
        public decimal? TotalVolume { get; set; }

        /// <summary>
        /// 成本浮率
        /// 说明：成本调整系数，用于价格浮动计算
        /// 精度：4位小数，支持精确的百分比计算
        /// 用途：价格调整、成本控制、利润分析
        /// 示例：1.05表示上浮5%，0.95表示下调5%
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 4)]
        [Display(Name = "成本浮率")]
        public decimal? CostFloatRate { get; set; }

        /// <summary>
        /// 汇率
        /// 说明：结算时使用的汇率（人民币对外币）
        /// 精度：4位小数，标准汇率精度
        /// 用途：成本核算、价格换算、财务结算
        /// 示例：7.2500表示1美元=7.25人民币
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 4)]
        [Display(Name = "汇率")]
        public decimal? ExchangeRate { get; set; }

        /// <summary>
        /// 运费
        /// 说明：货柜运输费用（人民币）
        /// 精度：2位小数，标准货币精度
        /// 用途：成本核算、利润计算、费用分摊
        /// </summary>
        [SugarColumn(IsNullable = true, DecimalDigits = 2)]
        [Display(Name = "运费")]
        public decimal? ShippingFee { get; set; }

        /// <summary>
        /// 货柜状态
        /// 说明：货柜当前的业务状态
        /// 枚举值：
        /// 0 - 已装柜（Loaded）
        /// 1 - 运输中（Shipping）
        /// 2 - 已完成（Completed）
     
        /// 7 - 已取消（Cancelled）
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "状态")]
        public int? Status { get; set; }

        /// <summary>
        /// 备注
        /// 说明：货柜相关的备注信息
        /// 长度：最大1000个字符
        /// 用途：记录特殊要求、注意事项、问题说明等
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        [Display(Name = "备注")]
        public string? Remarks { get; set; }

        /// <summary>
        /// 备注2
        /// 说明：额外的备注信息字段
        /// 长度：最大1000个字符
        /// 用途：补充说明、内部记录、特殊标记等
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 1000)]
        [Display(Name = "备注2")]
        public string? Remarks2 { get; set; }

        #region 导航属性

        /// <summary>
        /// 货柜明细列表
        /// 说明：一对多关系，一个货柜包含多个商品明细
        /// 用途：获取货柜中的所有商品信息
        /// </summary>
        [Navigate(NavigateType.OneToMany, nameof(ContainerDetail.ContainerCode), nameof(ContainerCode))]
        [SugarColumn(IsIgnore = true)]
        public List<ContainerDetail> Details { get; set; } = new List<ContainerDetail>();

        #endregion

        #region 计算属性

        /// <summary>
        /// 状态显示名称
        /// 说明：将数字状态转换为可读的中文名称
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public string StatusDisplayName => Status switch
        {
            0 => "草稿",
            1 => "已确认",
            2 => "已装柜",
            3 => "运输中",
            4 => "已到港",
            5 => "已清关",
            6 => "已完成",
            7 => "已取消",
            _ => "未知状态"
        };

        /// <summary>
        /// 是否已完成
        /// 说明：判断货柜是否已完成所有流程
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool IsCompleted => Status == 6;

        /// <summary>
        /// 是否可编辑
        /// 说明：判断货柜是否还可以编辑
        /// 规则：草稿和已确认状态可以编辑
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public bool IsEditable => Status is 0 or 1;

        /// <summary>
        /// 运输天数
        /// 说明：从装柜到实际到货的天数
        /// 用途：绩效分析、运输效率评估
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public int? ShippingDays
        {
            get
            {
                if (LoadingDate.HasValue && ActualArrivalDate.HasValue)
                {
                    return (ActualArrivalDate.Value - LoadingDate.Value).Days;
                }
                return null;
            }
        }

        /// <summary>
        /// 装载率
        /// 说明：实际装载体积与标准货柜容量的比率
        /// 用途：装载效率分析、成本优化
        /// 备注：假设标准货柜容量为68立方米
        /// </summary>
        [SugarColumn(IsIgnore = true)]
        public decimal? LoadingRate
        {
            get
            {
                const decimal StandardContainerVolume = 68m; // 标准40尺货柜容量
                if (TotalVolume.HasValue && TotalVolume.Value > 0)
                {
                    return Math.Round(TotalVolume.Value / StandardContainerVolume * 100, 2);
                }
                return null;
            }
        }

        #endregion
    }
}
