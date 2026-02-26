using AutoMapper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class ReactStoreLocalSupplierInvoiceDetailProfile : Profile
    {
        public ReactStoreLocalSupplierInvoiceDetailProfile()
        {
            CreateMap<RED_进货单详情表Store, StoreLocalSupplierInvoiceDetails>()
                .ForMember(
                    dest => dest.DetailGUID,
                    opt =>
                        opt.MapFrom(src => TrimLen(src.HGUID, 50) ?? Guid.NewGuid().ToString("N"))
                )
                .ForMember(
                    dest => dest.InvoiceGUID,
                    opt => opt.MapFrom(src => TrimLen(src.H主表GUID, 50))
                )
                .ForMember(
                    dest => dest.StoreCode,
                    opt => opt.MapFrom(src => TrimLen(src.H分店代码, 50))
                )
                .ForMember(
                    dest => dest.SupplierCode,
                    opt => opt.MapFrom(src => TrimLen(src.H供应商编码, 50))
                )
                .ForMember(
                    dest => dest.ProductTagGUID,
                    opt => opt.MapFrom(src => TrimLen(src.H商品标签GUID, 50))
                )
                .ForMember(
                    dest => dest.ProductCategoryGUID,
                    opt => opt.MapFrom(src => TrimLen(src.H商品分类码GUID, 50))
                )
                .ForMember(
                    dest => dest.StoreProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.H分店商品编码, 50))
                )
                .ForMember(
                    dest => dest.ProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.H商品编码, 50))
                )
                .ForMember(
                    dest => dest.ItemNumber,
                    opt => opt.MapFrom(src => TrimLen(src.H货号, 50))
                )
                .ForMember(
                    dest => dest.Barcode,
                    opt => opt.MapFrom(src => TrimLen(src.H主条形码, 50))
                )
                .ForMember(
                    dest => dest.ProductName,
                    opt => opt.MapFrom(src => TrimLen(src.H商品名称, 200))
                )
                .ForMember(
                    dest => dest.Specification,
                    opt => opt.MapFrom(src => TrimLen(src.H规格, 100))
                )
                .ForMember(dest => dest.Unit, opt => opt.MapFrom(src => TrimLen(src.H单位, 20)))
                .ForMember(dest => dest.Quantity, opt => opt.MapFrom(src => src.H数量))
                .ForMember(
                    dest => dest.LastPurchasePrice,
                    opt => opt.MapFrom(src => src.H上次进货价)
                )
                .ForMember(dest => dest.PurchasePrice, opt => opt.MapFrom(src => src.H进货价))
                .ForMember(dest => dest.RetailPrice, opt => opt.MapFrom(src => src.H零售价))
                .ForMember(dest => dest.Amount, opt => opt.MapFrom(src => src.H合计金额))
                .ForMember(
                    dest => dest.ExistingProductCount,
                    opt => opt.MapFrom(src => src.H已存在商品数)
                )
                .ForMember(dest => dest.ProductImage, opt => opt.Ignore())
                .ForMember(dest => dest.ActivityType, opt => opt.MapFrom(src => src.H活动类型))
                .ForMember(dest => dest.DiscountRate, opt => opt.MapFrom(src => src.H折扣率))
                .ForMember(dest => dest.AutoPricing, opt => opt.MapFrom(src => src.H是否自动定价))
                .ForMember(dest => dest.PricingFloatRate, opt => opt.MapFrom(src => src.H定价浮率))
                .ForMember(
                    dest => dest.NewAutoRetailPrice,
                    opt => opt.MapFrom(src => src.H新自动零售价)
                )
                .ForMember(
                    dest => dest.IsSpecialProduct,
                    opt => opt.MapFrom(src => src.H是否特殊商品)
                )
                .ForMember(
                    dest => dest.OldStoreProductCode,
                    opt => opt.MapFrom(src => TrimLen(src.H老库分店商品编码, 50))
                );
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
