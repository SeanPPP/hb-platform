using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 国内供应商映射配置
    /// 包含义乌采购相关的供应商实体与DTO的映射关系
    /// </summary>
    public class DomesticSupplierMappingProfile : BaseMappingProfile
    {
        public DomesticSupplierMappingProfile()
        {
            ConfigureDomesticSupplierMappings();
            ConfigureHqSupplierMappings();
        }

        /// <summary>
        /// 配置本地国内供应商映射
        /// </summary>
        private void ConfigureDomesticSupplierMappings()
        {
            // ChinaSupplier -> DomesticSupplierDto 映射
            CreateMap<ChinaSupplier, DomesticSupplierDto>()
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => src.FGC_Creator))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => src.FGC_LastModifier));

            // CreateDomesticSupplierDto -> ChinaSupplier 映射
            CreateMap<CreateDomesticSupplierDto, ChinaSupplier>()
                .ForMember(dest => dest.Guid, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_Creator, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_LastModifier, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_CreateDate, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_LastModifyDate, opt => opt.Ignore());

            // UpdateDomesticSupplierDto -> ChinaSupplier 映射（用于更新场景）
            CreateMap<UpdateDomesticSupplierDto, ChinaSupplier>()
                .ForMember(dest => dest.Guid, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_Creator, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_LastModifier, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_CreateDate, opt => opt.Ignore())
                .ForMember(dest => dest.FGC_LastModifyDate, opt => opt.Ignore());

            // ChinaSupplier -> ChinaSupplierDto 映射（通用DTO）
            CreateMap<ChinaSupplier, ChinaSupplierDto>();

            // ChinaSupplier -> ChinaSupplierDetailDto 映射（详情DTO）
            CreateMap<ChinaSupplier, ChinaSupplierDetailDto>()
                .ForMember(dest => dest.OrderCount, opt => opt.Ignore()) // 在服务层单独设置
                .ForMember(dest => dest.TotalOrderAmount, opt => opt.Ignore()); // 在服务层单独设置

            // 创建映射（DTO -> Entity）
            CreateMap<CreateChinaSupplierDto, ChinaSupplier>()
                .ForMember(dest => dest.Guid, opt => opt.MapFrom(src => Guid.NewGuid().ToString()))
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore());

            // 更新映射（DTO -> Entity）
            CreateMap<UpdateChinaSupplierDto, ChinaSupplier>()
                .ForMember(dest => dest.Guid, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedAt, opt => opt.Ignore())
                .ForMember(dest => dest.CreatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.UpdatedBy, opt => opt.Ignore())
                .ForMember(dest => dest.IsDeleted, opt => opt.Ignore());
        }

        /// <summary>
        /// 配置从HQ数据库同步的供应商映射
        /// </summary>
        private void ConfigureHqSupplierMappings()
        {
            // 从HQ国内供应商信息表到ChinaSupplier的映射配置
            CreateMap<CBP_DIC_国内供应商信息表, ChinaSupplier>()
                .ForMember(dest => dest.Guid, opt => opt.MapFrom(src => !string.IsNullOrEmpty(src.HGUID) ? src.HGUID : Guid.NewGuid().ToString()))
                .ForMember(dest => dest.SupplierCode, opt => opt.MapFrom(src => src.H供应商编码))
                .ForMember(dest => dest.SupplierName, opt => opt.MapFrom(src => src.H供应商名称))
                .ForMember(dest => dest.ShopNumber, opt => opt.MapFrom(src => src.H商铺编号))
                .ForMember(dest => dest.ContactPerson, opt => opt.MapFrom(src => src.H联系人))
                .ForMember(dest => dest.Phone, opt => opt.MapFrom(src => src.H电话))
                .ForMember(dest => dest.Email, opt => opt.MapFrom(src => src.HEMAIL地址))
                .ForMember(dest => dest.StorefrontPhoto, opt => opt.MapFrom(src => src.H商户门头照片))
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => src.备注))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.FGC_Creator, opt => opt.MapFrom(src => src.FGC_Creator))
                .ForMember(dest => dest.FGC_CreateDate, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(dest => dest.FGC_LastModifier, opt => opt.MapFrom(src => src.FGC_LastModifier))
                .ForMember(dest => dest.FGC_LastModifyDate, opt => opt.MapFrom(src => src.FGC_LastModifyDate))
                .ForMember(dest => dest.FGC_Rowversion, opt => opt.MapFrom(src => src.FGC_Rowversion))
                .ForMember(dest => dest.FGC_UpdateHelp, opt => opt.MapFrom(src => src.FGC_UpdateHelp))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => DateTime.Now))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.Now))
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => src.FGC_Creator ?? "DataSync"))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => src.FGC_LastModifier ?? "DataSync"))
                .ForMember(dest => dest.IsDeleted, opt => opt.MapFrom(src => false));
        }
    }
}
