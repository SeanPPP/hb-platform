using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 产品套装多码映射配置
    /// </summary>
    public class ProductSetCodeMappingProfile : BaseMappingProfile
    {
        public ProductSetCodeMappingProfile()
        {
            CreateMap<ProductSetCode, ProductSetCodeDto>()
                .ForMember(
                    dest => dest.ProductName,
                    opt => opt.MapFrom(src => src.Product != null ? src.Product.ProductName : null)
                );

            CreateMap<CreateProductSetCodeDto, ProductSetCode>()
                .ForMember(
                    dest => dest.SetCodeId,
                    opt => opt.MapFrom(src => UuidHelper.GenerateUuid7())
                )
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));

            CreateMap<UpdateProductSetCodeDto, ProductSetCode>()
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.UtcNow))
                .ForMember(dest => dest.SetCodeId, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore());

            // 一品多码表到ProductSetCode的映射（多码信息）
            CreateMap<DIC_一品多码表, ProductSetCode>()
                .ForMember(
                    dest => dest.SetCodeId,
                    opt => opt.MapFrom(src => UuidHelper.GenerateUuid7())
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => src.H商品编码 ?? UuidHelper.GenerateUuid7().ToString())
                ) // 🔗 使用商品编码关联
                .ForMember(
                    dest => dest.SetItemNumber,
                    opt => opt.MapFrom(src => src.H多码商品编号 ?? "")
                )
                .ForMember(
                    dest => dest.SetProductCode,
                    opt =>
                        opt.MapFrom(src =>
                            src.H多码商品编号 ?? UuidHelper.GenerateUuid7().ToString()
                        )
                ) // 🔗 使用多码商品编码关联
                .ForMember(dest => dest.SetBarcode, opt => opt.MapFrom(src => src.H多条形码))
                .ForMember(dest => dest.SetPurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(
                    dest => dest.SetRetailPrice,
                    opt => opt.MapFrom(src => src.H一品多码零售价)
                )
                .ForMember(dest => dest.SetType, opt => opt.MapFrom(src => 2)) // 多码套装类型为固定套装
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.H使用状态 ?? false))
                .ForMember(
                    dest => dest.CreatedAt,
                    opt => opt.MapFrom(src => src.FGC_CreateDate ?? DateTime.UtcNow)
                )
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate ?? DateTime.UtcNow)
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

    /// <summary>
    /// 产品套装多码映射扩展方法
    /// 提供手动映射的便捷方法
    /// </summary>
    public static class ProductSetCodeMappingExtensions
    {
        /// <summary>
        /// 将ProductSetCode实体转换为ProductSetCodeDto
        /// </summary>
        /// <param name="entity">ProductSetCode实体</param>
        /// <returns>ProductSetCodeDto</returns>
        public static ProductSetCodeDto ToDto(this ProductSetCode entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            return new ProductSetCodeDto
            {
                SetCodeId = entity.SetCodeId,
                ProductCode = entity.ProductCode,
                SetItemNumber = entity.SetItemNumber,
                SetBarcode = entity.SetBarcode,

                SetPurchasePrice = entity.SetPurchasePrice,
                SetRetailPrice = entity.SetRetailPrice,
                SetType = entity.SetType,
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                CreatedBy = entity.CreatedBy,
                UpdatedBy = entity.UpdatedBy,
                IsDeleted = entity.IsDeleted,
                // 关联数据
                ProductName = entity.Product?.ProductName,
            };
        }

        /// <summary>
        /// 将CreateProductSetCodeDto转换为ProductSetCode实体
        /// </summary>
        /// <param name="createDto">CreateProductSetCodeDto</param>
        /// <param name="createdBy">创建者</param>
        /// <returns>ProductSetCode实体</returns>
        public static ProductSetCode ToEntity(
            this CreateProductSetCodeDto createDto,
            string? createdBy = null
        )
        {
            if (createDto == null)
                throw new ArgumentNullException(nameof(createDto));

            var now = DateTime.UtcNow;
            return new ProductSetCode
            {
                SetCodeId = UuidHelper.GenerateUuid7(),
                ProductCode = createDto.ProductCode,
                SetItemNumber = createDto.SetItemNumber,
                SetBarcode = createDto.SetBarcode,

                SetPurchasePrice = createDto.SetPurchasePrice,
                SetRetailPrice = createDto.SetRetailPrice,
                SetType = createDto.SetType,
                IsActive = createDto.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = createdBy,
                UpdatedBy = createdBy,
                IsDeleted = false,
            };
        }

        /// <summary>
        /// 使用UpdateProductSetCodeDto更新ProductSetCode实体
        /// </summary>
        /// <param name="entity">要更新的实体</param>
        /// <param name="updateDto">更新数据</param>
        /// <param name="updatedBy">更新者</param>
        public static void UpdateFrom(
            this ProductSetCode entity,
            UpdateProductSetCodeDto updateDto,
            string? updatedBy = null
        )
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (updateDto == null)
                throw new ArgumentNullException(nameof(updateDto));

            entity.ProductCode = updateDto.ProductCode;
            entity.SetItemNumber = updateDto.SetItemNumber;
            entity.SetBarcode = updateDto.SetBarcode;

            entity.SetPurchasePrice = updateDto.SetPurchasePrice;
            entity.SetRetailPrice = updateDto.SetRetailPrice;
            entity.SetType = updateDto.SetType;
            entity.IsActive = updateDto.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = updatedBy;
        }

        /// <summary>
        /// 批量转换ProductSetCode实体列表为ProductSetCodeDto列表
        /// </summary>
        /// <param name="entities">ProductSetCode实体列表</param>
        /// <returns>ProductSetCodeDto列表</returns>
        public static List<ProductSetCodeDto> ToDtoList(this IEnumerable<ProductSetCode> entities)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            return entities.Select(e => e.ToDto()).ToList();
        }
    }
}
