using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 义乌订单映射配置
    /// 包含义乌订单相关的实体与DTO的映射关系
    /// </summary>
    public class YiwuOrderMappingProfile : BaseMappingProfile
    {
        public YiwuOrderMappingProfile()
        {
            ConfigureYiwuOrderMappings();
        }

        /// <summary>
        /// 配置义乌订单相关的映射
        /// </summary>
        private void ConfigureYiwuOrderMappings()
        {
            // CreateYiwuOrderDetailDto -> YIWU_OrderDetail 映射
            CreateMap<CreateYiwuOrderDetailDto, YIWU_OrderDetail>()
                .ForMember(dest => dest.ID, opt => opt.Ignore())
                .ForMember(dest => dest.OrderNo, opt => opt.Ignore()) // 在服务层设置
                .ForMember(dest => dest.Order, opt => opt.Ignore())
                .ForMember(dest => dest.OrderAmount, opt => opt.Ignore()) // 自动计算
                .ForMember(dest => dest.OrderVolume, opt => opt.Ignore()) // 自动计算
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore()) // 在服务层设置
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore()) // 在服务层设置
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));

            // UpdateYiwuOrderDto -> YIWU_Order 映射
            CreateMap<UpdateYiwuOrderDto, YIWU_Order>()
                .ForMember(dest => dest.OrderNo, opt => opt.Ignore()) // 不允许修改订单编号
                .ForMember(dest => dest.TotalAmount, opt => opt.Ignore()) // 自动计算
                .ForMember(dest => dest.TotalVolume, opt => opt.Ignore()) // 自动计算
                .ForMember(dest => dest.OrderDetails, opt => opt.Ignore())
                .ForMember(dest => dest.ChinaSupplier, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore()) // 在服务层设置
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore());
        }
    }
}
