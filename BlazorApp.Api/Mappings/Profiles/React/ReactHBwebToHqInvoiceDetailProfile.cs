using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactHBwebToHqInvoiceDetailProfile : Profile
    {
        public ReactHBwebToHqInvoiceDetailProfile()
        {
            CreateMap<StoreLocalSupplierInvoiceDetails, RED_进货单详情表Store>()
                .ForMember(dest => dest.HGUID, opt => opt.MapFrom(src => src.DetailGUID))
                .ForMember(dest => dest.H主表GUID, opt => opt.MapFrom(src => src.InvoiceGUID))
                .ForMember(dest => dest.H分店代码, opt => opt.MapFrom(src => src.StoreCode))
                .ForMember(dest => dest.H商品标签GUID, opt => opt.MapFrom(src => src.ProductTagGUID))
                .ForMember(dest => dest.H商品分类码GUID, opt => opt.MapFrom(src => src.ProductCategoryGUID))
                .ForMember(dest => dest.H供应商编码, opt => opt.MapFrom(src => src.SupplierCode))
                .ForMember(dest => dest.H分店商品编码, opt => opt.MapFrom(src => src.StoreProductCode))
                .ForMember(dest => dest.H商品编码, opt => opt.MapFrom(src => src.ProductCode))
                .ForMember(dest => dest.H货号, opt => opt.MapFrom(src => src.ItemNumber))
                .ForMember(dest => dest.H主条形码, opt => opt.MapFrom(src => src.Barcode))
                .ForMember(dest => dest.H商品名称, opt => opt.MapFrom(src => src.ProductName))
                .ForMember(dest => dest.H规格, opt => opt.MapFrom(src => src.Specification))
                .ForMember(dest => dest.H单位, opt => opt.MapFrom(src => src.Unit))
                .ForMember(dest => dest.H数量, opt => opt.MapFrom(src => src.Quantity))
                .ForMember(dest => dest.H上次进货价, opt => opt.MapFrom(src => src.LastPurchasePrice))
                .ForMember(dest => dest.H进货价, opt => opt.MapFrom(src => src.PurchasePrice))
                .ForMember(dest => dest.H零售价, opt => opt.MapFrom(src => src.RetailPrice))
                .ForMember(dest => dest.H合计金额, opt => opt.MapFrom(src => src.Amount))
                .ForMember(dest => dest.H已存在商品数, opt => opt.MapFrom(src => src.ExistingProductCount))
                .ForMember(dest => dest.H商品图片, opt => opt.MapFrom(src => (string?)null))
                .ForMember(dest => dest.H活动类型, opt => opt.MapFrom(src => src.ActivityType))
                .ForMember(dest => dest.H折扣率, opt => opt.MapFrom(src => src.DiscountRate))
                .ForMember(dest => dest.H是否自动定价, opt => opt.MapFrom(src => src.AutoPricing))
                .ForMember(dest => dest.H定价浮率, opt => opt.MapFrom(src => src.PricingFloatRate))
                .ForMember(dest => dest.H新自动零售价, opt => opt.MapFrom(src => src.NewAutoRetailPrice))
                .ForMember(dest => dest.H是否特殊商品, opt => opt.MapFrom(src => src.IsSpecialProduct))
                .ForMember(dest => dest.H老库分店商品编码, opt => opt.MapFrom(src => src.OldStoreProductCode))
                .ForMember(dest => dest.FGC_Creator, opt => opt.MapFrom(src => src.CreatedBy))
                .ForMember(dest => dest.FGC_CreateDate, opt => opt.MapFrom(src => src.CreatedAt))
                .ForMember(dest => dest.FGC_LastModifier, opt => opt.MapFrom(src => src.UpdatedBy))
                .ForMember(dest => dest.FGC_LastModifyDate, opt => opt.MapFrom(src => src.UpdatedAt))
                .ForMember(dest => dest.FGC_UpdateHelp, opt => opt.MapFrom(src => (string?)null))
                .ForMember(dest => dest.ID, opt => opt.Ignore());
        }
    }
}
