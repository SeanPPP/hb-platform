using AutoMapper;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 基础映射配置抽象类
    /// 提供通用的映射辅助方法
    /// </summary>
    public abstract class BaseMappingProfile : Profile
    {
        /// <summary>
        /// 截断字符串到指定长度
        /// </summary>
        /// <param name="value">要截断的字符串</param>
        /// <param name="maxLength">最大长度</param>
        /// <returns>截断后的字符串</returns>
        protected string? TruncateString(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }
    }
}
