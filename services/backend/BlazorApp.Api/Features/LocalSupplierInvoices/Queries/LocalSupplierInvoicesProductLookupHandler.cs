using System.Data;
using System.Linq;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BlazorApp.Api.Features.LocalSupplierInvoices
{
    internal sealed class LocalSupplierInvoicesProductLookupHandler
    {
        private readonly LocalSupplierInvoicesDependencies _dependencies;
        private SqlSugarContext _context => _dependencies.Context;
        private HqSqlSugarContext _hqContext => _dependencies.HqContext;
        private IMapper _mapper => _dependencies.Mapper;
        private ILogger _logger => _dependencies.Logger;
        private IAutoPricingService _autoPricingService => _dependencies.AutoPricingService;
        private IWarehouseProductChangeHistoryService _changeHistoryService => _dependencies.ChangeHistoryService;
        private ILocalSupplierInvoiceHqProductSyncService? _hqProductSyncService => _dependencies.HqProductSyncService;

        public LocalSupplierInvoicesProductLookupHandler(LocalSupplierInvoicesDependencies dependencies)
        {
            _dependencies = dependencies;
        }

        public async Task<ApiResponse<GetProductsByBarcodeResponse>> GetProductsByBarcodeAsync(
            string invoiceGuid,
            string barcode
        )
        {
            try
            {
                var db = _context.Db;

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<GetProductsByBarcodeResponse>.Error(
                        "订单不存在",
                        "NOT_FOUND"
                    );

                if (string.IsNullOrWhiteSpace(barcode))
                    return ApiResponse<GetProductsByBarcodeResponse>.Error(
                        "条码不能为空",
                        "VALIDATION_ERROR"
                    );

                var trimmedBarcode = barcode.Trim();
                var matchedProductCodes = new HashSet<string>();
                var productDetails = new Dictionary<string, Product>();

                var prods = await db.Queryable<Product>()
                    .Where(p =>
                        p.IsDeleted == false && p.Barcode != null && p.Barcode == trimmedBarcode
                    )
                    .ToListAsync();

                foreach (var p in prods)
                {
                    if (!string.IsNullOrWhiteSpace(p.ProductCode))
                    {
                        matchedProductCodes.Add(p.ProductCode);
                        if (!productDetails.ContainsKey(p.ProductCode))
                            productDetails[p.ProductCode] = p;
                    }
                }

                var multiCodes = await db.Queryable<StoreMultiCodeProduct>()
                    .Where(x =>
                        x.StoreCode == header.StoreCode
                        && x.MultiBarcode != null
                        && x.MultiBarcode == trimmedBarcode
                        && x.IsDeleted == false
                    )
                    .ToListAsync();

                foreach (var mc in multiCodes)
                {
                    if (!string.IsNullOrWhiteSpace(mc.ProductCode))
                    {
                        matchedProductCodes.Add(mc.ProductCode);

                        if (!productDetails.ContainsKey(mc.ProductCode))
                        {
                            var product = await db.Queryable<Product>()
                                .Where(p => p.ProductCode == mc.ProductCode && p.IsDeleted == false)
                                .FirstAsync();
                            if (product != null)
                                productDetails[mc.ProductCode] = product;
                        }
                    }
                }

                if (matchedProductCodes.Count > 0 && productDetails.Count == 0)
                {
                    var allProducts = await db.Queryable<Product>()
                        .Where(p =>
                            p.ProductCode != null
                            && matchedProductCodes.Contains(p.ProductCode)
                            && p.IsDeleted == false
                        )
                        .ToListAsync();

                    foreach (var p in allProducts)
                    {
                        if (!string.IsNullOrWhiteSpace(p.ProductCode))
                        {
                            productDetails[p.ProductCode] = p;
                        }
                    }
                }

                var supplierCodes = productDetails
                    .Values.Select(p => p.LocalSupplierCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var suppliers = new Dictionary<string, string>();
                if (supplierCodes.Count > 0)
                {
                    var supplierList = await db.Queryable<HBLocalSupplier>()
                        .Where(x =>
                            supplierCodes.Contains(x.LocalSupplierCode) && x.IsDeleted == false
                        )
                        .Select(x => new { x.LocalSupplierCode, x.Name })
                        .ToListAsync();
                    foreach (var s in supplierList)
                        suppliers[s.LocalSupplierCode] = s.Name;
                }

                var result = new GetProductsByBarcodeResponse
                {
                    Barcode = trimmedBarcode,
                    MatchedProducts = productDetails
                        .Values.Select(p => new BarcodeAbnormalMatchedProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ProductName = p.ProductName ?? string.Empty,
                            SupplierCode = p.LocalSupplierCode ?? string.Empty,
                            SupplierName = suppliers.GetValueOrDefault(
                                p.LocalSupplierCode ?? string.Empty
                            ),
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode ?? string.Empty,
                            ProductImage = p.ProductImage,
                            IsMultiCode = multiCodes.Any(mc => mc.ProductCode == p.ProductCode),
                            IsBundle = false,
                        })
                        .ToList(),
                };

                return ApiResponse<GetProductsByBarcodeResponse>.OK(result, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按条码查询匹配商品失败");
                return ApiResponse<GetProductsByBarcodeResponse>.Error("获取失败", "GET_ERROR");
            }
        }

        public async Task<
            ApiResponse<GetProductsByProductCodeResponse>
        > GetProductsByProductCodeAsync(string invoiceGuid, string productCode)
        {
            try
            {
                var db = _context.Db;

                var header = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x => x.InvoiceGUID == invoiceGuid && x.IsDeleted == false)
                    .FirstAsync();

                if (header == null)
                    return ApiResponse<GetProductsByProductCodeResponse>.Error(
                        "订单不存在",
                        "NOT_FOUND"
                    );

                if (string.IsNullOrWhiteSpace(productCode))
                    return ApiResponse<GetProductsByProductCodeResponse>.Error(
                        "商品编码不能为空",
                        "VALIDATION_ERROR"
                    );

                var trimmedProductCode = productCode.Trim();
                var productDetails = new Dictionary<string, Product>();

                var prods = await db.Queryable<Product>()
                    .Where(p =>
                        p.IsDeleted == false
                        && p.ProductCode != null
                        && p.ProductCode == trimmedProductCode
                    )
                    .ToListAsync();

                foreach (var p in prods)
                {
                    if (!string.IsNullOrWhiteSpace(p.ProductCode))
                    {
                        if (!productDetails.ContainsKey(p.ProductCode))
                            productDetails[p.ProductCode] = p;
                    }
                }

                var supplierCodes = productDetails
                    .Values.Select(p => p.LocalSupplierCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();

                var suppliers = new Dictionary<string, string>();
                if (supplierCodes.Count > 0)
                {
                    var supplierList = await db.Queryable<HBLocalSupplier>()
                        .Where(x =>
                            supplierCodes.Contains(x.LocalSupplierCode) && x.IsDeleted == false
                        )
                        .Select(x => new { x.LocalSupplierCode, x.Name })
                        .ToListAsync();
                    foreach (var s in supplierList)
                        suppliers[s.LocalSupplierCode] = s.Name;
                }

                var result = new GetProductsByProductCodeResponse
                {
                    ProductCode = trimmedProductCode,
                    MatchedProducts = productDetails
                        .Values.Select(p => new BarcodeAbnormalMatchedProductDto
                        {
                            ProductCode = p.ProductCode ?? string.Empty,
                            ProductName = p.ProductName ?? string.Empty,
                            SupplierCode = p.LocalSupplierCode ?? string.Empty,
                            SupplierName = suppliers.GetValueOrDefault(
                                p.LocalSupplierCode ?? string.Empty
                            ),
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode ?? string.Empty,
                            ProductImage = p.ProductImage,
                            IsMultiCode = false,
                            IsBundle = false,
                            ProductType = p.ProductType,
                        })
                        .ToList(),
                };

                return ApiResponse<GetProductsByProductCodeResponse>.OK(result, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按商品编码查询匹配商品失败");
                return ApiResponse<GetProductsByProductCodeResponse>.Error("获取失败", "GET_ERROR");
            }
        }

        public async Task<ApiResponse<InvoiceNoCheckResult>> CheckInvoiceNoExistsAsync(
            string storeCode,
            string supplierCode,
            string invoiceNo
        )
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(storeCode)
                    || string.IsNullOrWhiteSpace(supplierCode)
                    || string.IsNullOrWhiteSpace(invoiceNo)
                )
                    return ApiResponse<InvoiceNoCheckResult>.OK(
                        new InvoiceNoCheckResult { Exists = false }
                    );

                var db = _context.Db;
                var existing = await db.Queryable<StoreLocalSupplierInvoice>()
                    .Where(x =>
                        x.StoreCode == storeCode.Trim()
                        && x.SupplierCode == supplierCode
                        && x.InvoiceNo == invoiceNo.Trim()
                        && x.IsDeleted == false
                    )
                    .Select(x => new { x.InvoiceNo, x.CreatedAt })
                    .FirstAsync();

                if (existing != null)
                {
                    return ApiResponse<InvoiceNoCheckResult>.OK(
                        new InvoiceNoCheckResult
                        {
                            Exists = true,
                            ExistingInvoiceNo = existing.InvoiceNo,
                            ExistingCreatedAt = existing.CreatedAt,
                        }
                    );
                }

                return ApiResponse<InvoiceNoCheckResult>.OK(
                    new InvoiceNoCheckResult { Exists = false }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查随货单号是否存在失败");
                return ApiResponse<InvoiceNoCheckResult>.Error("检查失败", "CHECK_ERROR");
            }
        }

    }
}
