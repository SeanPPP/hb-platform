using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 过滤条件操作符枚举
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FilterOperator
    {
        // 字符串操作符
        /// <summary>等于</summary>
        Equals,
        /// <summary>不等于</summary>
        NotEquals,
        /// <summary>包含</summary>
        Contains,
        /// <summary>不包含</summary>
        NotContains,
        /// <summary>开头是</summary>
        StartsWith,
        /// <summary>结尾是</summary>
        EndsWith,
        /// <summary>为空</summary>
        IsNull,
        /// <summary>不为空</summary>
        IsNotNull,

        // 数字操作符
        /// <summary>大于</summary>
        GreaterThan,
        /// <summary>小于</summary>
        LessThan,
        /// <summary>大于等于</summary>
        GreaterThanOrEqual,
        /// <summary>小于等于</summary>
        LessThanOrEqual,

        // 时间操作符
        /// <summary>在范围内</summary>
        Between,
        /// <summary>不在范围内</summary>
        NotBetween,

        // 列表操作符
        /// <summary>在列表中</summary>
        In,
        /// <summary>不在列表中</summary>
        NotIn
    }

    /// <summary>
    /// 逻辑操作符枚举
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum LogicalOperator
    {
        /// <summary>且</summary>
        And,
        /// <summary>或</summary>
        Or
    }

    /// <summary>
    /// 字段类型枚举
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FieldType
    {
        /// <summary>字符串</summary>
        String,
        /// <summary>数字</summary>
        Number,
        /// <summary>小数</summary>
        Decimal,
        /// <summary>整数</summary>
        Integer,
        /// <summary>日期时间</summary>
        DateTime,
        /// <summary>日期</summary>
        Date,
        /// <summary>布尔值</summary>
        Boolean,
        /// <summary>枚举</summary>
        Enum
    }

    /// <summary>
    /// 过滤条件
    /// </summary>
    public class FilterCondition
    {
        /// <summary>
        /// 字段名称
        /// </summary>
        [Required(ErrorMessage = "字段名称不能为空")]
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// 字段类型
        /// </summary>
        public FieldType FieldType { get; set; } = FieldType.String;

        /// <summary>
        /// 操作符
        /// </summary>
        public FilterOperator Operator { get; set; } = FilterOperator.Equals;

        /// <summary>
        /// 过滤值
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// 第二个值（用于范围查询）
        /// </summary>
        public object? SecondValue { get; set; }

        /// <summary>
        /// 是否忽略大小写（仅适用于字符串）
        /// </summary>
        public bool IgnoreCase { get; set; } = true;

        /// <summary>
        /// 该条件的逻辑操作符（与前一个条件的连接方式）
        /// 注意：第一个条件的逻辑操作符通常被忽略
        /// </summary>
        public LogicalOperator LogicalOperator { get; set; } = LogicalOperator.And;
    }

    /// <summary>
    /// 复合过滤条件
    /// </summary>
    public class FilterGroup
    {
        /// <summary>
        /// 条件列表（同一字段内的条件）
        /// </summary>
        public List<FilterCondition> Conditions { get; set; } = new();

        /// <summary>
        /// 子分组列表（不同字段的条件组）
        /// </summary>
        public List<FilterGroup> SubGroups { get; set; } = new();

        /// <summary>
        /// 逻辑操作符（条件之间的关系）
        /// 对于同一字段内的条件：按用户选择的逻辑关系（AND/OR）
        /// 对于不同字段间的条件组：固定使用AND
        /// </summary>
        public LogicalOperator LogicalOperator { get; set; } = LogicalOperator.And;

        /// <summary>
        /// 字段名（用于标识这个组属于哪个字段，便于调试）
        /// </summary>
        public string? FieldName { get; set; }
    }

    /// <summary>
    /// 高级查询DTO基类
    /// </summary>
    public class AdvancedQuery : PagedQuery
    {
        /// <summary>
        /// 过滤条件组
        /// </summary>
        public FilterGroup? FilterGroup { get; set; }

        /// <summary>
        /// 排序字段
        /// </summary>
        public string? SortBy { get; set; }

        /// <summary>
        /// 排序方向 (asc/desc)
        /// </summary>
        public string? SortDirection { get; set; } = "asc";

        /// <summary>
        /// 搜索关键词（全局搜索）
        /// </summary>
        public string? Search { get; set; }
    }


    /// <summary>
    /// 字段信息DTO（用于前端生成过滤界面）
    /// </summary>
    public class FieldInfo
    {
        /// <summary>
        /// 字段名称
        /// </summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 字段类型
        /// </summary>
        public FieldType FieldType { get; set; }

        /// <summary>
        /// 支持的操作符
        /// </summary>
        public List<FilterOperator> SupportedOperators { get; set; } = new();

        /// <summary>
        /// 是否可搜索
        /// </summary>
        public bool IsSearchable { get; set; } = true;

        /// <summary>
        /// 是否可排序
        /// </summary>
        public bool IsSortable { get; set; } = true;

        /// <summary>
        /// 枚举选项（仅适用于枚举类型）
        /// </summary>
        public Dictionary<string, string>? EnumOptions { get; set; }

        /// <summary>
        /// 示例值
        /// </summary>
        public string? ExampleValue { get; set; }
    }

    /// <summary>
    /// 操作符信息DTO
    /// </summary>
    public class OperatorInfo
    {
        /// <summary>
        /// 操作符
        /// </summary>
        public FilterOperator Operator { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 是否需要值
        /// </summary>
        public bool RequiresValue { get; set; } = true;

        /// <summary>
        /// 是否需要第二个值（范围查询）
        /// </summary>
        public bool RequiresSecondValue { get; set; } = false;
    }
}
