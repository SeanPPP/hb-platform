using BlazorApp.Shared.DTOs;
using System.Linq.Expressions;
using System.Reflection;

namespace BlazorApp.Shared.Helper
{
    /// <summary>
    /// 查询辅助类
    /// </summary>
    public static class QueryHelper
    {
        /// <summary>
        /// 获取字段支持的操作符
        /// </summary>
        /// <param name="fieldType">字段类型</param>
        /// <returns>支持的操作符列表</returns>
        public static List<FilterOperator> GetSupportedOperators(FieldType fieldType)
        {
            return fieldType switch
            {
                FieldType.String => new List<FilterOperator>
                {
                    FilterOperator.Equals,
                    FilterOperator.NotEquals,
                    FilterOperator.Contains,
                    FilterOperator.NotContains,
                    FilterOperator.StartsWith,
                    FilterOperator.EndsWith,
                    FilterOperator.IsNull,
                    FilterOperator.IsNotNull
                },
                FieldType.Number or FieldType.Decimal or FieldType.Integer => new List<FilterOperator>
                {
                    FilterOperator.Equals,
                    FilterOperator.NotEquals,
                    FilterOperator.GreaterThan,
                    FilterOperator.LessThan,
                    FilterOperator.GreaterThanOrEqual,
                    FilterOperator.LessThanOrEqual,
                    FilterOperator.Between,
                    FilterOperator.NotBetween,
                    FilterOperator.IsNull,
                    FilterOperator.IsNotNull
                },
                FieldType.DateTime or FieldType.Date => new List<FilterOperator>
                {
                    FilterOperator.Equals,
                    FilterOperator.NotEquals,
                    FilterOperator.GreaterThan,
                    FilterOperator.LessThan,
                    FilterOperator.GreaterThanOrEqual,
                    FilterOperator.LessThanOrEqual,
                    FilterOperator.Between,
                    FilterOperator.NotBetween
                },
                FieldType.Boolean => new List<FilterOperator>
                {
                    FilterOperator.Equals,
                    FilterOperator.NotEquals
                },
                FieldType.Enum => new List<FilterOperator>
                {
                    FilterOperator.Equals,
                    FilterOperator.NotEquals,
                    FilterOperator.In,
                    FilterOperator.NotIn
                },
                _ => new List<FilterOperator> { FilterOperator.Equals, FilterOperator.NotEquals }
            };
        }

        /// <summary>
        /// 获取操作符信息
        /// </summary>
        /// <param name="op">操作符</param>
        /// <returns>操作符信息</returns>
        public static OperatorInfo GetOperatorInfo(FilterOperator op)
        {
            return op switch
            {
                FilterOperator.Equals => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "等于",
                    Description = "完全匹配指定值",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.NotEquals => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "不等于",
                    Description = "不匹配指定值",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.Contains => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "包含",
                    Description = "字段值包含指定文本",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.NotContains => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "不包含",
                    Description = "字段值不包含指定文本",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.StartsWith => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "开头是",
                    Description = "字段值以指定文本开头",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.EndsWith => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "结尾是",
                    Description = "字段值以指定文本结尾",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.IsNull => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "为空",
                    Description = "字段值为空或null",
                    RequiresValue = false,
                    RequiresSecondValue = false
                },
                FilterOperator.IsNotNull => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "不为空",
                    Description = "字段值不为空且不为null",
                    RequiresValue = false,
                    RequiresSecondValue = false
                },
                FilterOperator.GreaterThan => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "大于",
                    Description = "字段值大于指定值",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.LessThan => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "小于",
                    Description = "字段值小于指定值",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.GreaterThanOrEqual => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "大于等于",
                    Description = "字段值大于或等于指定值",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.LessThanOrEqual => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "小于等于",
                    Description = "字段值小于或等于指定值",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.Between => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "在范围内",
                    Description = "字段值在指定范围内",
                    RequiresValue = true,
                    RequiresSecondValue = true
                },
                FilterOperator.NotBetween => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "不在范围内",
                    Description = "字段值不在指定范围内",
                    RequiresValue = true,
                    RequiresSecondValue = true
                },
                FilterOperator.In => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "在列表中",
                    Description = "字段值在指定列表中",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                FilterOperator.NotIn => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = "不在列表中",
                    Description = "字段值不在指定列表中",
                    RequiresValue = true,
                    RequiresSecondValue = false
                },
                _ => new OperatorInfo
                {
                    Operator = op,
                    DisplayName = op.ToString(),
                    Description = "未知操作符",
                    RequiresValue = true,
                    RequiresSecondValue = false
                }
            };
        }

        /// <summary>
        /// 获取国内商品的字段信息
        /// </summary>
        /// <returns>字段信息列表</returns>
        public static List<BlazorApp.Shared.DTOs.FieldInfo> GetDomesticProductFields()
        {
            return new List<BlazorApp.Shared.DTOs.FieldInfo>
            {
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "Supplier.SupplierName",
                    DisplayName = "供应商名称",
                    FieldType = FieldType.String,
                    SupportedOperators = GetSupportedOperators(FieldType.String),
                    ExampleValue = "义乌供应商"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "SupplierCode",
                    DisplayName = "供应商编码",
                    FieldType = FieldType.String,
                    SupportedOperators = GetSupportedOperators(FieldType.String),
                    ExampleValue = "SUP001"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "ProductName",
                    DisplayName = "商品名称",
                    FieldType = FieldType.String,
                    SupportedOperators = GetSupportedOperators(FieldType.String),
                    ExampleValue = "精美手工艺品"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "HBProductNo",
                    DisplayName = "商品货号",
                    FieldType = FieldType.String,
                    SupportedOperators = GetSupportedOperators(FieldType.String),
                    ExampleValue = "HB001"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "ProductType",
                    DisplayName = "商品类型",
                    FieldType = FieldType.Enum,
                    SupportedOperators = GetSupportedOperators(FieldType.Enum),
                    EnumOptions = new Dictionary<string, string>
                    {
                        { "0", "普通商品" },
                        { "1", "套装商品" },
                        { "2", "多码商品" }
                    }
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "DomesticPrice",
                    DisplayName = "国内价格",
                    FieldType = FieldType.Decimal,
                    SupportedOperators = GetSupportedOperators(FieldType.Decimal),
                    ExampleValue = "100.00"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "ImportPrice",
                    DisplayName = "进口价格",
                    FieldType = FieldType.Decimal,
                    SupportedOperators = GetSupportedOperators(FieldType.Decimal),
                    ExampleValue = "50.00"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "OEMPrice",
                    DisplayName = "贴牌价格",
                    FieldType = FieldType.Decimal,
                    SupportedOperators = GetSupportedOperators(FieldType.Decimal),
                    ExampleValue = "75.00"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "PackingQuantity",
                    DisplayName = "装箱数",
                    FieldType = FieldType.Integer,
                    SupportedOperators = GetSupportedOperators(FieldType.Integer),
                    ExampleValue = "12"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "UnitVolume",
                    DisplayName = "单件体积",
                    FieldType = FieldType.Decimal,
                    SupportedOperators = GetSupportedOperators(FieldType.Decimal),
                    ExampleValue = "0.5"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "SetQuantity",
                    DisplayName = "套装数量",
                    FieldType = FieldType.Integer,
                    SupportedOperators = GetSupportedOperators(FieldType.Integer),
                    ExampleValue = "3"
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "IsActive",
                    DisplayName = "状态",
                    FieldType = FieldType.Boolean,
                    SupportedOperators = GetSupportedOperators(FieldType.Boolean),
                    EnumOptions = new Dictionary<string, string>
                    {
                        { "true", "启用" },
                        { "false", "禁用" }
                    }
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "CreatedAt",
                    DisplayName = "创建时间",
                    FieldType = FieldType.DateTime,
                    SupportedOperators = GetSupportedOperators(FieldType.DateTime),
                    ExampleValue = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                },
                new BlazorApp.Shared.DTOs.FieldInfo
                {
                    FieldName = "UpdatedAt",
                    DisplayName = "更新时间",
                    FieldType = FieldType.DateTime,
                    SupportedOperators = GetSupportedOperators(FieldType.DateTime),
                    ExampleValue = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };
        }
    }
}
