using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactWareHouseOrderDetailMappingProfile : Profile
    {
        public ReactWareHouseOrderDetailMappingProfile()
        {
            CreateMap<CBP_RED_分店订单详情表Store, WareHouseOrderDetails>()
                .ForMember(
                    dest => dest.DetailGUID,
                    opt =>
                        opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? Guid.NewGuid().ToString("N"))
                )
                .ForMember(
                    dest => dest.OrderGUID,
                    opt => opt.MapFrom(src => TrimLen(src.主表GUID, 50))
                )
                .ForMember(
                    dest => dest.StoreCode,
                    opt => opt.MapFrom(src => TrimLen(src.分店代码, 50))
                )
                .ForMember(
                    dest => dest.StoreProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.分店商品编码, 50))
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.商品编码, 50))
                )
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.数量))
                .ForMember(dest => dest.AllocQuantity, opt => opt.MapFrom(src => src.配货数量))
                .ForMember(dest => dest.LastCost, opt => opt.MapFrom(src => src.上次成本))
                .ForMember(dest => dest.ImportPrice, opt => opt.MapFrom(src => src.进口价格))
                .ForMember(dest => dest.ImportAmount, opt => opt.MapFrom(src => src.合计进口金额))
                .ForMember(dest => dest.OEMPrice, opt => opt.MapFrom(src => src.贴牌价格))
                .ForMember(dest => dest.OEMAmount, opt => opt.MapFrom(src => src.合计贴牌金额));
        }

        private static string? TrimLen(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s))
                return s;
            var t = s.Trim();
            return t.Length <= max ? t : t.Substring(0, max);
        }
    }
}
