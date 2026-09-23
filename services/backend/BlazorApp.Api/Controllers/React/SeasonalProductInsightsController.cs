using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BlazorApp.Api.Controllers.React;

/// <summary>移动端「季节商品查询」：独立权限码，进货与销量按各自区间统计。</summary>
[ApiController]
[Route("api/react/v1/seasonal-product-insights")]
public sealed class SeasonalProductInsightsController(
    SeasonalProductInsightQueryService queryService,
    IRoleService roleService,
    SqlSugarContext context,
    ILogger<SeasonalProductInsightsController> logger
) : ControllerBase
{
    private const int MaxKeywordLength = 100;
    private readonly ISqlSugarClient _db = context.Db;

    [HttpGet("lookup")]
    [Authorize(Policy = Permissions.SeasonalProductInsights.View)]
    public async Task<IActionResult> Lookup(
        [FromQuery] string? storeCode,
        [FromQuery] string? keyword,
        [FromQuery] string? inboundStartDate,
        [FromQuery] string? inboundEndDate,
        [FromQuery] string? salesStartDate,
        [FromQuery] string? salesEndDate,
        CancellationToken cancellationToken
    )
    {
        var normalizedKeyword = keyword?.Trim() ?? string.Empty;
        if (normalizedKeyword.Length == 0 || normalizedKeyword.Length > MaxKeywordLength)
        {
            return BadRequest(new { success = false, message = $"请输入 1–{MaxKeywordLength} 个字符的货号或条码" });
        }

        var scope = await ResolveScopeAsync(storeCode, inboundStartDate, inboundEndDate, salesStartDate, salesEndDate, cancellationToken);
        if (scope.Failure != null)
        {
            return scope.Failure;
        }

        try
        {
            var result = await queryService.LookupAsync(scope.StoreCode, normalizedKeyword, scope.Inbound, scope.Sales, cancellationToken);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "季节商品检索失败 StoreCode={StoreCode} Keyword={Keyword}", scope.StoreCode, normalizedKeyword);
            return StatusCode(500, new { success = false, message = "商品检索暂时不可用" });
        }
    }

    [HttpGet("store")]
    [Authorize(Policy = Permissions.SeasonalProductInsights.View)]
    public async Task<IActionResult> GetStoreInsight(
        [FromQuery] string? storeCode,
        [FromQuery] string? productCode,
        [FromQuery] string? inboundStartDate,
        [FromQuery] string? inboundEndDate,
        [FromQuery] string? salesStartDate,
        [FromQuery] string? salesEndDate,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(productCode))
        {
            return BadRequest(new { success = false, message = "productCode 不能为空" });
        }

        var scope = await ResolveScopeAsync(storeCode, inboundStartDate, inboundEndDate, salesStartDate, salesEndDate, cancellationToken);
        if (scope.Failure != null)
        {
            return scope.Failure;
        }

        try
        {
            var result = await queryService.GetAsync(scope.StoreCode, productCode.Trim(), scope.Inbound, scope.Sales, cancellationToken);
            return result == null
                ? NotFound(new { success = false, message = "商品不存在，或分店不存在/未启用" })
                : Ok(new { success = true, data = result });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 查询异常必须返回失败，不能把数据源故障伪装成零销量或无进货。
            logger.LogError(ex, "查询季节商品进销失败 StoreCode={StoreCode} ProductCode={ProductCode}", scope.StoreCode, productCode);
            return StatusCode(500, new { success = false, message = "商品进销数据暂时无法读取" });
        }
    }

    /// <summary>校验分店访问范围并解析两个区间；未传区间时默认 8 月 1 日至门店当地今天。</summary>
    private async Task<QueryScope> ResolveScopeAsync(
        string? storeCode,
        string? inboundStartDate,
        string? inboundEndDate,
        string? salesStartDate,
        string? salesEndDate,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            return QueryScope.Fail(BadRequest(new { success = false, message = "storeCode 不能为空" }));
        }

        var normalizedStoreCode = storeCode.Trim();
        var allowedStoreCodes = await ResolveAllowedStoreCodesAsync(cancellationToken);
        if (!allowedStoreCodes.IsAllowed)
        {
            return QueryScope.Fail(Unauthorized(new { success = false, message = allowedStoreCodes.Message }));
        }
        if (!StoreProductInsightRules.CanAccessStore(allowedStoreCodes.StoreCodes, normalizedStoreCode))
        {
            return QueryScope.Fail(Forbid());
        }

        var storeToday = await queryService.GetStoreTodayAsync(normalizedStoreCode, cancellationToken);
        if (!storeToday.HasValue)
        {
            return QueryScope.Fail(NotFound(new { success = false, message = "分店不存在或未启用" }));
        }

        var fallback = (SeasonalProductInsightRules.SeasonStart(storeToday.Value), storeToday.Value);
        if (!SeasonalProductInsightRules.TryResolveRange(inboundStartDate, inboundEndDate, fallback, out var inbound, out var inboundError))
        {
            return QueryScope.Fail(BadRequest(new { success = false, message = $"进货区间：{inboundError}" }));
        }
        if (!SeasonalProductInsightRules.TryResolveRange(salesStartDate, salesEndDate, fallback, out var sales, out var salesError))
        {
            return QueryScope.Fail(BadRequest(new { success = false, message = $"销售区间：{salesError}" }));
        }

        return new QueryScope(normalizedStoreCode, inbound, sales, null);
    }

    /// <summary>与单店商品进销一致：全店角色（管理员、店长以上、仓库角色）可查任意分店，其余只查关联分店。</summary>
    private async Task<(bool IsAllowed, List<string>? StoreCodes, string Message)> ResolveAllowedStoreCodesAsync(
        CancellationToken cancellationToken
    )
    {
        var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var actorLabel = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userGuid) && !string.IsNullOrWhiteSpace(actorLabel))
        {
            userGuid = await _db.Queryable<User>()
                .Where(user => user.Username == actorLabel && !user.IsDeleted)
                .Select(user => user.UserGUID)
                .FirstAsync();
        }
        if (string.IsNullOrWhiteSpace(userGuid))
        {
            return (false, null, "未找到当前用户信息");
        }

        // 全店范围以实时角色快照为准，撤销后的旧 JWT 声明不能继续扩大范围。
        var snapshot = await roleService.GetUserPermissionSnapshotAsync(userGuid);
        if (snapshot?.Success != true || snapshot.Data == null)
        {
            return (false, null, "读取当前角色权限失败");
        }
        if (StoreProductInsightsController.HasElevatedStoreAccess(snapshot.Data.RoleNames))
        {
            return (true, null, string.Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var storeCodes = await _db.Queryable<UserStore>()
            .InnerJoin<Store>((userStore, store) => userStore.StoreGUID == store.StoreGUID)
            .Where((userStore, store) => userStore.UserGUID == userGuid && !userStore.IsDeleted && !store.IsDeleted)
            .Select((userStore, store) => store.StoreCode)
            .ToListAsync();
        return (true, storeCodes, string.Empty);
    }

    private sealed record QueryScope(
        string StoreCode,
        (DateTime Start, DateTime End) Inbound,
        (DateTime Start, DateTime End) Sales,
        IActionResult? Failure
    )
    {
        public static QueryScope Fail(IActionResult failure) => new(string.Empty, default, default, failure);
    }
}
