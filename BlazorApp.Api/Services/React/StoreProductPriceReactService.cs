using System;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class StoreProductPriceReactService : IStoreProductPriceReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<StoreProductPriceReactService> _logger;

        public StoreProductPriceReactService(
            SqlSugarContext context,
            ILogger<StoreProductPriceReactService> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        public async Task<GridResponseDto<StoreProductPriceListDto>> GetGridDataAsync(
            StoreProductPriceQueryDto query
        )
        {
            try
            {
                var db = _context.Db;
                var pageIndex = query.PageNumber;
                var pageSize = query.PageSize;

                var baseQuery = db.Queryable<Product>()
                    .With(SqlWith.NoLock)
                    .Where(p => p.IsDeleted == false);

                if (!string.IsNullOrWhiteSpace(query.LocalSupplierCode))
                {
                    baseQuery = baseQuery.Where(p => p.LocalSupplierCode == query.LocalSupplierCode);
                }

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    var keyword = query.Search.Trim();
                    baseQuery = baseQuery.Where(p =>
                        p.ProductName.Contains(keyword) ||
                        (p.ProductCode != null && p.ProductCode.Contains(keyword)) ||
                        (p.ItemNumber != null && p.ItemNumber.Contains(keyword)) ||
                        (p.Barcode != null && p.Barcode.Contains(keyword))
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.ProductName))
                {
                    baseQuery = baseQuery.Where(p => p.ProductName.Contains(query.ProductName));
                }

                if (!string.IsNullOrWhiteSpace(query.ProductCode))
                {
                    baseQuery = baseQuery.Where(p => p.ProductCode.Contains(query.ProductCode));
                }

                if (!string.IsNullOrWhiteSpace(query.ItemNumber))
                {
                    baseQuery = baseQuery.Where(p => p.ItemNumber.Contains(query.ItemNumber));
                }

                if (!string.IsNullOrWhiteSpace(query.Barcode))
                {
                    baseQuery = baseQuery.Where(p => p.Barcode.Contains(query.Barcode));
                }

                if (query.ProductType.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.ProductType == query.ProductType.Value);
                }

                if (query.IsActive.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.IsActive == query.IsActive.Value);
                }

                if (query.IsSpecialProduct.HasValue)
                {
                    baseQuery = baseQuery.Where(p => p.IsSpecialProduct == query.IsSpecialProduct.Value);
                }

                var joinQuery = baseQuery
                    .LeftJoin<StoreRetailPrice>(
                        (p, srp) => p.ProductCode == srp.ProductCode && 
                                     srp.StoreCode == query.StoreCode && 
                                     srp.IsDeleted == false
                    )
                    .LeftJoin<HBLocalSupplier>(
                        (p, srp, sup) => p.LocalSupplierCode == sup.LocalSupplierCode && sup.IsDeleted == false
                    );

                if (query.PurchasePriceGt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.PurchasePrice >= query.PurchasePriceGt.Value);
                }

                if (query.PurchasePriceLt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.PurchasePrice <= query.PurchasePriceLt.Value);
                }

                if (query.RetailPriceGt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.StoreRetailPriceValue >= query.RetailPriceGt.Value);
                }

                if (query.RetailPriceLt.HasValue)
                {
                    joinQuery = joinQuery.Where((p, srp, sup) => srp.StoreRetailPriceValue <= query.RetailPriceLt.Value);
                }

                if (!string.IsNullOrWhiteSpace(query.SortBy))
                {
                    var asc = query.SortOrder?.ToLower() == "asc";
                    joinQuery = query.SortBy.ToLower() switch
                    {
                        "productname" => joinQuery.OrderBy((p, srp, sup) => p.ProductName, asc ? OrderByType.Asc : OrderByType.Desc),
                        "productcode" => joinQuery.OrderBy((p, srp, sup) => p.ProductCode, asc ? OrderByType.Asc : OrderByType.Desc),
                        "itemnumber" => joinQuery.OrderBy((p, srp, sup) => p.ItemNumber, asc ? OrderByType.Asc : OrderByType.Desc),
                        "barcode" => joinQuery.OrderBy((p, srp, sup) => p.Barcode, asc ? OrderByType.Asc : OrderByType.Desc),
                        "middlesackagequantity" => joinQuery.OrderBy((p, srp, sup) => p.MiddlePackageQuantity, asc ? OrderByType.Asc : OrderByType.Desc),
                        "purchaseprice" => joinQuery.OrderBy((p, srp, sup) => srp.PurchasePrice, asc ? OrderByType.Asc : OrderByType.Desc),
                        "retailprice" => joinQuery.OrderBy((p, srp, sup) => srp.StoreRetailPriceValue, asc ? OrderByType.Asc : OrderByType.Desc),
                        "discountrate" => joinQuery.OrderBy((p, srp, sup) => srp.DiscountRate, asc ? OrderByType.Asc : OrderByType.Desc),
                        "updatedat" => joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, asc ? OrderByType.Asc : OrderByType.Desc),
                        _ => joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, OrderByType.Desc)
                    };
                }
                else
                {
                    joinQuery = joinQuery.OrderBy((p, srp, sup) => p.UpdatedAt, OrderByType.Desc);
                }

                var totalRef = new RefAsync<int>(0);
                var items = await joinQuery
                    .Select(
                        (p, srp, sup) => new StoreProductPriceListDto
                        {
                            ProductCode = p.ProductCode,
                            ProductName = p.ProductName,
                            ProductImage = p.ProductImage,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            LocalSupplierCode = p.LocalSupplierCode,
                            LocalSupplierName = sup.Name,
                            ProductType = p.ProductType,
                            MiddlePackageQuantity = p.MiddlePackageQuantity,
                            IsActive = p.IsActive,
                            UpdatedAt = p.UpdatedAt,
                            UpdatedBy = p.UpdatedBy,
                            StoreCode = srp.StoreCode,
                            StorePurchasePrice = srp.PurchasePrice,
                            StoreRetailPrice = srp.StoreRetailPriceValue,
                            IsStoreAutoPricing = srp.IsAutoPricing,
                            IsStoreSpecialProduct = srp.IsSpecialProduct,
                            DiscountRate = srp.DiscountRate
                        }
                    )
                    .ToPageListAsync(pageIndex, pageSize, totalRef);

                return GridResponseDto<StoreProductPriceListDto>.OK(items, totalRef.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StoreProductPrice Grid 查询失败");
                return GridResponseDto<StoreProductPriceListDto>.Error("查询失败");
            }
        }
    }
}
