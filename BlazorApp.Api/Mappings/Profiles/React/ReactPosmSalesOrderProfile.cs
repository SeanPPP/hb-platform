using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactPosmSalesOrderProfile : Profile
    {
        public ReactPosmSalesOrderProfile()
        {
            CreateMap<SalesOrder, PosmSalesOrderDto>()
                .ForMember(dest => dest.OrderGuid, opt => opt.MapFrom(src => src.OrderGuid))
                .ForMember(dest => dest.BranchCode, opt => opt.MapFrom(src => src.BranchCode))
                .ForMember(dest => dest.BranchName, opt => opt.MapFrom(src => src.BranchCode))
                .ForMember(dest => dest.DeviceCode, opt => opt.MapFrom(src => src.DeviceCode))
                .ForMember(dest => dest.OrderTime, opt => opt.MapFrom(src => src.OrderTime))
                .ForMember(dest => dest.SkuCount, opt => opt.MapFrom(src => 0))
                .ForMember(dest => dest.ItemCount, opt => opt.MapFrom(src => src.ItemCount))
                .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => src.TotalAmount))
                .ForMember(
                    dest => dest.DiscountAmount,
                    opt => opt.MapFrom(src => src.DiscountAmount)
                )
                .ForMember(dest => dest.ActualAmount, opt => opt.MapFrom(src => src.ActualAmount));

            CreateMap<SalesOrderDetail, PosmSalesOrderDetailDto>()
                .ForMember(dest => dest.ProductImage, opt => opt.MapFrom(src => (string?)null))
                .ForMember(dest => dest.ProductCode, opt => opt.MapFrom(src => src.ProductCode))
                .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.ProductName))
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.Quantity))
                .ForMember(dest => dest.UnitPrice, opt => opt.MapFrom(src => src.Price))
                .ForMember(
                    dest => dest.DiscountAmount,
                    opt => opt.MapFrom(src => src.DiscountAmount)
                )
                .ForMember(dest => dest.ActualAmount, opt => opt.MapFrom(src => src.ActualAmount));

            CreateMap<PaymentDetail, PosmPaymentDetailDto>()
                .ForMember(dest => dest.PaymentTime, opt => opt.MapFrom(src => src.CreatedTime))
                .ForMember(dest => dest.PaymentMethod, opt => opt.MapFrom(src => src.PaymentMethod))
                .ForMember(
                    dest => dest.PaymentMethodName,
                    opt => opt.MapFrom(src => GetPaymentMethodName(src.PaymentMethod))
                )
                .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.Amount));
        }

        private static string GetPaymentMethodName(int? paymentMethod)
        {
            return paymentMethod switch
            {
                1 => "现金",
                2 => "刷卡",
                3 => "代金券",
                _ => "未知",
            };
        }
    }
}
