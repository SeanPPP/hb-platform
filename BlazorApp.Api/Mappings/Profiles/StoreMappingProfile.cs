using AutoMapper;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 分店数据映射配置
    /// 处理HQ实体到本地实体的映射转换
    /// </summary>
    public class StoreMappingProfile : Profile
    {
        /// <summary>
        /// 构造函数 - 配置分店相关的映射关系
        /// </summary>
        public StoreMappingProfile()
        {
            // HQ分店零售价表 -> 本地分店零售价表
            CreateMap<DIC_商品零售价表, StoreRetailPrice>()
                .ForMember(dest => dest.UUID, opt => opt.MapFrom(src => UuidHelper.GenerateUuid7()))
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.H分店代码))
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.H商品编码))
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.H供应商编码))
                .ForMember(dest => dest.PurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(
                    dest => dest.StoreRetailPriceValue,
                    opt => opt.MapFrom(src => src.H分店零售价)
                )
                .ForMember(dest => dest.DiscountRate, opt => opt.MapFrom(src => src.H折扣率))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.H使用状态))
                .ForMember(dest => dest.IsAutoPricing, opt => opt.MapFrom(src => src.H是否自动定价))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => "DataSync"))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => "DataSync"))
                // 忽略导航属性
                .ForMember(dest => dest.Product, opt => opt.Ignore())
                .ForMember(dest => dest.Store, opt => opt.Ignore());

            // HQ分店清货价表 -> 本地分店清货价表
            CreateMap<DIC_商品清货价表, StoreClearancePrice>()
                .ForMember(dest => dest.UUID, opt => opt.MapFrom(src => UuidHelper.GenerateUuid7()))
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.分店代码))
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.商品编码))
                .ForMember(dest => dest.ClearanceBarcode, opt => opt.MapFrom(src => src.清货条形码))
                .ForMember(dest => dest.ClearancePrice, opt => opt.MapFrom(src => src.清货价))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => "DataSync"))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => "DataSync"))
                // 忽略导航属性
                .ForMember(dest => dest.Product, opt => opt.Ignore())
                .ForMember(dest => dest.Store, opt => opt.Ignore());

            // HQ分店一品多码表 -> 本地分店一品多码表
            CreateMap<DIC_分店一品多码表, StoreMultiCodeProduct>()
                .ForMember(dest => dest.UUID, opt => opt.MapFrom(src => UuidHelper.GenerateUuid7()))
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.H分店代码))
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.H商品编码))
                .ForMember(
                    dest => dest.MultiCodeProductCode,
                    opt => opt.MapFrom(src => src.H多码商品编码)
                )
                .ForMember(dest => dest.MultiBarcode, opt => opt.MapFrom(src => src.H多条形码))
                .ForMember(dest => dest.PurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(dest => dest.DiscountRate, opt => opt.MapFrom(src => src.H折扣率))
                .ForMember(
                    dest => dest.MultiCodeRetailPrice,
                    opt => opt.MapFrom(src => src.H一品多码零售价)
                )
                .ForMember(
                    dest => dest.IsAutoPricing,
                    opt => opt.MapFrom(src => src.H是否自动定价 ?? false)
                )
                .ForMember(
                    dest => dest.IsSpecialProduct,
                    opt => opt.MapFrom(src => src.H是否特殊商品 ?? false)
                )
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.H使用状态 ?? true))
                .ForMember(
                    dest => dest.CreatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            src.FGC_CreateDate.HasValue ? src.FGC_CreateDate.Value : DateTime.Now
                        )
                )
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            src.FGC_LastModifyDate.HasValue
                                ? src.FGC_LastModifyDate.Value
                                : DateTime.Now
                        )
                )
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => "DataSync"))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => "DataSync"))
                // 忽略导航属性
                .ForMember(dest => dest.Product, opt => opt.Ignore())
                .ForMember(dest => dest.Store, opt => opt.Ignore());
        }
    }
}
