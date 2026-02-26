using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Mappings.Profiles
{
    /// <summary>
    /// PDA仓库订单映射配置
    /// 包含PDA仓库订单、订单明细相关的实体与DTO的映射关系
    /// </summary>
    public class PDAWarehouseOrderMappingProfile : Profile
    {
        public PDAWarehouseOrderMappingProfile()
        {
            ConfigurePDAWarehouseOrderMappings();
        }

        /// <summary>
        /// 配置PDA仓库订单映射
        /// </summary>
        private void ConfigurePDAWarehouseOrderMappings()
        {
            CreateMap<WareHouseOrder, PDAWarehouseOrderDto>()
                .ForMember(
                    dest => dest.OrderGUID,
                    opt => opt.MapFrom(src => src.OrderGUID ?? string.Empty)
                )
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.StoreCode))
                .ForMember(dest => dest.OrderNo, opt => opt.MapFrom(src => src.OrderNo))
                .ForMember(dest => dest.OrderDate, opt => opt.MapFrom(src => src.OrderDate))
                .ForMember(dest => dest.OutboundDate, opt => opt.MapFrom(src => src.OutboundDate))
                .ForMember(dest => dest.ShippingFee, opt => opt.MapFrom(src => src.ShippingFee))
                .ForMember(
                    dest => dest.ImportTotalAmount,
                    opt => opt.MapFrom(src => src.ImportTotalAmount)
                )
                .ForMember(
                    dest => dest.OEMTotalAmount,
                    opt => opt.MapFrom(src => src.OEMTotalAmount)
                )
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => src.Remarks))
                .ForMember(dest => dest.FlowStatus, opt => opt.MapFrom(src => src.FlowStatus))
                .ForMember(dest => dest.InboundStatus, opt => opt.MapFrom(src => src.InboundStatus))
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => src.UpdatedAt))
                .ForMember(dest => dest.FlowStatusText, opt => opt.Ignore())
                .ForMember(dest => dest.InboundStatusText, opt => opt.Ignore())
                .ForMember(dest => dest.OrderDetails, opt => opt.Ignore());
        }
    }
}
