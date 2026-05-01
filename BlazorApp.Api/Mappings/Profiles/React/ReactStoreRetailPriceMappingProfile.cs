using AutoMapper;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactStoreRetailPriceMappingProfile : Profile
    {
        public ReactStoreRetailPriceMappingProfile()
        {
            CreateMap<DIC_商品零售价表, StoreRetailPrice>()
                .ForMember(
                    dest => dest.UUID,
                    opt => opt.MapFrom(src => src.HGUID ?? UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.H分店代码))
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.H商品编码))
                .ForMember(
                    dest => dest.StoreProductCode,
                    opt => opt.MapFrom(src => src.H分店商品编码)
                )
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.H供应商编码))
                .ForMember(dest => dest.PurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(
                    dest => dest.StoreRetailPriceValue,
                    opt => opt.MapFrom(src => src.H分店零售价)
                )
                .ForMember(dest => dest.DiscountRate, opt => opt.MapFrom(src => src.H折扣率))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.H使用状态))
                .ForMember(dest => dest.IsAutoPricing, opt => opt.MapFrom(src => src.H是否自动定价))
                .ForMember(dest => dest.IsSpecialProduct, opt => opt.MapFrom(src => src.H是否特殊商品))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => "ReactSync"))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => "ReactSync"))
                .ForMember(dest => dest.Product, opt => opt.Ignore())
                .ForMember(dest => dest.Store, opt => opt.Ignore());
        }
    }
}
