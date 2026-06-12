using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactWarehouseProductStockProfile : Profile
    {
        public ReactWarehouseProductStockProfile()
        {
            CreateMap<CBP_DIC_商品库存表, WarehouseProduct>()
                .ForMember(
                    dest => dest.ProductCode,
                    opt =>
                        opt.MapFrom(src =>
                            TrimLen(src.H商品编码, 50) ?? Guid.NewGuid().ToString("N")
                        )
                )
                .ForMember(dest => dest.DomesticPrice, opt => opt.MapFrom(src => src.H国内价格))
                .ForMember(dest => dest.OEMPrice, opt => opt.MapFrom(src => src.H贴牌价格))
                .ForMember(dest => dest.ImportPrice, opt => opt.MapFrom(src => src.H进口价格))
                .ForMember(dest => dest.StockQuantity, opt => opt.MapFrom(src => ToInt(src.H库存)))
                .ForMember(
                    dest => dest.MinOrderQuantity,
                    opt => opt.MapFrom(src => ToInt(src.H最小订货量))
                )
                .ForMember(dest => dest.StockValue, opt => opt.MapFrom(src => src.H库存金额))
                .ForMember(
                    dest => dest.StockAlertQuantity,
                    opt => opt.MapFrom(src => src.H库存预警数)
                )
                .ForMember(
                    dest => dest.IsActive,
                    opt => opt.MapFrom(src => (src.H使用状态 ?? 0) == 1)
                )
                .ForMember(dest => dest.Volume, opt => opt.Ignore());
        }

        private static string? TrimLen(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s))
                return s;
            var t = s.Trim();
            return t.Length <= max ? t : t.Substring(0, max);
        }

        private static int? ToInt(decimal? v)
        {
            if (!v.HasValue)
                return null;
            var n = (int)Math.Round(v.Value, MidpointRounding.AwayFromZero);
            return n >= 0 ? n : null;
        }
    }
}
