using AutoMapper;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactProductMappingProfile : Profile
    {
        public ReactProductMappingProfile()
        {
            CreateMap<DIC_商品信息字典表, Product>()
                .ForMember(
                    dest => dest.UUID,
                    opt => opt.MapFrom(src => src.HGUID ?? UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt =>
                        opt.MapFrom(src =>
                            Truncate(src.H商品编码, 100) ?? UuidHelper.GenerateUuid7().ToString()
                        )
                )
                .ForMember(
                    dest => dest.ProductCategoryGUID,
                    opt => opt.MapFrom(src => Truncate(src.H商品分类码GUID, 50))
                )
                .ForMember(
                    dest => dest.LocalSupplierCode,
                    opt => opt.MapFrom(src => Truncate(src.H供货商编码, 50))
                )
                .ForMember(
                    dest => dest.ItemNumber,
                    opt => opt.MapFrom(src => Truncate(src.H货号, 50))
                )
                .ForMember(
                    dest => dest.Barcode,
                    opt => opt.MapFrom(src => Truncate(src.H主条形码, 50))
                )
                .ForMember(
                    dest => dest.ProductName,
                    opt => opt.MapFrom(src => Truncate(src.H商品名称, 50) ?? string.Empty)
                )
                .ForMember(dest => dest.ProductType, opt => opt.MapFrom(src => src.H商品类型))
                .ForMember(
                    dest => dest.MiddlePackageQuantity,
                    opt => opt.MapFrom(src => src.中包数量)
                )
                .ForMember(dest => dest.PurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(dest => dest.RetailPrice, opt => opt.MapFrom(src => src.H零售价))
                .ForMember(dest => dest.IsAutoPricing, opt => opt.MapFrom(src => src.H是否自动定价))
                .ForMember(dest => dest.ProductImage, opt => opt.MapFrom(src => src.H商品图片))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.H使用状态))
                .ForMember(
                    dest => dest.IsSpecialProduct,
                    opt => opt.MapFrom(src => src.H是否特殊商品)
                )
                .ForMember(
                    dest => dest.WarehouseCategoryGUID,
                    opt => opt.MapFrom(src => Truncate(src.CBP商品分类码GUID, 50))
                )
                .ForMember(
                    dest => dest.CreatedBy,
                    opt => opt.MapFrom(src => Truncate(src.FGC_Creator, 50) ?? "ReactSync")
                )
                .ForMember(
                    dest => dest.UpdatedBy,
                    opt => opt.MapFrom(src => Truncate(src.FGC_LastModifier, 50) ?? "ReactSync")
                )
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));
        }

        private static string? Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
