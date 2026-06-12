using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactStoreLocalSupplierInvoiceProfile : Profile
    {
        public ReactStoreLocalSupplierInvoiceProfile()
        {
            CreateMap<RED_进货单主表Store, StoreLocalSupplierInvoice>()
                .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.FGC_CreateDate))
                .ForMember(
                    dest => dest.UpdatedAt,
                    opt => opt.MapFrom(src => src.FGC_LastModifyDate)
                )
                .ForMember(
                    dest => dest.InvoiceGUID,
                    opt =>
                        opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? Guid.NewGuid().ToString("N"))
                )
                .ForMember(
                    dest => dest.AppGUID,
                    opt => opt.MapFrom(src => TrimLen(src.APPGUID, 50))
                )
                .ForMember(dest => dest.PcGUID, opt => opt.MapFrom(src => TrimLen(src.PCGUID, 50)))
                .ForMember(
                    dest => dest.StoreCode,
                    opt => opt.MapFrom(src => TrimLen(src.H分店代码, 50))
                )
                .ForMember(
                    dest => dest.SupplierCode,
                    opt => opt.MapFrom(src => TrimLen(src.H供应商编码, 50))
                )
                .ForMember(dest => dest.VoucherType, opt => opt.MapFrom(src => src.H单据类型))
                .ForMember(dest => dest.OrderDate, opt => opt.MapFrom(src => src.H订单日期))
                .ForMember(dest => dest.InvoiceNo, opt => opt.MapFrom(src => src.H随货同行单号))
                .ForMember(dest => dest.InboundDate, opt => opt.MapFrom(src => src.H入库日期))
                .ForMember(dest => dest.TotalAmount, opt => opt.MapFrom(src => src.H总金额))
                .ForMember(
                    dest => dest.ReceivedTotalAmount,
                    opt => opt.MapFrom(src => src.H收货总金额)
                )
                .ForMember(
                    dest => dest.Remarks,
                    opt => opt.MapFrom(src => TrimLen(src.H备注, 1000))
                )
                .ForMember(
                    dest => dest.ImportTemplate,
                    opt => opt.MapFrom(src => TrimLen(src.H导入模板, 100))
                )
                .ForMember(dest => dest.FlowStatus, opt => opt.MapFrom(src => src.H流程状态))
                .ForMember(dest => dest.InboundStatus, opt => opt.MapFrom(src => src.H入库状态));
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
