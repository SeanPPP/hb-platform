using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactStoreClearancePriceMappingProfile : Profile
    {
        public ReactStoreClearancePriceMappingProfile()
        {
            CreateMap<DIC_商品清货价表, StoreClearancePrice>()
                .ForMember(dest => dest.UUID, opt => opt.MapFrom(src => src.HGUID))
                .ForMember(
                    dest => dest.StoreCode,
                    opt => opt.MapFrom(src => TrimLen(src.分店代码, 50))
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.商品编码, 50))
                )
                .ForMember(
                    dest => dest.ClearanceBarcode,
                    opt => opt.MapFrom(src => TrimLen(src.清货条形码, 50))
                )
                .ForMember(dest => dest.ClearancePrice, opt => opt.MapFrom(src => src.清货价));
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
