using System.ComponentModel.DataAnnotations;
using BlazorApp.Shared.Helper;
using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 国内商品信息表 - 用于存储义乌采购的国内供应商商品信息
    ///
    /// 功能说明：
    /// 1. 管理国内供应商提供的商品基础信息
    /// 2. 支持普通商品、套装商品、多码商品三种类型
    /// 3. 包含价格、规格、包装等完整商品属性
    /// 4. 通过导航属性关联供应商和套装商品详情
    ///
    /// 数据库表名：DomesticProduct
    /// 创建时间：2024年
    /// 维护团队：HB Platform 开发团队
    /// </summary>
    [SugarTable("DomesticProduct")]
    public class DomesticProduct : BaseEntity
    {
        /// <summary>
        /// 商品编码（主键）
        /// 说明：使用UUID7格式生成的唯一标识符，确保全局唯一性
        /// 格式：类似 01234567-89ab-cdef-0123-456789abcdef
        /// 用途：作为商品的唯一标识，用于关联其他表和业务操作
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string ProductCode { get; set; } = UuidHelper.GenerateUuid7();

        /// <summary>
        /// 供应商编码（外键）
        /// 说明：关联到ChinaSupplier表的SupplierCode字段
        /// 用途：标识商品来源的供应商，用于供应商管理和商品分类
        /// 示例：SUP001, SUP002等
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? SupplierCode { get; set; }

        /// <summary>
        /// 商品名称（中文）
        /// 说明：商品的中文显示名称，用于前端展示和搜索
        /// 长度：最大200个字符
        /// 用途：商品列表显示、搜索匹配、订单展示等
        /// 示例：小米手机13、苹果iPhone15等
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        [Display(Name = "商品名称")]
        public string? ProductName { get; set; }

        /// <summary>
        /// 商品英文名称
        /// 说明：商品的英文显示名称，用于国际化和出口业务
        /// 长度：最大500个字符
        /// 用途：外贸订单、英文报表、国际化显示等
        /// 示例：Xiaomi Phone 13, Apple iPhone 15等
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        [Display(Name = "商品英文名称")]
        public string? EnglishProductName { get; set; }

        /// <summary>
        /// HB货号（内部货号）
        /// 说明：HB平台内部使用的商品货号，格式为前缀+序号
        /// 格式：HB123-001, YW001-002等
        /// 长度：最大50个字符
        /// 生成规则：前缀代码 + 序号，支持自动生成或手动指定
        /// 用途：内部管理、仓库管理、订单处理等
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "HB货号")]
        public string? HBProductNo { get; set; }

        /// <summary>
        /// 商品条形码
        /// 说明：商品的标准条形码，用于扫码识别和库存管理
        /// 长度：最大50个字符
        /// 格式：支持EAN-13、UPC-A等标准格式
        /// 用途：扫码入库、销售扫码、库存盘点等
        /// 示例：6901234567890
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        [Display(Name = "条形码")]
        public string? Barcode { get; set; }

        /// <summary>
        /// 商品规格描述
        /// 说明：详细描述商品的规格参数和特性
        /// 长度：最大100个字符
        /// 用途：商品详情展示、规格对比、采购参考等
        /// 示例：6.1英寸屏幕/128GB存储/蓝色
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        [Display(Name = "商品规格")]
        public string? ProductSpecification { get; set; }

        /// <summary>
        /// 商品类型枚举
        /// 说明：定义商品的分类类型，影响业务处理逻辑
        ///
        /// 类型定义：
        /// - 0：普通商品（单一SKU商品）
        /// - 1：套装商品（由多个商品组成的套装）
        /// - 2：多码商品（一个商品对应多个条码或规格）
        ///
        /// 业务影响：
        /// - 普通商品：标准的单品处理流程
        /// - 套装商品：需要关联套装明细表，计算套装数量
        /// - 多码商品：支持多种规格和条码，库存分别管理
        ///
        /// 默认值：0（普通商品）
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 20)]
        [Display(Name = "商品类型")]
        public int ProductType { get; set; } = 0;

        /// <summary>
        /// 国内价格（人民币）
        /// 说明：商品在国内市场的销售价格，以人民币计价
        /// 单位：元（CNY）
        /// 用途：国内订单报价、成本核算、利润计算等
        /// 精度：支持小数点后2位
        /// 示例：99.99, 1299.00等
        /// 注意：可为空，表示价格待定或未设置
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "国内价格")]
        public decimal? DomesticPrice { get; set; }

        /// <summary>
        /// 零售价（美元）
        /// 说明：商品零售价格，通常以美元计价
        /// 单位：美元（USD）
        /// 用途：零售报价、外贸订单、利润计算等
        /// 精度：支持小数点后2位
        /// 示例：15.99, 199.00等
        /// 注意：可为空，表示价格待定或未设置
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "零售价")]
        public decimal? OEMPrice { get; set; }

        /// <summary>
        /// 进口价格（美元）
        /// 说明：商品的进口采购价格，通常以美元计价
        /// 单位：美元（USD）
        /// 用途：进口成本核算、外汇预算、供应链管理等
        /// 精度：支持小数点后2位
        /// 示例：12.50, 89.99等
        /// 注意：可为空，表示非进口商品或价格待定
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "进口价格")]
        public decimal? ImportPrice { get; set; }

        /// <summary>
        /// 单件装箱数量
        /// 说明：单个包装箱可容纳的商品数量
        /// 单位：件/箱
        /// 用途：物流计算、仓储管理、运费估算等
        /// 示例：12件/箱, 50件/箱等
        /// 注意：影响运输成本和仓储空间计算
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "单件装箱数")]
        public int? PackingQuantity { get; set; }

        /// <summary>
        /// 单件体积
        /// 说明：单个商品占用的空间体积
        /// 单位：立方米（m³）
        /// 用途：仓储空间计算、物流成本估算、货柜装载等
        /// 精度：支持小数点后3位
        /// 示例：0.001, 0.025, 0.150等
        /// 计算方式：长 × 宽 × 高（米）
        /// 注意：用于物流费用和仓储成本的精确计算
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "单件体积")]
        public decimal? UnitVolume { get; set; }

        /// <summary>
        /// 中包数量
        /// 说明：中等包装单位的数量，介于单件和整箱之间
        /// 单位：件/中包
        /// 用途：批发销售、中等批量订单、库存管理等
        /// 示例：6件/中包, 24件/中包等
        /// 业务场景：适用于中等批量的B2B销售
        /// 注意：通常小于装箱数，大于1
        /// </summary>
        [SugarColumn(IsNullable = true)]
        [Display(Name = "中包数量")]
        public int? MiddlePackQuantity { get; set; }

        /// <summary>
        /// 商品图片URL地址
        /// 说明：商品主图的网络访问地址
        /// 长度：最大500个字符
        /// 格式：支持http/https协议的图片URL
        /// 用途：商品展示、订单确认、营销推广等
        /// 示例：https://cdn.example.com/products/12345.jpg
        /// 支持格式：JPG、PNG、WebP等常见图片格式
        /// 注意：建议使用CDN加速提升加载速度
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        [Display(Name = "商品图片")]
        public string? ProductImage { get; set; }

        /// <summary>
        /// 商品状态标识
        /// 说明：控制商品是否在系统中可用和可见
        ///
        /// 状态定义：
        /// - true：启用状态，商品正常可用
        /// - false：禁用状态，商品暂停使用
        ///
        /// 业务影响：
        /// - 启用：可在前端显示、可下单、可搜索
        /// - 禁用：前端隐藏、禁止下单、搜索排除
        ///
        /// 默认值：true（启用状态）
        /// 用途：商品上下架管理、库存控制、业务流程控制
        /// </summary>
        [SugarColumn(IsNullable = false)]
        [Display(Name = "使用状态")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 导航属性 - 关联供应商信息（一对一关系）
        /// 说明：通过SupplierCode外键关联到ChinaSupplier表
        /// 关联方式：DomesticProduct.SupplierCode = ChinaSupplier.SupplierCode
        /// 用途：获取商品对应的供应商详细信息
        ///
        /// 包含信息：
        /// - 供应商名称、联系方式
        /// - 供应商地址、资质信息
        /// - 合作状态、评级等级
        ///
        /// 使用场景：
        /// - 商品列表显示供应商名称
        /// - 供应商管理和商品关联查询
        /// - 采购订单生成和供应商联系
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(SupplierCode), nameof(ChinaSupplier.SupplierCode))]
        public ChinaSupplier? Supplier { get; set; }

        /// <summary>
        /// 导航属性 - 套装商品明细信息（一对多关系）
        /// 说明：当ProductType=1（套装商品）时，关联到DomesticSetProduct表
        /// 关联方式：DomesticProduct.ProductCode = DomesticSetProduct.ProductCode
        /// 用途：获取套装商品包含的具体商品清单
        ///
        /// 套装逻辑：
        /// - 仅套装商品类型才有关联数据
        /// - 一个套装可包含多个不同的商品
        /// - 每个套装商品有独立的价格和规格
        ///
        /// 业务应用：
        /// - 计算套装总价和成本
        /// - 库存管理和发货清单
        /// - 套装商品的详情展示
        /// - SetQuantity属性通过此集合计算得出
        /// </summary>
        [Navigate(
            NavigateType.OneToMany,
            nameof(ProductCode),
            nameof(DomesticSetProduct.ProductCode)
        )]
        public List<DomesticSetProduct>? DomesticSetProducts { get; set; }
    }
}
