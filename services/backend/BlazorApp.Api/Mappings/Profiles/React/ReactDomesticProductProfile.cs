using AutoMapper;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactDomesticProductProfile : Profile
    {
        // 商品信息字典表 映射到 DomesticProduct
        public ReactDomesticProductProfile()
        {
            CreateMap<CPT_DIC_商品信息字典表, DomesticProduct>()
                .ForMember(
                    dest => dest.ProductCode,
                    opt =>
                        opt.MapFrom(src => TrimLen(src.商品编码, 50) ?? UuidHelper.GenerateUuid7())
                )
                .ForMember(
                    dest => dest.SupplierCode,
                    opt => opt.MapFrom(src => TrimLen(src.供应商编码, 50))
                )
                .ForMember(
                    dest => dest.ProductName,
                    opt => opt.MapFrom(src => TrimLen(src.中文名称, 200))
                )
                .ForMember(
                    dest => dest.EnglishProductName,
                    opt => opt.MapFrom(src => TrimLen(src.英文名称, 500))
                )
                .ForMember(
                    dest => dest.HBProductNo,
                    opt => opt.MapFrom(src => TrimLen(src.HB货号, 50))
                )
                .ForMember(dest => dest.Barcode, opt => opt.MapFrom(src => TrimLen(src.条形码, 50)))
                .ForMember(dest => dest.ProductType, opt => opt.MapFrom(src => src.商品类型 ?? 0))
                .ForMember(dest => dest.DomesticPrice, opt => opt.MapFrom(src => src.国内价格))
                .ForMember(dest => dest.ImportPrice, opt => opt.MapFrom(src => src.进口价格))
                .ForMember(dest => dest.OEMPrice, opt => opt.MapFrom(src => src.贴牌价格))
                .ForMember(
                    dest => dest.PackingQuantity,
                    opt =>
                        opt.MapFrom(src =>
                            src.单件装箱数.HasValue ? (int?)(int)src.单件装箱数.Value : null
                        )
                )
                .ForMember(dest => dest.UnitVolume, opt => opt.MapFrom(src => src.单件体积))
                .ForMember(
                    dest => dest.MiddlePackQuantity,
                    opt =>
                        opt.MapFrom(src =>
                            src.中包数量.HasValue ? (int?)(int)src.中包数量.Value : null
                        )
                )
                .ForMember(
                    dest => dest.ProductImage,
                    opt => opt.MapFrom(src => TrimLen(src.商品图片, 500))
                )
                .ForMember(
                    dest => dest.IsActive,
                    opt => opt.MapFrom(src => (src.使用状态 ?? 0) == 1)
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
