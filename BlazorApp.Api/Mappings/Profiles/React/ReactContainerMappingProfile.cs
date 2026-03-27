using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactContainerMappingProfile : Profile
    {
        public ReactContainerMappingProfile()
        {
            CreateMap<CPT_RED_货柜单主表Store, Container>()
                .ForMember(
                    dest => dest.ContainerCode,
                    opt => opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? $"CONTAINER_{src.ID}")
                )
                .ForMember(
                    dest => dest.ContainerNumber,
                    opt => opt.MapFrom(src => TrimLen(src.货柜编号, 50))
                )
                .ForMember(dest => dest.LoadingDate, opt => opt.MapFrom(src => src.装柜日期))
                .ForMember(
                    dest => dest.EstimatedArrivalDate,
                    opt => opt.MapFrom(src => src.预计到岸日期)
                )
                .ForMember(
                    dest => dest.ActualArrivalDate,
                    opt => opt.MapFrom(src => src.实际到货日期)
                )
                .ForMember(dest => dest.TotalPieces, opt => opt.MapFrom(src => src.合计件数))
                .ForMember(dest => dest.TotalQuantity, opt => opt.MapFrom(src => src.合计数量))
                .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => src.合计金额))
                .ForMember(dest => dest.TotalVolume, opt => opt.MapFrom(src => src.总体积))
                .ForMember(dest => dest.CostFloatRate, opt => opt.MapFrom(src => src.成本浮率))
                .ForMember(dest => dest.ExchangeRate, opt => opt.MapFrom(src => src.汇率))
                .ForMember(dest => dest.ShippingFee, opt => opt.MapFrom(src => src.运费))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => TrimLen(src.备注, 1000)))
                .ForMember(
                    dest => dest.Remarks2,
                    opt => opt.MapFrom(src => TrimLen(src.备注2, 1000))
                );
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
