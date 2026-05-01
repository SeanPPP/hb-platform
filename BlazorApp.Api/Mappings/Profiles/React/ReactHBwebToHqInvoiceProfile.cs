using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactHBwebToHqInvoiceProfile : Profile
    {
        public ReactHBwebToHqInvoiceProfile()
        {
            CreateMap<StoreLocalSupplierInvoice, RED_进货单主表Store>()
                .ForMember(dest => dest.HGUID, opt => opt.MapFrom(src => src.InvoiceGUID))
                .ForMember(dest => dest.APPGUID, opt => opt.MapFrom(src => src.AppGUID))
                .ForMember(dest => dest.PCGUID, opt => opt.MapFrom(src => src.PcGUID))
                .ForMember(dest => dest.H分店代码, opt => opt.MapFrom(src => src.StoreCode))
                .ForMember(dest => dest.H供应商编码, opt => opt.MapFrom(src => src.SupplierCode))
                .ForMember(dest => dest.H随货同行单号, opt => opt.MapFrom(src => src.InvoiceNo))
                .ForMember(dest => dest.H单据类型, opt => opt.MapFrom(src => src.VoucherType))
                .ForMember(dest => dest.H订单日期, opt => opt.MapFrom(src => src.OrderDate))
                .ForMember(dest => dest.H入库日期, opt => opt.MapFrom(src => src.InboundDate))
                .ForMember(dest => dest.H总金额, opt => opt.MapFrom(src => src.TotalAmount))
                .ForMember(dest => dest.H收货总金额, opt => opt.MapFrom(src => src.ReceivedTotalAmount))
                .ForMember(dest => dest.H单据图片, opt => opt.MapFrom(src => src.VoucherImage))
                .ForMember(dest => dest.H备注, opt => opt.MapFrom(src => src.Remarks))
                .ForMember(dest => dest.H导入模板, opt => opt.MapFrom(src => src.ImportTemplate))
                .ForMember(dest => dest.H流程状态, opt => opt.MapFrom(src => src.FlowStatus))
                .ForMember(dest => dest.H入库状态, opt => opt.MapFrom(src => src.InboundStatus))
                .ForMember(dest => dest.FGC_Creator, opt => opt.MapFrom(src => src.CreatedBy))
                .ForMember(dest => dest.FGC_CreateDate, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.FGC_LastModifier, opt => opt.MapFrom(src => src.UpdatedBy))
                .ForMember(dest => dest.FGC_LastModifyDate, opt => opt.MapFrom(src => src.UpdatedAt))
                .ForMember(dest => dest.FGC_UpdateHelp, opt => opt.MapFrom(src => (string?)null))
                .ForMember(dest => dest.ID, opt => opt.Ignore());
        }
    }
}
