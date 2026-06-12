using AutoMapper;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactProductGradeProfile : Profile
    {
        public ReactProductGradeProfile()
        {
            CreateMap<ProductGrade, BlazorApp.Shared.DTOs.ProductGradeDto>();
        }
    }
}
