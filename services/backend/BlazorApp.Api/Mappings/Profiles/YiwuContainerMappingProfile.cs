using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 义乌货柜AutoMapper映射配置
    /// </summary>
    public class YiwuContainerMappingProfile : Profile
    {
        public YiwuContainerMappingProfile()
        {
            // Container -> YiwuContainerDto
            CreateMap<Container, YiwuContainerDto>()
                .ReverseMap();

            // ContainerDetail -> YiwuContainerDetailDto
            CreateMap<ContainerDetail, YiwuContainerDetailDto>()
                .ForMember(dest => dest.Product, opt => opt.MapFrom(src => src.Product != null ? new ProductInfoDto
                {
                    ProductCode = src.Product.ProductCode,
                    ItemNumber = src.Product.HBProductNo,
                    Barcode = src.Product.Barcode,
                    ChineseName = src.Product.ProductName,
                    EnglishName = src.Product.EnglishProductName,
                    ImageUrl = src.Product.ProductImage,
                    Specification = src.Product.ProductSpecification,

                } : null))
                .ReverseMap()
                .ForMember(dest => dest.Product, opt => opt.Ignore()); // 反向映射时忽略Product属性

            // HQ实体到本地实体的映射配置
            // CPT_RED_货柜单主表Store -> Container
            CreateMap<CPT_RED_货柜单主表Store, Container>()
                .ForMember(dest => dest.ContainerCode, opt => opt.MapFrom(src => src.HGUID ?? Guid.NewGuid().ToString()))
                .ForMember(dest => dest.ContainerNumber, opt => opt.MapFrom(src => src.货柜编号))
                .ForMember(dest => dest.LoadingDate, opt => opt.MapFrom(src => src.装柜日期))
                .ForMember(dest => dest.EstimatedArrivalDate, opt => opt.MapFrom(src => src.预计到岸日期))
                .ForMember(dest => dest.ActualArrivalDate, opt => opt.MapFrom(src => src.实际到货日期))
                .ForMember(dest => dest.TotalPieces, opt => opt.MapFrom(src => src.合计件数))
                .ForMember(dest => dest.TotalQuantity, opt => opt.MapFrom(src => src.合计数量))
                .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => src.合计金额))
                .ForMember(dest => dest.TotalVolume, opt => opt.MapFrom(src => src.总体积))
                .ForMember(dest => dest.CostFloatRate, opt => opt.MapFrom(src => src.成本浮率))
                .ForMember(dest => dest.ExchangeRate, opt => opt.MapFrom(src => src.汇率))
                .ForMember(dest => dest.ShippingFee, opt => opt.MapFrom(src => src.运费))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => src.备注))
                .ForMember(dest => dest.Remarks2, opt => opt.MapFrom(src => src.备注2))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate ?? DateTime.Now))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.FGC_LastModifyDate ?? DateTime.Now))
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => src.FGC_Creator))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => src.FGC_LastModifier));

            // CPT_RED_货柜单详情表Store -> ContainerDetail
            CreateMap<CPT_RED_货柜单详情表Store, ContainerDetail>()
                .ForMember(dest => dest.DetailCode, opt => opt.MapFrom(src => src.HGUID ?? Guid.NewGuid().ToString()))
                .ForMember(dest => dest.ContainerCode, opt => opt.MapFrom(src => src.主表GUID ?? ""))
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.商品编码))
                .ForMember(dest => dest.LoadingType, opt => opt.MapFrom(src => src.装柜类型))
                .ForMember(dest => dest.MixedGroupCode, opt => opt.MapFrom(src => src.混装GUID))
                .ForMember(dest => dest.ProductType, opt => opt.MapFrom(src => src.商品类型))
                .ForMember(dest => dest.SetQuantity, opt => opt.MapFrom(src => src.套装数量))
                .ForMember(dest => dest.LoadingPieces, opt => opt.MapFrom(src => src.装柜件数))
                .ForMember(dest => dest.LoadingQuantity, opt => opt.MapFrom(src => src.装柜数量))
                .ForMember(dest => dest.DomesticPrice, opt => opt.MapFrom(src => src.国内价格))
                .ForMember(dest => dest.AdjustmentRate, opt => opt.MapFrom(src => src.调整浮率))
                .ForMember(dest => dest.ImportPrice, opt => opt.MapFrom(src => src.进口价格))
                .ForMember(dest => dest.OEMPrice, opt => opt.MapFrom(src => src.贴牌价格))
                .ForMember(dest => dest.PackingQuantity, opt => opt.MapFrom(src => src.单件装箱数))
                .ForMember(dest => dest.UnitVolume, opt => opt.MapFrom(src => src.单件体积))
                .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => src.合计装柜金额))
                .ForMember(dest => dest.TotalVolume, opt => opt.MapFrom(src => src.合计装柜体积))
                .ForMember(dest => dest.TransportCost, opt => opt.MapFrom(src => src.运输成本))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => src.备注))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate ?? DateTime.Now))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.FGC_LastModifyDate ?? DateTime.Now))
                .ForMember(dest => dest.CreatedBy, opt => opt.MapFrom(src => src.FGC_Creator))
                .ForMember(dest => dest.UpdatedBy, opt => opt.MapFrom(src => src.FGC_LastModifier));
        }
    }
}
