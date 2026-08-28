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

internal sealed class ProductWarehouseSyncSlice
    : ProductWarehouseSliceBase,
      IProductWarehouseSyncSlice
{
    internal ProductWarehouseSyncSlice(ProductWarehouseSliceContext context)
        : base(context) { }

    /// <summary>
    /// 从 HQ 商品库存表同步到本地仓库商品表
    /// 这里统一委托给全量同步服务，避免 React 服务层保留旧的逐条增删改逻辑。
    /// </summary>
    /// <returns>同步结果</returns>
    public Task<SyncResult> SyncFromHqAsync() => SyncFromHqAsync(null, null);

    public async Task<SyncResult> SyncFromHqAsync(
        string? actorUserGuid,
        string? actorName
    )
    {
        _logger.LogInformation("[WarehouseProductSync] 开始委托全量同步仓库商品库存");
        return await _dataSyncFullService.SyncWarehouseProductsFromHqAsync(
            50000,
            10000,
            actorUserGuid,
            actorName
        );
    }
}
