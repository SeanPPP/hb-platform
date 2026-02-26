using AutoMapper;
using BlazorApp.Api.Utils;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 国内商品映射配置
    /// </summary>
    public class DomesticProductMappingProfile : BaseMappingProfile
    {
        public DomesticProductMappingProfile()
        {
            // DomesticProduct -> DomesticProductDto 映射
            CreateMap<DomesticProduct, DomesticProductDto>()
                .ForMember(
                    dest => dest.SupplierName,
                    opt =>
                        opt.MapFrom(src => src.Supplier != null ? src.Supplier.SupplierName : null)
                )
                .ForMember(
                    dest => dest.SetProducts,
                    opt => opt.MapFrom(src => src.DomesticSetProducts)
                );

            // CreateDomesticProductDto -> DomesticProduct 映射
            CreateMap<CreateDomesticProductDto, DomesticProduct>()
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore()) // 自动生成
                .ForMember(dest => dest.HBProductNo, opt => opt.Ignore()) // 自动生成或使用提供的值
                .ForMember(dest => dest.Barcode, opt => opt.Ignore()) // 自动生成或使用提供的值
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());

            // UpdateDomesticProductDto -> DomesticProduct 映射（用于更新场景）
            CreateMap<UpdateDomesticProductDto, DomesticProduct>()
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.SupplierCode, opt => opt.Ignore())
                .ForMember(dest => dest.HBProductNo, opt => opt.Ignore())
                .ForMember(dest => dest.Barcode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore())
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());

            // DomesticProduct -> DomesticProductDetailDto 映射
            CreateMap<DomesticProduct, DomesticProductDetailDto>()
                .ForMember(
                    dest => dest.SupplierName,
                    opt =>
                        opt.MapFrom(src => src.Supplier != null ? src.Supplier.SupplierName : null)
                )
                .ForMember(dest => dest.Supplier, opt => opt.MapFrom(src => src.Supplier))
                .ForMember(dest => dest.SetProducts, opt => opt.Ignore()); // 需要在服务中单独设置

            // BatchProductItem -> DomesticProduct 映射（用于批量创建）
            CreateMap<BatchProductItem, DomesticProduct>()
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore()) // 自动生成
                .ForMember(dest => dest.SupplierCode, opt => opt.Ignore()) // 从父DTO获取
                .ForMember(dest => dest.HBProductNo, opt => opt.Ignore()) // 自动生成
                .ForMember(dest => dest.Barcode, opt => opt.Ignore()) // 自动生成
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());

            // BatchProductInputDto -> DomesticProduct 映射（用于批量创建和检测）
            CreateMap<BatchProductInputDto, DomesticProduct>()
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore()) // 自动生成
                .ForMember(dest => dest.SupplierCode, opt => opt.Ignore()) // 从父DTO获取
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());

            // CPT_DIC_商品信息字典表 -> DomesticProduct 映射（用于HQ数据同步）
            CreateMap<CPT_DIC_商品信息字典表, DomesticProduct>()
                .ForMember(
                    dest => dest.ProductCode,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.商品编码)
                                ? src.商品编码
                                : UuidHelper.GenerateUuid7()
                        )
                )
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.供应商编码))
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.中文名称))
                .ForMember(dest => dest.EnglishProductName, opt => opt.MapFrom(src => src.英文名称))
                .ForMember(dest => dest.HBProductNo, opt => opt.MapFrom(src => src.HB货号))
                .ForMember(dest => dest.Barcode, opt => opt.MapFrom(src => src.条形码))
                .ForMember(dest => dest.ProductSpecification, opt => opt.MapFrom(src => src.规格))
                .ForMember(dest => dest.ProductType, opt => opt.MapFrom(src => src.商品类型 ?? 0))
                .ForMember(dest => dest.DomesticPrice, opt => opt.MapFrom(src => src.国内价格))
                .ForMember(dest => dest.OEMPrice, opt => opt.MapFrom(src => src.贴牌价格))
                .ForMember(dest => dest.ImportPrice, opt => opt.MapFrom(src => src.进口价格))
                .ForMember(dest => dest.UnitVolume, opt => opt.MapFrom(src => src.单件体积))
                .ForMember(
                    dest => dest.PackingQuantity,
                    opt => opt.MapFrom(src => (int?)src.单件装箱数)
                )
                .ForMember(
                    dest => dest.MiddlePackQuantity,
                    opt => opt.MapFrom(src => (int?)src.中包数量)
                )
                .ForMember(
                    dest => dest.ProductImage,
                    opt =>
                        opt.MapFrom(src =>
                            // 优先使用商品图片字段，但需要先修复可能存在的重复URL
                            !string.IsNullOrEmpty(src.商品图片)
                                ? ImageUrlHelper.FixDuplicateUrl(src.商品图片) ?? src.商品图片
                                : (
                                    !string.IsNullOrEmpty(src.HB货号)
                                    && !src.HB货号.StartsWith("http://")
                                    && !src.HB货号.StartsWith("https://")
                                        ? $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{src.HB货号}.jpg"
                                        : null
                                )
                        )
                )
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.使用状态 == 1))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false))
                .ForMember(dest => dest.Supplier, opt => opt.Ignore());
        }
    }
}
