using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed class ProductWarehouseBarcodePriceSlice
    : ProductWarehouseSliceBase,
      IProductWarehouseBarcodePriceSlice
{
    internal ProductWarehouseBarcodePriceSlice(ProductWarehouseSliceContext context)
        : base(context) { }

    /// <summary>
    /// 获取商品条码对应套装价/进货价列表（来自 ProductSetCode + StoreMultiCodeProduct）
    /// </summary>
    public async Task<List<BarcodePriceItemDto>> GetBarcodePricesAsync(string productCode)
    {
        if (string.IsNullOrWhiteSpace(productCode))
            return new List<BarcodePriceItemDto>();

        var setCodes = await _context
            .Db.Queryable<ProductSetCode>()
            .Where(psc => psc.ProductCode == productCode && !psc.IsDeleted)
            .Select(psc => new BarcodePriceItemDto
            {
                Barcode = psc.SetBarcode ?? "",
                RetailPrice = psc.SetRetailPrice,
                PurchasePrice = psc.SetPurchasePrice,
                SetCodeId = psc.SetCodeId,
            })
            .ToListAsync();
        var multiCodes = await _context
            .Db.Queryable<StoreMultiCodeProduct>()
            .Where(mcp => mcp.ProductCode == productCode && !mcp.IsDeleted)
            .Select(mcp => new BarcodePriceItemDto
            {
                Barcode = mcp.MultiBarcode ?? "",
                RetailPrice = mcp.MultiCodeRetailPrice,
                PurchasePrice = mcp.PurchasePrice,
                MultiCodeUuid = mcp.UUID,
            })
            .ToListAsync();
        var list = new List<BarcodePriceItemDto>();
        list.AddRange(setCodes.Where(x => !string.IsNullOrWhiteSpace(x.Barcode)));
        foreach (var m in multiCodes)
        {
            if (string.IsNullOrWhiteSpace(m.Barcode))
                continue;
            if (
                list.Any(x =>
                    x.Barcode == m.Barcode && !string.IsNullOrWhiteSpace(x.MultiCodeUuid)
                )
            )
                continue;
            list.Add(m);
        }
        return list;
    }
}
