using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/installment-orders")]
[Authorize]
public sealed class InstallmentOrdersController : ControllerBase
{
    private readonly IInstallmentOrderReactService _service;
    private readonly SqlSugarContext _dbContext;

    public InstallmentOrdersController(
        IInstallmentOrderReactService service,
        SqlSugarContext dbContext
    )
    {
        _service = service;
        _dbContext = dbContext;
    }

    [HttpPost("list")]
    [Authorize(Policy = Permissions.InstallmentOrders.View)]
    public async Task<IActionResult> List([FromBody] InstallmentOrderQueryParams queryParams)
    {
        if (!HasFullStoreAccess())
        {
            var assignedStoreCodes = await GetCurrentUserStoreCodesAsync();
            var scopedStoreCodes = ApplyRequestedStoreScope(queryParams, assignedStoreCodes);
            if (scopedStoreCodes.Count == 0)
                return EmptyList(queryParams);

            queryParams.StoreCodes = scopedStoreCodes;
        }

        var result = await _service.GetOrderListAsync(queryParams);
        return Ok(new { success = true, data = result, message = "查询成功" });
    }

    [HttpGet("detail/{installmentGuid}")]
    [Authorize(Policy = Permissions.InstallmentOrders.View)]
    public async Task<IActionResult> Detail(string installmentGuid)
    {
        IReadOnlyCollection<string>? allowedStoreCodes = null;
        if (!HasFullStoreAccess())
            allowedStoreCodes = await GetCurrentUserStoreCodesAsync();

        // 关键逻辑：门店范围交给服务参与主单查询，越权与不存在统一返回 404。
        var result = await _service.GetOrderDetailAsync(installmentGuid, allowedStoreCodes);
        if (!result.Success || result.Data?.Order == null)
            return NotFound(new { success = false, data = (object?)null, message = result.Message });

        return Ok(new { success = true, data = result.Data, message = result.Message });
    }

    private bool HasFullStoreAccess() =>
        User.FindAll(ClaimTypes.Role).Any(claim =>
            Permissions.IsSuperAdminRole(claim.Value)
            || claim.Value.Equals("WarehouseManager", StringComparison.OrdinalIgnoreCase)
            || claim.Value.Equals("仓库经理", StringComparison.OrdinalIgnoreCase)
        );

    private string GetCurrentUserGuid() =>
        User.FindFirst("userId")?.Value
        ?? User.FindFirst("userGuid")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? string.Empty;

    private async Task<List<string>> GetCurrentUserStoreCodesAsync()
    {
        var userGuid = GetCurrentUserGuid();
        if (string.IsNullOrWhiteSpace(userGuid))
            return [];

        var storeGuids = await _dbContext.Db.Queryable<UserStore>()
            .Where(relation => relation.UserGUID == userGuid && !relation.IsDeleted)
            .Select(relation => relation.StoreGUID)
            .ToListAsync();
        if (storeGuids.Count == 0)
            return [];

        var codes = await _dbContext.Db.Queryable<Store>()
            .Where(store => !store.IsDeleted && storeGuids.Contains(store.StoreGUID))
            .Select(store => store.StoreCode)
            .ToListAsync();
        return codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ApplyRequestedStoreScope(
        InstallmentOrderQueryParams queryParams,
        IReadOnlyCollection<string> assignedStoreCodes
    )
    {
        var scoped = assignedStoreCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(queryParams.BranchCode))
            scoped.IntersectWith([queryParams.BranchCode.Trim()]);

        if (queryParams.StoreCodes is { Count: > 0 })
        {
            scoped.IntersectWith(
                queryParams.StoreCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code.Trim())
            );
        }

        return scoped.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private IActionResult EmptyList(InstallmentOrderQueryParams queryParams) =>
        Ok(new
        {
            success = true,
            data = new PagedListReactDto<InstallmentOrderSummaryDto>
            {
                Items = [],
                Total = 0,
                PageNumber = Math.Max(1, queryParams.PageNumber),
                PageSize = queryParams.PageSize <= 0 ? 20 : Math.Min(queryParams.PageSize, 1000),
            },
            message = "查询成功",
        });
}
