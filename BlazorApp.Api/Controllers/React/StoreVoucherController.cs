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
[Route("api/react/v1/store-vouchers")]
[Authorize]
public class StoreVoucherController : ControllerBase
{
    private readonly IStoreVoucherReactService _service;
    private readonly SqlSugarContext _dbContext;

    public StoreVoucherController(IStoreVoucherReactService service, SqlSugarContext dbContext)
    {
        _service = service;
        _dbContext = dbContext;
    }

    [HttpPost("list")]
    [Authorize(Policy = Permissions.StoreVouchers.View)]
    public async Task<IActionResult> List([FromBody] StoreVoucherQueryParams queryParams)
    {
        if (!IsFullStoreAccessUser())
        {
            var userStoreCodes = await GetCurrentUserStoreCodesAsync();
            if (userStoreCodes.Count > 0)
            {
                if (
                    !string.IsNullOrWhiteSpace(queryParams.StoreCode)
                    && !userStoreCodes.Contains(queryParams.StoreCode)
                )
                {
                    return Ok(new
                    {
                        success = true,
                        data = new PagedListReactDto<StoreVoucherDto>
                        {
                            Items = new List<StoreVoucherDto>(),
                            Total = 0,
                            PageNumber = queryParams.PageNumber,
                            PageSize = queryParams.PageSize,
                        },
                    });
                }

                queryParams.StoreCodes = userStoreCodes;
            }
        }

        var result = await _service.GetVoucherListAsync(queryParams);
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{idOrCode}")]
    [Authorize(Policy = Permissions.StoreVouchers.View)]
    public async Task<IActionResult> Detail(string idOrCode)
    {
        var result = await _service.GetVoucherDetailAsync(idOrCode);
        if (!result.Success || result.Data?.Voucher == null)
            return NotFound(new { success = false, message = result.Message });

        if (
            !IsFullStoreAccessUser()
            && !await CanAccessStoreAsync(result.Data.Voucher.StoreCode)
        )
        {
            return Forbid();
        }

        return Ok(new { success = true, data = result.Data });
    }

    private bool IsFullStoreAccessUser() =>
        User.Claims.Any(c =>
            c.Type == ClaimTypes.Role
            && (
                c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || c.Value.Equals("WarehouseManager", StringComparison.OrdinalIgnoreCase)
            )
        );

    private string GetCurrentUserGuid() =>
        User.FindFirst("userId")?.Value
        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? string.Empty;

    private async Task<List<string>> GetCurrentUserStoreCodesAsync()
    {
        var userGuid = GetCurrentUserGuid();
        if (string.IsNullOrWhiteSpace(userGuid))
            return new List<string>();

        var storeGuids = await _dbContext.Db.Queryable<UserStore>()
            .Where(us => us.UserGUID == userGuid)
            .Select(us => us.StoreGUID)
            .ToListAsync();
        if (storeGuids.Count == 0)
            return new List<string>();

        return await _dbContext.Db.Queryable<Store>()
            .Where(store => storeGuids.Contains(store.StoreGUID))
            .Select(store => store.StoreCode)
            .ToListAsync();
    }

    private async Task<bool> CanAccessStoreAsync(string? storeCode)
    {
        if (string.IsNullOrWhiteSpace(storeCode))
            return false;

        var storeCodes = await GetCurrentUserStoreCodesAsync();
        return storeCodes.Contains(storeCode);
    }
}
