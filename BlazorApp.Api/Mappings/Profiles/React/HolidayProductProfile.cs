using AutoMapper;
using BlazorApp.Api.Mappings.Profiles;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class HolidayProductProfile : BaseMappingProfile
    {
        public HolidayProductProfile()
        {
            CreateMap<HolidayProduct, HolidayProductDto>()
                .ForMember(dest => dest.ProductName,
                    opt => opt.MapFrom(src => src.Product != null ? src.Product.ProductName : null));
        }
    }
}
