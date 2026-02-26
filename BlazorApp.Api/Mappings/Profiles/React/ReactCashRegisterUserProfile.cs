using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactCashRegisterUserProfile : Profile
    {
        public ReactCashRegisterUserProfile()
        {
            CreateMap<DIC_收银用户信息表, CashRegisterUser>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.ID))
                .ForMember(dest => dest.HGUID, opt => opt.MapFrom(src => src.HGUID ?? ""))
                .ForMember(dest => dest.StoreCode, opt => opt.MapFrom(src => src.分店代码 ?? ""))
                .ForMember(dest => dest.OperatorUser, opt => opt.MapFrom(src => src.操作用户 ?? ""))
                .ForMember(dest => dest.UserBarcode, opt => opt.MapFrom(src => src.用户条码 ?? ""))
                .ForMember(dest => dest.LoginRole, opt => opt.MapFrom(src => src.登陆角色 ?? ""))
                .ForMember(dest => dest.Remark, opt => opt.MapFrom(src => src.备注 ?? ""))
                .ForMember(dest => dest.PrintCount, opt => opt.MapFrom(src => src.条码打印次数))
                .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.状态))
                .ForMember(dest => dest.Creator, opt => opt.MapFrom(src => src.FGC_Creator ?? ""))
                .ForMember(dest => dest.CreateDate, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.LastModifier,
                    opt => opt.MapFrom(src => src.FGC_LastModifier ?? "")
                )
                .ForMember(
                    dest => dest.LastModifyDate,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                );
        }
    }
}
