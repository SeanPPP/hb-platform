using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactLocationProfile : Profile
    {
        public ReactLocationProfile()
        {
            CreateMap<CPT_DIC_货位编码信息表, Location>()
                .ForMember(dest => dest.LocationGuid, opt => opt.MapFrom(src => src.HGUID))
                .ForMember(dest => dest.LocationType, opt => opt.MapFrom(src => src.货位类型))
                .ForMember(dest => dest.LocationCode, opt => opt.MapFrom(src => src.货位编码))
                .ForMember(dest => dest.LocationBarcode, opt => opt.MapFrom(src => src.货位条形码))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态));
        }
    }
}
