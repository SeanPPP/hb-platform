using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 套装商品映射配置
    /// </summary>
    public class DomesticSetProductMappingProfile : BaseMappingProfile
    {
        public DomesticSetProductMappingProfile()
        {
            // DomesticSetProduct -> DomesticSetProductDto
            CreateMap<DomesticSetProduct, DomesticSetProductDto>()
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.DomesticProduct != null ? src.DomesticProduct.ProductName : null))
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.DomesticProduct != null ? src.DomesticProduct.SupplierCode : null))
                .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => 
                    src.DomesticProduct != null && src.DomesticProduct.Supplier != null 
                        ? src.DomesticProduct.Supplier.SupplierName 
                        : null));

            // DomesticSetProduct -> DomesticSetProductDetailDto
            CreateMap<DomesticSetProduct, DomesticSetProductDetailDto>()
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.DomesticProduct != null ? src.DomesticProduct.ProductName : null))
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.DomesticProduct != null ? src.DomesticProduct.SupplierCode : null))
                .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => 
                    src.DomesticProduct != null && src.DomesticProduct.Supplier != null 
                        ? src.DomesticProduct.Supplier.SupplierName 
                        : null))
                .ForMember(dest => dest.Product, opt => opt.MapFrom(src => src.DomesticProduct));

            // CreateDomesticSetProductDto -> DomesticSetProduct
            CreateMap<CreateDomesticSetProductDto, DomesticSetProduct>()
                .ForMember(dest => dest.SetProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.DomesticProduct, opt => opt.Ignore());

            // UpdateDomesticSetProductDto -> DomesticSetProduct
            CreateMap<UpdateDomesticSetProductDto, DomesticSetProduct>()
                .ForMember(dest => dest.SetProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.ProductNo, opt => opt.Ignore())
                .ForMember(dest => dest.SetProductNo, opt => opt.Ignore())
                .ForMember(dest => dest.SetBarcode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.DomesticProduct, opt => opt.Ignore());

            // BatchSetProductItem -> DomesticSetProduct
            CreateMap<BatchSetProductItem, DomesticSetProduct>()
                .ForMember(dest => dest.SetProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.ProductNo, opt => opt.Ignore())
                .ForMember(dest => dest.SetProductNo, opt => opt.Ignore())
                .ForMember(dest => dest.SetBarcode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.DomesticProduct, opt => opt.Ignore());
        }
    }
}