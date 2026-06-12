using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using HqEntities = BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactProductCategoryProfile : Profile
    {
        public ReactProductCategoryProfile()
        {
            CreateMap<HqEntities::DIC_商品分类码表, ProductCategory>()
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
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => true))
                .ForMember(dest => dest.SortOrder, opt => opt.MapFrom(src => (int?)null));

            CreateMap<ProductCategory, ProductCategoryDto>()
                .ForMember(dest => dest.Children, opt => opt.MapFrom(src => src.Children));

            CreateMap<ProductCategoryDto, ProductCategory>()
                .ForMember(dest => dest.Children, opt => opt.MapFrom(src => src.Children));

            CreateMap<CreateProductCategoryDto, ProductCategory>()
                .ForMember(
                    dest => dest.CategoryGUID,
                    opt => opt.MapFrom(src => Guid.NewGuid().ToString())
                )
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.SortOrder, opt => opt.MapFrom(src => src.SortOrder));

            CreateMap<UpdateProductCategoryDto, ProductCategory>()
                .ForMember(dest => dest.CategoryGUID, opt => opt.MapFrom(src => src.CategoryGUID))
                .ForMember(dest => dest.ParentGUID, opt => opt.MapFrom(src => src.ParentGUID))
                .ForMember(dest => dest.CategoryName, opt => opt.MapFrom(src => src.CategoryName))
                .ForMember(dest => dest.IsActive, opt => opt.MapFrom(src => src.IsActive))
                .ForMember(dest => dest.SortOrder, opt => opt.MapFrom(src => src.SortOrder));
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
