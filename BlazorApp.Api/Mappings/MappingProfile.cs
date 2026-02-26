using AutoMapper;
using BlazorApp.Api.Mappings.Profiles;

namespace BlazorApp.Api.Mappings
{
    /// <summary>
    /// 主映射配置文件
    /// 按照最佳实践重构，将不同业务领域的映射关系分离到独立的Profile文件中
    /// 这样提高了代码的可维护性和可读性
    /// </summary>
    public class MappingProfile : Profile
    {
        /// <summary>
        /// 构造函数 - 注册所有映射Profile
        /// </summary>
        public MappingProfile()
        {
            // 注册所有分离的映射配置文件
            // 每个Profile负责一个特定的业务领域
            
            // ⚠️ 注意：AutoMapper会自动扫描并包含同一程序集中的所有Profile类
            // 不需要手动IncludeProfile，只要Profile类继承自Profile即可
            // 
            // 以下Profile将被自动包含：
            // - UserMappingProfile (用户和角色管理)
            // - DomesticSupplierMappingProfile (国内供应商管理)
            // - ProductMappingProfile (商品管理)
            // - WarehouseMappingProfile (仓库管理)
            // - CartMappingProfile (购物车)
            // - ContainerMappingProfile (货柜)
            // - YiwuOrderMappingProfile (义乌订单)
        }
    }
}
