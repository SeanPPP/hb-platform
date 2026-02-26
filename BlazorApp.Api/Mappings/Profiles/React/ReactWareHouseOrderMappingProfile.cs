using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactWareHouseOrderMappingProfile : Profile
    {
        public ReactWareHouseOrderMappingProfile()
        {
            CreateMap<CBP_RED_分店订货单主表Store, WareHouseOrder>()
                .ForMember(
                    dest => dest.OrderGUID,
                    opt =>
                        opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? Guid.NewGuid().ToString("N"))
                )
                .ForMember(
                    dest => dest.StoreCode,
                    opt => opt.MapFrom(src => TrimLen(src.分店代码, 50))
                )
                .ForMember(dest => dest.OrderNo, opt => opt.MapFrom(src => TrimLen(src.订单号, 50)))
                .ForMember(dest => dest.OrderDate, opt => opt.MapFrom(src => src.订单日期))
                .ForMember(dest => dest.OutboundDate, opt => opt.MapFrom(src => src.出库日期))
                .ForMember(dest => dest.ShippingFee, opt => opt.MapFrom(src => src.运输费用))
                .ForMember(
                    dest => dest.ImportTotalAmount,
                    opt => opt.MapFrom(src => src.进口总金额)
                )
                .ForMember(dest => dest.OEMTotalAmount, opt => opt.MapFrom(src => src.贴牌总金额))
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => TrimLen(src.备注, 1000)))
                .ForMember(
                    dest => dest.FlowStatus,
                    opt => opt.MapFrom(src => (src.流程状态 == 1 && src.入库状态 == 1) ? 2 : 1)
                )
                .ForMember(dest => dest.InboundStatus, opt => opt.MapFrom(src => src.入库状态));
        }

        private static string? TrimLen(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s))
                return s;
            var t = s.Trim();
            return t.Length <= max ? t : t.Substring(0, max);
        }
    }
}
