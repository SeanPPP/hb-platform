using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// 货柜映射配置
    /// 包含货柜相关的实体与DTO的映射关系
    /// </summary>
    public class ContainerMappingProfile : BaseMappingProfile
    {
        public ContainerMappingProfile()
        {
            ConfigureContainerMappings();
        }

        /// <summary>
        /// 配置货柜相关的映射
        /// </summary>
        private void ConfigureContainerMappings()
        {
            // CPT_RED_货柜单主表Store -> ContainerMainDto 映射（HQ数据库）
            CreateMap<CPT_RED_货柜单主表Store, ContainerMainDto>()
                .ForMember(
                    dest => dest.Details,
                    opt => opt.MapFrom(src => src.Details.Where(d => d.商品信息 != null))
                );

            // CPT_RED_货柜单详情表Store -> ContainerDetailDto 映射（HQ数据库）
            CreateMap<CPT_RED_货柜单详情表Store, ContainerDetailDto>()
                .ForMember(dest => dest.商品信息, opt => opt.MapFrom(src => src.商品信息));

            // CPT_DIC_商品信息字典表 -> ContainerProductInfoDto 映射（HQ数据库）
            CreateMap<CPT_DIC_商品信息字典表, ContainerProductInfoDto>()
                .ForMember(dest => dest.货号, opt => opt.MapFrom(src => src.HB货号 ?? src.工厂货号))
                .ForMember(
                    dest => dest.商品名称,
                    opt => opt.MapFrom(src => src.中文名称 ?? src.英文名称)
                ) // 优先显示英文翻译
                .ForMember(dest => dest.英文名称, opt => opt.MapFrom(src => src.英文名称))
                .ForMember(dest => dest.商品图片, opt => opt.MapFrom(src => src.商品图片))
                .ForMember(dest => dest.零售价格, opt => opt.MapFrom(src => src.进口价格)) // 使用进口价格作为零售价格
                .ForMember(dest => dest.商品规格, opt => opt.MapFrom(src => src.规格))
                .ForMember(dest => dest.单位, opt => opt.MapFrom(src => src.单位))
                .ForMember(dest => dest.单件装箱数, opt => opt.MapFrom(src => src.单件装箱数))
                .ForMember(dest => dest.单件体积, opt => opt.MapFrom(src => src.单件体积))
                .ForMember(
                    dest => dest.商品类型,
                    opt =>
                        opt.MapFrom(src =>
                            src.商品类型.HasValue
                                ? src.商品类型.Value == 0
                                    ? "普通商品"
                                    : src.商品类型.Value == 1
                                        ? "套装商品"
                                        : src.商品类型.Value == 2
                                            ? "套装子商品"
                                            : "未知"
                                : null
                        )
                )
                .ForMember(dest => dest.套装数量, opt => opt.MapFrom(src => src.套装数量));

            // Container -> ContainerMainDto 映射（本地数据库）
            CreateMap<Container, ContainerMainDto>()
                .ForMember(dest => dest.HGUID, opt => opt.MapFrom(src => src.ContainerCode))
                .ForMember(dest => dest.货柜编号, opt => opt.MapFrom(src => src.ContainerNumber))
                .ForMember(dest => dest.装柜日期, opt => opt.MapFrom(src => src.LoadingDate))
                .ForMember(
                    dest => dest.预计到岸日期,
                    opt => opt.MapFrom(src => src.EstimatedArrivalDate)
                )
                .ForMember(
                    dest => dest.实际到货日期,
                    opt => opt.MapFrom(src => src.ActualArrivalDate)
                )
                .ForMember(dest => dest.合计件数, opt => opt.MapFrom(src => src.TotalPieces))
                .ForMember(dest => dest.合计数量, opt => opt.MapFrom(src => src.TotalQuantity))
                .ForMember(dest => dest.合计金额, opt => opt.MapFrom(src => src.TotalAmount))
                .ForMember(dest => dest.总体积, opt => opt.MapFrom(src => src.TotalVolume))
                .ForMember(dest => dest.成本浮率, opt => opt.MapFrom(src => src.CostFloatRate))
                .ForMember(dest => dest.汇率, opt => opt.MapFrom(src => src.ExchangeRate))
                .ForMember(dest => dest.运费, opt => opt.MapFrom(src => src.ShippingFee))
                .ForMember(dest => dest.备注, opt => opt.MapFrom(src => src.Remarks))
                .ForMember(dest => dest.状态, opt => opt.MapFrom(src => src.Status))
                .ForMember(
                    dest => dest.Details,
                    opt => opt.MapFrom(src => src.Details.Where(d => d.Product != null))
                );

            // ContainerDetail -> ContainerDetailDto 映射（本地数据库）
            CreateMap<ContainerDetail, ContainerDetailDto>()
                .ForMember(dest => dest.HGUID, opt => opt.MapFrom(src => src.DetailCode))
                .ForMember(dest => dest.主表GUID, opt => opt.MapFrom(src => src.ContainerCode))
                .ForMember(dest => dest.商品编码, opt => opt.MapFrom(src => src.ProductCode))
                .ForMember(
                    dest => dest.LocalSupplierCode,
                    opt =>
                        opt.MapFrom(src =>
                            src.LocalProduct != null ? src.LocalProduct.LocalSupplierCode : null
                        )
                )
                .ForMember(dest => dest.商品信息, opt => opt.MapFrom(src => src.Product))
                .ForMember(
                    dest => dest.是否新商品,
                    opt => opt.MapFrom(src => src.LocalProduct == null)
                )
                .ForMember(dest => dest.装柜类型, opt => opt.MapFrom(src => src.LoadingType))
                .ForMember(dest => dest.商品类型, opt => opt.MapFrom(src => src.ProductType))
                .ForMember(dest => dest.套装数量, opt => opt.MapFrom(src => src.SetQuantity))
                .ForMember(dest => dest.装柜件数, opt => opt.MapFrom(src => src.LoadingPieces))
                .ForMember(dest => dest.装柜数量, opt => opt.MapFrom(src => src.LoadingQuantity))
                .ForMember(dest => dest.国内价格, opt => opt.MapFrom(src => src.DomesticPrice))
                .ForMember(dest => dest.调整浮率, opt => opt.MapFrom(src => src.AdjustmentRate))
                .ForMember(dest => dest.进口价格, opt => opt.MapFrom(src => src.ImportPrice))
                .ForMember(dest => dest.贴牌价格, opt => opt.MapFrom(src => src.OEMPrice))
                .ForMember(dest => dest.LastImportPrice, opt => opt.MapFrom(src => src.LastImportPrice))
                .ForMember(dest => dest.LastOEMPrice, opt => opt.MapFrom(src => src.LastOEMPrice))
                .ForMember(
                    dest => dest.WarehouseImportPrice,
                    opt => opt.MapFrom(src => src.WarehouseProduct != null ? src.WarehouseProduct.ImportPrice : null)
                )
                .ForMember(
                    dest => dest.WarehouseOEMPrice,
                    opt => opt.MapFrom(src => src.WarehouseProduct != null ? src.WarehouseProduct.OEMPrice : null)
                )
                .ForMember(dest => dest.单件装箱数, opt => opt.MapFrom(src => src.PackingQuantity))
                .ForMember(dest => dest.单件体积, opt => opt.MapFrom(src => src.UnitVolume))
                .ForMember(dest => dest.合计装柜金额, opt => opt.MapFrom(src => src.TotalAmount))
                .ForMember(dest => dest.合计装柜体积, opt => opt.MapFrom(src => src.TotalVolume))
                .ForMember(dest => dest.运输成本, opt => opt.MapFrom(src => src.TransportCost))
                .ForMember(dest => dest.备注, opt => opt.MapFrom(src => src.Remarks))
                .ForMember(dest => dest.商品信息, opt => opt.MapFrom(src => src.Product))
                .AfterMap(
                    (src, dest) =>
                    {
                        // 货柜明细的本地供应商编码来自本地 Product 表。
                        if (dest.商品信息 != null)
                        {
                            dest.商品信息.LocalSupplierCode = src.LocalProduct?.LocalSupplierCode;
                        }
                    }
                );

            // DomesticProduct -> ContainerProductInfoDto 映射（本地数据库）
            CreateMap<DomesticProduct, ContainerProductInfoDto>()
                .ForMember(dest => dest.商品编码, opt => opt.MapFrom(src => src.ProductCode))
                .ForMember(dest => dest.货号, opt => opt.MapFrom(src => src.HBProductNo))
                .ForMember(
                    dest => dest.商品名称,
                    opt => opt.MapFrom(src => src.ProductName ?? src.EnglishProductName)
                ) // 优先显示英文名称
                .ForMember(dest => dest.LocalSupplierCode, opt => opt.Ignore())
                .ForMember(dest => dest.英文名称, opt => opt.MapFrom(src => src.EnglishProductName))
                .ForMember(dest => dest.条形码, opt => opt.MapFrom(src => src.Barcode))
                .ForMember(dest => dest.商品图片, opt => opt.MapFrom(src => src.ProductImage))
                .ForMember(dest => dest.零售价格, opt => opt.MapFrom(src => src.ImportPrice)) // 使用进口价格作为零售价格
                .ForMember(
                    dest => dest.商品规格,
                    opt => opt.MapFrom(src => src.ProductSpecification)
                )
                .ForMember(dest => dest.单位, opt => opt.Ignore()) // DomesticProduct 没有单位字段，忽略
                .ForMember(dest => dest.单件装箱数, opt => opt.MapFrom(src => src.PackingQuantity))
                .ForMember(dest => dest.单件体积, opt => opt.MapFrom(src => src.UnitVolume))
                .ForMember(
                    dest => dest.商品类型,
                    opt =>
                        opt.MapFrom(src =>
                            src.ProductType == 0 ? "普通商品"
                            : src.ProductType == 1 ? "套装商品"
                            : src.ProductType == 2 ? "套装子商品"
                            : "未知"
                        )
                )
                .ForMember(dest => dest.套装数量, opt => opt.Ignore()); // DomesticProduct 没有直接的套装数量字段，忽略
        }
    }
}
