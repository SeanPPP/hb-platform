using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 商品前缀映射配置
    /// </summary>
    public class ProductPrefixCodeMappingProfile : BaseMappingProfile
    {
        public ProductPrefixCodeMappingProfile()
        {
            // ProductPrefixCode -> ProductPrefixCodeDto 映射
            CreateMap<ProductPrefixCode, ProductPrefixCodeDto>()
                .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.Supplier != null ? src.Supplier.SupplierName : null));

            // CreateProductPrefixCodeDto -> ProductPrefixCode 映射
            CreateMap<CreateProductPrefixCodeDto, ProductPrefixCode>()
                .ForMember(dest => dest.PrefixCode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());

            // UpdateProductPrefixCodeDto -> ProductPrefixCode 映射（用于更新场景）
            CreateMap<UpdateProductPrefixCodeDto, ProductPrefixCode>()
                .ForMember(dest => dest.PrefixCode, opt => opt.Ignore())
                .ForMember(dest => dest.SupplierCode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());

            // ProductPrefixCode -> ProductPrefixCodeDetailDto 映射
            CreateMap<ProductPrefixCode, ProductPrefixCodeDetailDto>()
                .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.Supplier != null ? src.Supplier.SupplierName : null))
                .ForMember(dest => dest.Supplier, opt => opt.MapFrom(src => src.Supplier))
                .ForMember(dest => dest.ProductCount, opt => opt.Ignore()); // 需要在服务中单独设置

            // ProductPrefixCode -> SimpleProductPrefixCodeDto 映射
            CreateMap<ProductPrefixCode, SimpleProductPrefixCodeDto>();
        }
    }
}
