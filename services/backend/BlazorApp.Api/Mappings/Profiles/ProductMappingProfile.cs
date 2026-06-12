using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 商品映射配置
    /// 包含商品相关的实体与DTO的映射关系，以及从HQ数据库同步商品的映射
    /// </summary>
    public class ProductMappingProfile : BaseMappingProfile
    {
        public ProductMappingProfile()
        {
            ConfigureProductMappings();
            ConfigureHqProductMappings();
            ConfigureLocationMappings();
            ConfigureProductLocationMappings();
        }

        /// <summary>
        /// 配置本地商品映射
        /// </summary>
        private void ConfigureProductMappings()
        {
            // Product -> ProductDto 映射
            CreateMap<Product, ProductDto>();

            // CreateProductDto -> Product 映射
            CreateMap<CreateProductDto, Product>()
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow));

            // UpdateProductDto -> Product 映射
            CreateMap<UpdateProductDto, Product>()
                .ForMember(dest => dest.ProductCode, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow));
        }

        /// <summary>
        /// 配置从HQ数据库同步的商品映射
        /// </summary>
        private void ConfigureHqProductMappings()
        {
            // 添加从HQ商品信息字典表到本地Product实体的映射
            CreateMap<DIC_商品信息字典表, Product>()
                .ForMember(
                    dest => dest.ProductCode,
                    opt =>
                        opt.MapFrom(src =>
                            TruncateString(src.H商品编码, 100) ?? UuidHelper.GenerateUuid7().ToString()
                        )
                )
                .ForMember(
                    dest => dest.ProductCategoryGUID,
                    opt => opt.MapFrom(src => TruncateString(src.H商品分类码GUID, 50))
                )
                .ForMember(
                    dest => dest.LocalSupplierCode,
                    opt => opt.MapFrom(src => TruncateString(src.H供货商编码, 50))
                )
                .ForMember(
                    dest => dest.ItemNumber,
                    opt => opt.MapFrom(src => TruncateString(src.H货号, 50))
                )
                .ForMember(
                    dest => dest.Barcode,
                    opt => opt.MapFrom(src => TruncateString(src.H主条形码, 50))
                )
                .ForMember(
                    dest => dest.ProductName,
                    opt => opt.MapFrom(src => TruncateString(src.H商品名称, 50) ?? "")
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
                    opt => opt.MapFrom(src => TruncateString(src.CBP商品分类码GUID, 50))
                )
                .ForMember(
                    dest => dest.CreatedBy,
                    opt => opt.MapFrom(src => TruncateString(src.FGC_Creator, 50) ?? "System")
                )
                .ForMember(
                    dest => dest.UpdatedBy,
                    opt => opt.MapFrom(src => TruncateString(src.FGC_LastModifier, 50) ?? "System")
                )
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));
        }

        /// <summary>
        /// 配置货位映射
        /// </summary>
        private void ConfigureLocationMappings()
        {
            // Location -> LocationDto 映射
            CreateMap<Location, LocationDto>();

            // CreateLocationDto -> Location 映射
            CreateMap<CreateLocationDto, Location>()
                .ForMember(
                    dest => dest.LocationGuid,
                    opt => opt.MapFrom(src => UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow));

            // UpdateLocationDto -> Location 映射
            CreateMap<UpdateLocationDto, Location>()
                .ForMember(dest => dest.LocationGuid, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow));

            // 添加从HQ货位编码信息表到本地Location实体的映射
            CreateMap<CPT_DIC_货位编码信息表, Location>()
                .ForMember(
                    dest => dest.LocationGuid,
                    opt => opt.MapFrom(src => src.HGUID ?? UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(dest => dest.LocationType, opt => opt.MapFrom(src => src.货位类型))
                .ForMember(dest => dest.LocationCode, opt => opt.MapFrom(src => src.货位编码))
                .ForMember(dest => dest.LocationBarcode, opt => opt.MapFrom(src => src.货位条形码))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => src.FGC_Creator))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => src.FGC_LastModifier))
                .ForMember(
                    dest => dest.CreatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.FGC_CreateDate)
                                ? DateTime.Parse(src.FGC_CreateDate)
                                : DateTime.Now
                        )
                )
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.FGC_LastModifyDate)
                                ? DateTime.Parse(src.FGC_LastModifyDate)
                                : (DateTime?)null
                        )
                )
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));
        }

        /// <summary>
        /// 配置商品货位关系映射
        /// </summary>
        private void ConfigureProductLocationMappings()
        {
            // ProductLocation -> ProductLocationDto 映射
            CreateMap<ProductLocation, ProductLocationDto>();

            // CreateProductLocationDto -> ProductLocation 映射
            CreateMap<CreateProductLocationDto, ProductLocation>()
                .ForMember(dest => dest.Guid, opt => opt.MapFrom(src => UuidHelper.GenerateUuid7().ToString()))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow));

            // UpdateProductLocationDto -> ProductLocation 映射
            CreateMap<UpdateProductLocationDto, ProductLocation>()
                .ForMember(dest => dest.Guid, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow));

            // 从HQ货位存货信息表到ProductLocation的映射配置
            CreateMap<CPT_RED_货位存货信息表, ProductLocation>()
                .ForMember(
                    dest => dest.Guid,
                    opt => opt.MapFrom(src => src.HGUID ?? UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.商品编码))
                .ForMember(dest => dest.LocationGuid, opt => opt.MapFrom(src => src.货位编码)) // 注意：这里可能需要转换为LocationGuid
                .ForMember(
                    dest => dest.CreatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.FGC_CreateDate)
                                ? DateTime.Parse(src.FGC_CreateDate)
                                : DateTime.Now
                        )
                )
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.FGC_LastModifyDate)
                                ? DateTime.Parse(src.FGC_LastModifyDate)
                                : DateTime.Now
                        )
                )
                .ForMember(
                    dest => dest.CreatedBy,
                    opt => opt.MapFrom(src => src.FGC_Creator ?? "DataSync")
                )
                .ForMember(
                    dest => dest.UpdatedBy,
                    opt => opt.MapFrom(src => src.FGC_LastModifier ?? "DataSync")
                )
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));

            // 从HQ货位配货信息表到ProductLocation的映射配置
            CreateMap<CPT_RED_货位配货信息表, ProductLocation>()
                .ForMember(
                    dest => dest.Guid,
                    opt => opt.MapFrom(src => src.HGUID ?? UuidHelper.GenerateUuid7().ToString())
                )
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.商品编码))
                .ForMember(dest => dest.LocationGuid, opt => opt.MapFrom(src => src.货位编码)) // 注意：这里可能需要转换为LocationGuid
                .ForMember(
                    dest => dest.CreatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.FGC_CreateDate)
                                ? DateTime.Parse(src.FGC_CreateDate)
                                : DateTime.Now
                        )
                )
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt =>
                        opt.MapFrom(src =>
                            !string.IsNullOrEmpty(src.FGC_LastModifyDate)
                                ? DateTime.Parse(src.FGC_LastModifyDate)
                                : DateTime.Now
                        )
                )
                .ForMember(
                    dest => dest.CreatedBy,
                    opt => opt.MapFrom(src => src.FGC_Creator ?? "DataSync")
                )
                .ForMember(
                    dest => dest.UpdatedBy,
                    opt => opt.MapFrom(src => src.FGC_LastModifier ?? "DataSync")
                )
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));
        }
    }
}
