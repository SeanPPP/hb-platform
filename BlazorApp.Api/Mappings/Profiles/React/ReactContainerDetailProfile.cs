using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactContainerDetailProfile : Profile
    {
        public ReactContainerDetailProfile()
        {
            CreateMap<CPT_RED_货柜单详情表Store, ContainerDetail>()
                .ForMember(
                    dest => dest.ContainerCode,
                    opt => opt.MapFrom(src => TrimLen(src.主表GUID, 50) ?? string.Empty)
                )
                .ForMember(
                    dest => dest.DetailCode,
                    opt => opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? $"DETAIL_{src.ID}")
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.商品编码, 50))
                )
                .ForMember(
                    dest => dest.LoadingType,
                    opt => opt.MapFrom(src => TrimLen(src.装柜类型, 20))
                )
                .ForMember(
                    dest => dest.MixedGroupCode,
                    opt => opt.MapFrom(src => TrimLen(src.混装GUID, 50))
                )
                .ForMember(
                    dest => dest.ProductType,
                    opt => opt.MapFrom(src => TrimLen(src.商品类型, 20))
                )
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
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => TrimLen(src.备注, 500)));
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
