using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 购物车映射配置
    /// 包含购物车和购物车项相关的实体与DTO的映射关系
    /// </summary>
    public class CartMappingProfile : BaseMappingProfile
    {
        public CartMappingProfile()
        {
            ConfigureCartMappings();
            ConfigureCartItemMappings();
        }

        /// <summary>
        /// 配置购物车映射
        /// </summary>
        private void ConfigureCartMappings()
        {
            // Cart -> CartDto 映射
            CreateMap<Cart, CartDto>()
                .ForMember(dest => dest.UserGUID, opt => opt.MapFrom(src => src.UserGUID))
                .ForMember(dest => dest.StoreGUID, opt => opt.MapFrom(src => src.StoreGUID))
                .ForMember(dest => dest.CartItems, opt => opt.MapFrom(src => src.CartItems));

            // CartDto -> Cart 反向映射（用于更新场景）
            CreateMap<CartDto, Cart>()
                .ForMember(dest => dest.CartGUID, opt => opt.MapFrom(src => src.CartGUID ?? Guid.NewGuid().ToString()))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore()) // 不覆盖创建时间
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.Now))
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore()) // 保持现有状态
                .ForMember(dest => dest.User, opt => opt.Ignore()) // 导航属性忽略
                .ForMember(dest => dest.Store, opt => opt.Ignore()); // 导航属性忽略
        }

        /// <summary>
        /// 配置购物车项映射
        /// </summary>
        private void ConfigureCartItemMappings()
        {
            // CartItem -> CartItemDto 映射
            CreateMap<CartItem, CartItemDto>()
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.ProductCode))
                .ForMember(dest => dest.ItemNumber, opt => opt.MapFrom(src => src.ItemNumber))
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.ProductName))
                .ForMember(dest => dest.LocationCode, opt => opt.MapFrom(src =>
                    (src.Product != null && src.Product.Locations != null)
                        ? src.Product.Locations.Select(l => l.LocationCode).FirstOrDefault()
                        : string.Empty
                ))
                .ForMember(dest => dest.TotalPrice, opt => opt.MapFrom(src => src.TotalPrice ?? src.UnitPrice * src.Quantity));

            // CartItemDto -> CartItem 反向映射（用于更新场景）
            CreateMap<CartItemDto, CartItem>()
                .ForMember(dest => dest.CartItemGUID, opt => opt.MapFrom(src => src.CartItemGUID ?? Guid.NewGuid().ToString()))
                .ForMember(dest => dest.TotalPrice, opt => opt.MapFrom(src => src.TotalPrice ?? src.UnitPrice * src.Quantity))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore()) // 不覆盖创建时间
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.Now))
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore()) // 保持现有状态
                .ForMember(dest => dest.Cart, opt => opt.Ignore()) // 导航属性忽略
                .ForMember(dest => dest.Product, opt => opt.Ignore()); // 导航属性忽略
        }
    }
}
