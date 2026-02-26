using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactChinaSupplierProfile : Profile
    {
        public ReactChinaSupplierProfile()
        {
            CreateMap<CBP_DIC_国内供应商信息表, ChinaSupplier>()
                .ForMember(dest => dest.Guid, opt => opt.MapFrom(src => TrimLen(src.HGUID, 50)))
                .ForMember(
                    dest => dest.SupplierCode,
                    opt => opt.MapFrom(src => TrimLen(src.H供应商编码, 50))
                )
                .ForMember(
                    dest => dest.SupplierName,
                    opt => opt.MapFrom(src => TrimLen(src.H供应商名称, 100))
                )
                .ForMember(
                    dest => dest.ShopNumber,
                    opt => opt.MapFrom(src => TrimLen(src.H商铺编号, 50))
                )
                .ForMember(
                    dest => dest.ContactPerson,
                    opt => opt.MapFrom(src => TrimLen(src.H联系人, 50))
                )
                .ForMember(dest => dest.Phone, opt => opt.MapFrom(src => TrimLen(src.H电话, 50)))
                .ForMember(
                    dest => dest.Email,
                    opt => opt.MapFrom(src => TrimLen(src.HEMAIL地址, 100))
                )
                .ForMember(
                    dest => dest.StorefrontPhoto,
                    opt => opt.MapFrom(src => TrimLen(src.H商户门头照片, 500))
                )
                .ForMember(dest => dest.Remarks, opt => opt.MapFrom(src => TrimLen(src.备注, 500)))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态));
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
