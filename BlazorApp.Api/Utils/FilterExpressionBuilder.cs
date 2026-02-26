using System.Linq.Expressions;
using System.Reflection;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// 过滤表达式构建器
    /// </summary>
    public static class FilterExpressionBuilder
    {
        /// <summary>
        /// 验证过滤条件组是否有效
        /// </summary>
        /// <param name="filterGroup">过滤条件组</param>
        /// <returns>是否有效</returns>
        public static bool IsValidFilterGroup(FilterGroup? filterGroup)
        {
            if (filterGroup == null)
                return false;

            // 检查是否有有效的条件或子分组
            var hasValidConditions =
                filterGroup.Conditions?.Any(c => c != null && !string.IsNullOrEmpty(c.FieldName))
                == true;
            var hasValidSubGroups =
                filterGroup.SubGroups?.Any(sg => IsValidFilterGroup(sg)) == true;

            return hasValidConditions || hasValidSubGroups;
        }

        /// <summary>
        /// 构建过滤表达式
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="filterGroup">过滤条件组</param>
        /// <returns>过滤表达式</returns>
        public static Expression<Func<T, bool>>? BuildFilterExpression<T>(FilterGroup? filterGroup)
        {
            if (
                filterGroup == null
                || (filterGroup.Conditions?.Any() != true && filterGroup.SubGroups?.Any() != true)
            )
                return null;

            var parameter = Expression.Parameter(typeof(T), "x");
            var expression = BuildGroupExpression<T>(filterGroup, parameter);

            if (expression == null)
                return null;

            return Expression.Lambda<Func<T, bool>>(expression, parameter);
        }

        /// <summary>
        /// 构建分组表达式
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="group">过滤分组</param>
        /// <param name="parameter">参数表达式</param>
        /// <returns>布尔表达式</returns>
        private static Expression? BuildGroupExpression<T>(
            FilterGroup group,
            ParameterExpression parameter
        )
        {
            if (group == null)
                return null;

            var expressions = new List<Expression>();

            // 处理条件
            if (group.Conditions != null)
            {
                foreach (var condition in group.Conditions)
                {
                    if (condition != null)
                    {
                        var conditionExpression = BuildConditionExpression<T>(condition, parameter);
                        if (conditionExpression != null)
                        {
                            expressions.Add(conditionExpression);
                        }
                    }
                }
            }

            // 处理子分组
            if (group.SubGroups != null)
            {
                foreach (var subGroup in group.SubGroups)
                {
                    if (subGroup != null)
                    {
                        var subExpression = BuildGroupExpression<T>(subGroup, parameter);
                        if (subExpression != null)
                        {
                            expressions.Add(subExpression);
                        }
                    }
                }
            }

            if (!expressions.Any())
                return null;

            // 根据逻辑操作符组合表达式
            Expression result = expressions[0];
            for (int i = 1; i < expressions.Count; i++)
            {
                result =
                    group.LogicalOperator == LogicalOperator.And
                        ? Expression.AndAlso(result, expressions[i])
                        : Expression.OrElse(result, expressions[i]);
            }

            return result;
        }

        /// <summary>
        /// 构建单个条件表达式
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="condition">过滤条件</param>
        /// <param name="parameter">参数表达式</param>
        /// <returns>布尔表达式</returns>
        private static Expression? BuildConditionExpression<T>(
            FilterCondition condition,
            ParameterExpression parameter
        )
        {
            try
            {
                if (condition == null || string.IsNullOrEmpty(condition.FieldName))
                    return null;

                var property = GetPropertyExpression<T>(condition.FieldName, parameter);
                if (property == null)
                    return null;

                return condition.Operator switch
                {
                    FilterOperator.Equals => BuildEqualsExpression(
                        property,
                        condition.Value,
                        condition.IgnoreCase
                    ),
                    FilterOperator.NotEquals => BuildNotEqualsExpression(
                        property,
                        condition.Value,
                        condition.IgnoreCase
                    ),
                    FilterOperator.Contains => BuildContainsExpression(
                        property,
                        condition.Value,
                        condition.IgnoreCase
                    ),
                    FilterOperator.NotContains => BuildNotContainsExpression(
                        property,
                        condition.Value,
                        condition.IgnoreCase
                    ),
                    FilterOperator.StartsWith => BuildStartsWithExpression(
                        property,
                        condition.Value,
                        condition.IgnoreCase
                    ),
                    FilterOperator.EndsWith => BuildEndsWithExpression(
                        property,
                        condition.Value,
                        condition.IgnoreCase
                    ),
                    FilterOperator.IsNull => BuildIsNullExpression(property),
                    FilterOperator.IsNotNull => BuildIsNotNullExpression(property),
                    FilterOperator.GreaterThan => BuildGreaterThanExpression(
                        property,
                        condition.Value
                    ),
                    FilterOperator.LessThan => BuildLessThanExpression(property, condition.Value),
                    FilterOperator.GreaterThanOrEqual => BuildGreaterThanOrEqualExpression(
                        property,
                        condition.Value
                    ),
                    FilterOperator.LessThanOrEqual => BuildLessThanOrEqualExpression(
                        property,
                        condition.Value
                    ),
                    FilterOperator.Between => BuildBetweenExpression(
                        property,
                        condition.Value,
                        condition.SecondValue
                    ),
                    FilterOperator.NotBetween => BuildNotBetweenExpression(
                        property,
                        condition.Value,
                        condition.SecondValue
                    ),
                    FilterOperator.In => BuildInExpression(property, condition.Value),
                    FilterOperator.NotIn => BuildNotInExpression(property, condition.Value),
                    _ => null,
                };
            }
            catch (Exception)
            {
                // 如果构建表达式失败，返回null，不影响其他条件
                return null;
            }
        }

        /// <summary>
        /// 获取属性表达式
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="propertyName">属性名</param>
        /// <param name="parameter">参数表达式</param>
        /// <returns>属性表达式</returns>
        private static Expression? GetPropertyExpression<T>(
            string propertyName,
            ParameterExpression parameter
        )
        {
            if (string.IsNullOrEmpty(propertyName))
                return null;

            var properties = propertyName.Split('.');
            Expression property = parameter;

            foreach (var prop in properties)
            {
                var propertyInfo = property.Type.GetProperty(
                    prop,
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance
                );
                if (propertyInfo == null)
                    return null;

                property = Expression.Property(property, propertyInfo);
            }

            return property;
        }

        /// <summary>
        /// 构建等于表达式
        /// </summary>
        private static Expression BuildEqualsExpression(
            Expression property,
            object? value,
            bool ignoreCase
        )
        {
            if (value == null)
                return BuildIsNullExpression(property);

            var constantValue = Expression.Constant(
                ConvertValue(value, property.Type),
                property.Type
            );

            if (property.Type == typeof(string) && ignoreCase)
            {
                var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
                var propertyToLower = Expression.Call(property, toLowerMethod);
                var valueToLower = Expression.Call(constantValue, toLowerMethod);
                return Expression.Equal(propertyToLower, valueToLower);
            }

            return Expression.Equal(property, constantValue);
        }

        /// <summary>
        /// 构建不等于表达式
        /// </summary>
        private static Expression BuildNotEqualsExpression(
            Expression property,
            object? value,
            bool ignoreCase
        )
        {
            return Expression.Not(BuildEqualsExpression(property, value, ignoreCase));
        }

        /// <summary>
        /// 构建包含表达式
        /// </summary>
        private static Expression? BuildContainsExpression(
            Expression property,
            object? value,
            bool ignoreCase
        )
        {
            if (property.Type != typeof(string) || value == null)
                return null;

            var stringValue = value.ToString() ?? "";
            var constantValue = Expression.Constant(stringValue);

            if (ignoreCase)
            {
                var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
                var propertyToLower = Expression.Call(property, toLowerMethod);
                var valueToLower = Expression.Call(constantValue, toLowerMethod);
                var containsMethod = typeof(string).GetMethod(
                    "Contains",
                    new[] { typeof(string) }
                )!;
                return Expression.Call(propertyToLower, containsMethod, valueToLower);
            }
            else
            {
                var containsMethod = typeof(string).GetMethod(
                    "Contains",
                    new[] { typeof(string) }
                )!;
                return Expression.Call(property, containsMethod, constantValue);
            }
        }

        /// <summary>
        /// 构建不包含表达式
        /// </summary>
        private static Expression? BuildNotContainsExpression(
            Expression property,
            object? value,
            bool ignoreCase
        )
        {
            var containsExpression = BuildContainsExpression(property, value, ignoreCase);
            return containsExpression != null ? Expression.Not(containsExpression) : null;
        }

        /// <summary>
        /// 构建开头是表达式
        /// </summary>
        private static Expression? BuildStartsWithExpression(
            Expression property,
            object? value,
            bool ignoreCase
        )
        {
            if (property.Type != typeof(string) || value == null)
                return null;

            var stringValue = value.ToString() ?? "";
            var constantValue = Expression.Constant(stringValue);

            if (ignoreCase)
            {
                var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
                var propertyToLower = Expression.Call(property, toLowerMethod);
                var valueToLower = Expression.Call(constantValue, toLowerMethod);
                var startsWithMethod = typeof(string).GetMethod(
                    "StartsWith",
                    new[] { typeof(string) }
                )!;
                return Expression.Call(propertyToLower, startsWithMethod, valueToLower);
            }
            else
            {
                var startsWithMethod = typeof(string).GetMethod(
                    "StartsWith",
                    new[] { typeof(string) }
                )!;
                return Expression.Call(property, startsWithMethod, constantValue);
            }
        }

        /// <summary>
        /// 构建结尾是表达式
        /// </summary>
        private static Expression? BuildEndsWithExpression(
            Expression property,
            object? value,
            bool ignoreCase
        )
        {
            if (property.Type != typeof(string) || value == null)
                return null;

            var stringValue = value.ToString() ?? "";
            var constantValue = Expression.Constant(stringValue);

            if (ignoreCase)
            {
                var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
                var propertyToLower = Expression.Call(property, toLowerMethod);
                var valueToLower = Expression.Call(constantValue, toLowerMethod);
                var endsWithMethod = typeof(string).GetMethod(
                    "EndsWith",
                    new[] { typeof(string) }
                )!;
                return Expression.Call(propertyToLower, endsWithMethod, valueToLower);
            }
            else
            {
                var endsWithMethod = typeof(string).GetMethod(
                    "EndsWith",
                    new[] { typeof(string) }
                )!;
                return Expression.Call(property, endsWithMethod, constantValue);
            }
        }

        /// <summary>
        /// 构建为空表达式
        /// </summary>
        private static Expression BuildIsNullExpression(Expression property)
        {
            if (property.Type == typeof(string))
            {
                var nullCheck = Expression.Equal(
                    property,
                    Expression.Constant(null, typeof(string))
                );
                var emptyCheck = Expression.Equal(property, Expression.Constant(""));
                return Expression.OrElse(nullCheck, emptyCheck);
            }
            else if (Nullable.GetUnderlyingType(property.Type) != null)
            {
                return Expression.Equal(property, Expression.Constant(null, property.Type));
            }
            else
            {
                return Expression.Constant(false); // 非可空类型永远不为null
            }
        }

        /// <summary>
        /// 构建不为空表达式
        /// </summary>
        private static Expression BuildIsNotNullExpression(Expression property)
        {
            return Expression.Not(BuildIsNullExpression(property));
        }

        /// <summary>
        /// 构建大于表达式
        /// </summary>
        private static Expression? BuildGreaterThanExpression(Expression property, object? value)
        {
            if (value == null)
                return null;

            var convertedValue = ConvertValue(value, property.Type);
            if (convertedValue == null)
                return null;

            var constantValue = Expression.Constant(convertedValue, property.Type);
            return Expression.GreaterThan(property, constantValue);
        }

        /// <summary>
        /// 构建小于表达式
        /// </summary>
        private static Expression? BuildLessThanExpression(Expression property, object? value)
        {
            if (value == null)
                return null;

            var convertedValue = ConvertValue(value, property.Type);
            if (convertedValue == null)
                return null;

            var constantValue = Expression.Constant(convertedValue, property.Type);
            return Expression.LessThan(property, constantValue);
        }

        /// <summary>
        /// 构建大于等于表达式
        /// </summary>
        private static Expression? BuildGreaterThanOrEqualExpression(
            Expression property,
            object? value
        )
        {
            if (value == null)
                return null;

            var convertedValue = ConvertValue(value, property.Type);
            if (convertedValue == null)
                return null;

            var constantValue = Expression.Constant(convertedValue, property.Type);
            return Expression.GreaterThanOrEqual(property, constantValue);
        }

        /// <summary>
        /// 构建小于等于表达式
        /// </summary>
        private static Expression? BuildLessThanOrEqualExpression(
            Expression property,
            object? value
        )
        {
            if (value == null)
                return null;

            var convertedValue = ConvertValue(value, property.Type);
            if (convertedValue == null)
                return null;

            var constantValue = Expression.Constant(convertedValue, property.Type);
            return Expression.LessThanOrEqual(property, constantValue);
        }

        /// <summary>
        /// 构建范围表达式
        /// </summary>
        private static Expression? BuildBetweenExpression(
            Expression property,
            object? value1,
            object? value2
        )
        {
            if (value1 == null || value2 == null)
                return null;

            var convertedValue1 = ConvertValue(value1, property.Type);
            var convertedValue2 = ConvertValue(value2, property.Type);

            if (convertedValue1 == null || convertedValue2 == null)
                return null;

            var constantValue1 = Expression.Constant(convertedValue1, property.Type);
            var constantValue2 = Expression.Constant(convertedValue2, property.Type);

            var greaterThan = Expression.GreaterThanOrEqual(property, constantValue1);
            var lessThan = Expression.LessThanOrEqual(property, constantValue2);

            return Expression.AndAlso(greaterThan, lessThan);
        }

        /// <summary>
        /// 构建不在范围表达式
        /// </summary>
        private static Expression? BuildNotBetweenExpression(
            Expression property,
            object? value1,
            object? value2
        )
        {
            var betweenExpression = BuildBetweenExpression(property, value1, value2);
            return betweenExpression != null ? Expression.Not(betweenExpression) : null;
        }

        /// <summary>
        /// 构建在列表中表达式
        /// </summary>
        private static Expression? BuildInExpression(Expression property, object? value)
        {
            if (value == null)
                return null;

            // 处理数组或列表
            var valueType = value.GetType();
            if (
                valueType.IsArray
                || (
                    valueType.IsGenericType
                    && valueType.GetGenericTypeDefinition() == typeof(List<>)
                )
            )
            {
                var values = ((System.Collections.IEnumerable)value).Cast<object>().ToList();
                if (!values.Any())
                    return Expression.Constant(false);

                Expression? result = null;
                foreach (var item in values)
                {
                    var equalsExpression = BuildEqualsExpression(property, item, false);
                    result =
                        result == null
                            ? equalsExpression
                            : Expression.OrElse(result, equalsExpression);
                }

                return result;
            }

            return null;
        }

        /// <summary>
        /// 构建不在列表中表达式
        /// </summary>
        private static Expression? BuildNotInExpression(Expression property, object? value)
        {
            var inExpression = BuildInExpression(property, value);
            return inExpression != null ? Expression.Not(inExpression) : null;
        }

        /// <summary>
        /// 转换值类型
        /// </summary>
        /// <param name="value">原始值</param>
        /// <param name="targetType">目标类型</param>
        /// <returns>转换后的值</returns>
        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null)
                return null;

            try
            {
                // 处理可空类型
                var underlyingType = Nullable.GetUnderlyingType(targetType);
                if (underlyingType != null)
                    targetType = underlyingType;

                // 如果类型相同，直接返回
                if (value.GetType() == targetType)
                    return value;

                // 字符串转换
                if (value is string stringValue)
                {
                    if (targetType == typeof(DateTime))
                        return DateTime.Parse(stringValue);

                    if (targetType == typeof(decimal))
                        return decimal.Parse(stringValue);

                    if (targetType == typeof(int))
                        return int.Parse(stringValue);

                    if (targetType == typeof(double))
                        return double.Parse(stringValue);

                    if (targetType == typeof(bool))
                        return bool.Parse(stringValue);
                }

                // 使用Convert类进行转换
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                return null;
            }
        }
    }
}
