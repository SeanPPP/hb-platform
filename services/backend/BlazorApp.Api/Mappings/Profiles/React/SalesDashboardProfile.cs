using AutoMapper;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Mappings.Profiles.React
{
    public class SalesDashboardProfile : Profile
    {
        public SalesDashboardProfile()
        {
            CreateMap<DailySalesStatistic, DashboardSummaryDto>();

            CreateMap<HourlySalesStatistic, HourlySalesDto>();

            CreateMap<StoreSalesStatistic, StoreSalesRankDto>();

            CreateMap<SupplierSalesStatistic, SupplierSalesRankDto>();

            CreateMap<SupplierSalesStatistic, ChinaSupplierSalesRankDto>();

            CreateMap<SalesOrderDetail, SalesProductDetailDto>();
        }
    }
}
