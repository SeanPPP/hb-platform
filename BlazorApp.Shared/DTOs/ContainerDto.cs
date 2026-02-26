using System;
using System.Collections.Generic;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 货柜主表DTO
    /// </summary>
    public class ContainerMainDto
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 全局唯一标识
        /// </summary>
        public string? HGUID { get; set; }

        /// <summary>
        /// 货柜编号
        /// </summary>
        public string? 货柜编号 { get; set; }

        /// <summary>
        /// 装柜日期
        /// </summary>
        public DateTime? 装柜日期 { get; set; }

        /// <summary>
        /// 预计到岸日期
        /// </summary>
        public DateTime? 预计到岸日期 { get; set; }

        /// <summary>
        /// 实际到货日期
        /// </summary>
        public DateTime? 实际到货日期 { get; set; }

        /// <summary>
        /// 合计件数
        /// </summary>
        public decimal? 合计件数 { get; set; }

        /// <summary>
        /// 合计数量
        /// </summary>
        public decimal? 合计数量 { get; set; }

        /// <summary>
        /// 合计金额
        /// </summary>
        public decimal? 合计金额 { get; set; }

        /// <summary>
        /// 总体积
        /// </summary>
        public decimal? 总体积 { get; set; }

        /// <summary>
        /// 成本浮率
        /// </summary>
        public decimal? 成本浮率 { get; set; }

        /// <summary>
        /// 汇率
        /// </summary>
        public decimal? 汇率 { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        public decimal? 运费 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }

        /// <summary>
        /// 状态
        /// </summary>
        public int? 状态 { get; set; }

        /// <summary>
        /// 货柜详情列表
        /// </summary>
        public List<ContainerDetailDto> Details { get; set; } = new List<ContainerDetailDto>();
    }

    /// <summary>
    /// 货柜详情DTO
    /// </summary>
    public class ContainerDetailDto
    {
        /// <summary>
        /// 主键ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 全局唯一标识
        /// </summary>
        public string? HGUID { get; set; }

        /// <summary>
        /// 主表GUID
        /// </summary>
        public string? 主表GUID { get; set; }

        /// <summary>
        /// 商品编码
        /// </summary>
        public string? 商品编码 { get; set; }

        /// <summary>
        /// 装柜类型
        /// </summary>
        public string? 装柜类型 { get; set; }

        /// <summary>
        /// 商品类型
        /// </summary>
        public string? 商品类型 { get; set; }

        /// <summary>
        /// 套装数量
        /// </summary>
        public decimal? 套装数量 { get; set; }

        /// <summary>
        /// 装柜件数
        /// </summary>
        public decimal? 装柜件数 { get; set; }

        /// <summary>
        /// 装柜数量
        /// </summary>
        public decimal? 装柜数量 { get; set; }

        /// <summary>
        /// 国内价格
        /// </summary>
        public decimal? 国内价格 { get; set; }

        /// <summary>
        /// 调整浮率
        /// </summary>
        public decimal? 调整浮率 { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? 进口价格 { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        public decimal? 贴牌价格 { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public decimal? 单件装箱数 { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? 单件体积 { get; set; }

        /// <summary>
        /// 合计装柜金额
        /// </summary>
        public decimal? 合计装柜金额 { get; set; }

        /// <summary>
        /// 合计装柜体积
        /// </summary>
        public decimal? 合计装柜体积 { get; set; }

        /// <summary>
        /// 运输成本
        /// </summary>
        public decimal? 运输成本 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }

        /// <summary>
        /// 商品信息
        /// </summary>
        public ContainerProductInfoDto? 商品信息 { get; set; }

        public bool 是否新商品 { get; set; }
    }

    /// <summary>
    /// 货柜商品信息DTO
    /// </summary>
    public class ContainerProductInfoDto
    {
        /// <summary>
        /// 商品编码
        /// </summary>
        public string? 商品编码 { get; set; }

        /// <summary>
        /// 货号
        /// </summary>
        public string? 货号 { get; set; }

        /// <summary>
        /// 商品名称
        /// </summary>
        public string? 商品名称 { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? 英文名称 { get; set; }

        /// <summary>
        /// 商品图片
        /// </summary>
        public string? 商品图片 { get; set; }

        public string? 条形码 { get; set; }

        /// <summary>
        /// 零售价格
        /// </summary>
        public decimal? 零售价格 { get; set; }

        /// <summary>
        /// 商品规格
        /// </summary>
        public string? 商品规格 { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string? 单位 { get; set; }

        /// <summary>
        /// 单件装箱数
        /// </summary>
        public decimal? 单件装箱数 { get; set; }

        /// <summary>
        /// 单件体积
        /// </summary>
        public decimal? 单件体积 { get; set; }

        /// <summary>
        /// 商品类型
        /// </summary>
        public string? 商品类型 { get; set; }

        /// <summary>
        /// 套装数量
        /// </summary>
        public decimal? 套装数量 { get; set; }
    }

    /// <summary>
    /// 更新货柜明细DTO
    /// </summary>
    public class UpdateContainerDetailDto
    {
        /// <summary>
        /// 明细GUID
        /// </summary>
        public string HGUID { get; set; } = string.Empty;

        /// <summary>
        /// 调整浮率
        /// </summary>
        public decimal? 调整浮率 { get; set; }

        /// <summary>
        /// 进口价格
        /// </summary>
        public decimal? 进口价格 { get; set; }

        /// <summary>
        /// 运输成本
        /// </summary>
        public decimal? 运输成本 { get; set; }

        /// <summary>
        /// 商品名称（商品信息）
        /// </summary>
        public string? 商品名称 { get; set; }

        /// <summary>
        /// 英文名称（商品信息）
        /// </summary>
        public string? 英文名称 { get; set; }

        /// <summary>
        /// 贴牌价格
        /// </summary>
        public decimal? 贴牌价格 { get; set; }
    }

    /// <summary>
    /// 创建货柜DTO
    /// </summary>
    public class CreateContainerDto
    {
        /// <summary>
        /// 货柜编号
        /// </summary>
        public string 货柜编号 { get; set; } = string.Empty;

        /// <summary>
        /// 装柜日期
        /// </summary>
        public DateTime? 装柜日期 { get; set; }

        /// <summary>
        /// 预计到岸日期
        /// </summary>
        public DateTime? 预计到岸日期 { get; set; }

        /// <summary>
        /// 汇率
        /// </summary>
        public decimal? 汇率 { get; set; }

        /// <summary>
        /// 运费
        /// </summary>
        public decimal? 运费 { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? 备注 { get; set; }
    }

    /// <summary>
    /// 货柜查询请求DTO
    /// </summary>
    public class ContainerQueryRequest
    {
        /// <summary>
        /// 日期过滤类型 - 预计到岸日期 或 实际到货日期
        /// </summary>
        public string DateType { get; set; } = "预计到岸日期";

        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// 页大小
        /// </summary>
        public int PageSize { get; set; } = 20;

        /// <summary>
        /// 货号过滤
        /// </summary>
        public string? ItemNumberFilter { get; set; }

        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortBy { get; set; } = "货号";

        /// <summary>
        /// 排序方向
        /// </summary>
        public string? SortDirection { get; set; } = "asc";
    }

    /// <summary>
    /// 货柜列表响应DTO
    /// </summary>
    public class ContainerListResponse
    {
        /// <summary>
        /// 货柜列表
        /// </summary>
        public List<ContainerMainDto> Containers { get; set; } = new List<ContainerMainDto>();

        /// <summary>
        /// 总数量
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// 页大小
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }
}
