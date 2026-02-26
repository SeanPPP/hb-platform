using AutoMapper;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactProductSetCodeMappingProfile : Profile
    {
        public ReactProductSetCodeMappingProfile()
        {
            CreateMap<DIC_一品多码表, ProductSetCode>()
                .ForMember(
                    dest => dest.SetCodeId,
                    opt => opt.MapFrom(src => UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => Truncate(src.H商品编码, 50))
                )
                .ForMember(
                    dest => dest.SetProductCode,
                    opt => opt.MapFrom(src => src.H多码商品编号)
                )
                .ForMember(dest => dest.SetItemNumber, opt => opt.MapFrom(src => src.H多码商品编号))
                .ForMember(
                    dest => dest.SetBarcode,
                    opt =>
                        opt.MapFrom(src =>
                            Truncate(src.H多条形码, 50) ?? Truncate(src.H主条形码, 50)
                        )
                )
                .ForMember(dest => dest.SetPurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(
                    dest => dest.SetRetailPrice,
                    opt => opt.MapFrom(src => src.H一品多码零售价)
                )
                .ForMember(dest => dest.SetQuantity, opt => opt.MapFrom(src => 1))
                .ForMember(dest => dest.SetType, opt => opt.MapFrom(src => 2))
                .ForMember(
                    dest => dest.IsActive,
                    opt => opt.MapFrom(src => src.H使用状态 ?? false)
                );
        }

        private static string? Truncate(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s))
                return s;
            var t = s.Trim();
            return t.Length <= max ? t : t.Substring(0, max);
        }
    }
}
