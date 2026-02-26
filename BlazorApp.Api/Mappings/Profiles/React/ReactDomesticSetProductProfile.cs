using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactDomesticSetProductProfile : Profile
    {
        public ReactDomesticSetProductProfile()
        {
            CreateMap<CPT_DIC_商品套装信息表, DomesticSetProduct>()
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.商品编码, 50) ?? string.Empty)
                )
                .ForMember(
                    dest => dest.SetProductNo,
                    opt =>
                        opt.MapFrom(src =>
                            TrimLen(src.商品小货号, 50)
                            ?? TrimLen(src.条形码, 50)
                            ?? TrimLen(src.商品编码, 50)
                            ?? string.Empty
                        )
                )
                .ForMember(
                    dest => dest.SetBarcode,
                    opt => opt.MapFrom(src => TrimLen(src.条形码, 50))
                )
                .ForMember(dest => dest.DomesticPrice, opt => opt.MapFrom(src => src.国内价格))
                .ForMember(dest => dest.ImportPrice, opt => opt.MapFrom(src => src.进口价格))
                .ForMember(dest => dest.OEMPrice, opt => opt.MapFrom(src => src.贴牌价格))
                .ForMember(
                    dest => dest.Remarks,
                    opt => opt.MapFrom(src => TrimLen(src.备注, 1000))
                );
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
