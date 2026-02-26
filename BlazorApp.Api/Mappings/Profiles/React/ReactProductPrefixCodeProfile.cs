using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactProductPrefixCodeProfile : Profile
    {
        public ReactProductPrefixCodeProfile()
        {
            CreateMap<CPT_DIC_货号前缀信息表, ProductPrefixCode>()
                .ForMember(
                    dest => dest.SupplierCode,
                    opt => opt.MapFrom(src => TrimLen(src.供应商编码, 50) ?? string.Empty)
                )
                .ForMember(
                    dest => dest.PrefixName,
                    opt => opt.MapFrom(src => TrimLen(src.HB货号前缀码, 10) ?? string.Empty)
                )
                .ForMember(
                    dest => dest.PrefixDescription,
                    opt => opt.MapFrom(src => TrimLen(src.前缀描述, 200))
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
