using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactWarehouseCategoryProfile : Profile
    {
        public ReactWarehouseCategoryProfile()
        {
            CreateMap<CBP_DIC_商品分类码表, WarehouseCategory>()
                .ForMember(
                    dest => dest.CategoryGUID,
                    opt => opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? Guid.NewGuid().ToString())
                )
                .ForMember(
                    dest => dest.ParentGUID,
                    opt => opt.MapFrom(src => TrimLen(src.H父级GUID, 50))
                )
                .ForMember(
                    dest => dest.CategoryName,
                    opt => opt.MapFrom(src => TrimLen(src.H类别名称, 100) ?? string.Empty)
                )
                .ForMember(
                    dest => dest.ChineseName,
                    opt => opt.MapFrom(src => TrimLen(src.H中文名称, 100))
                )
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true));
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
