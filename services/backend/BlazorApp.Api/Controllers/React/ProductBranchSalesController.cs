using System.Globalization;
using System.Security.Claims;
using BlazorApp.Api.Features.ProductInsights;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/product-insights/branches")]
[Authorize(Policy = Permissions.Reports.ProductMovementView)]
public sealed class ProductBranchSalesController(
    IProductBranchSalesService service,
    IUserService userService,
    IRoleService roleService,
    ILogger<ProductBranchSalesController> logger,
    TimeProvider timeProvider
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? productCode,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var normalizedProductCode = Normalize(productCode);
            if (normalizedProductCode == null)
                return BadRequest(ApiResponse<ProductInsightBranchSalesDto>.Error("商品代码不能为空。", "INVALID_PRODUCT_CODE"));

            var defaultEndDate = ResolveBusinessToday(timeProvider);
            if (!TryResolveDateRange(startDate, endDate, defaultEndDate, out var range, out var error))
                return BadRequest(ApiResponse<ProductInsightBranchSalesDto>.Error(error!, "INVALID_DATE_RANGE"));

            var scope = await ResolveStoreScopeAsync(cancellationToken);
            if (!scope.HasAccess)
                return Forbid();

            var result = await service.GetAsync(
                normalizedProductCode,
                range.StartDate,
                range.EndDate,
                scope.AuthorizedStoreCodes,
                cancellationToken
            );
            return Ok(ApiResponse<ProductInsightBranchSalesDto>.OK(result));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<ProductInsightBranchSalesDto>.Error(exception.Message, "INVALID_REQUEST"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "商品跨 POS 分店销量查询失败 ProductCode={ProductCode}", productCode);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<ProductInsightBranchSalesDto>.Error("商品分店销量查询失败。", "QUERY_ERROR")
            );
        }
    }

    private async Task<(bool HasAccess, IReadOnlyCollection<string>? AuthorizedStoreCodes)> ResolveStoreScopeAsync(
        CancellationToken cancellationToken
    )
    {
        var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userGuid))
            return (false, Array.Empty<string>());

        // 全店角色必须由实时快照决定，撤销后的 JWT 角色不能继续扩大查询范围。
        var snapshot = await roleService.GetUserPermissionSnapshotAsync(userGuid);
        if (snapshot?.Success != true || snapshot.Data == null)
            return (false, Array.Empty<string>());
        var roles = snapshot.Data.RoleNames ?? new List<string>();
        var hasAllStoreAccess = roles.Any(role =>
            Permissions.SuperAdminRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase)
            || Permissions.WarehouseManagerRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase)
        );
        if (hasAllStoreAccess)
            return (true, null);

        cancellationToken.ThrowIfCancellationRequested();
        var stores = await userService.GetUserStoresAsync(userGuid);
        if (stores?.Success != true || stores.Data == null)
            return (false, Array.Empty<string>());
        return (
            true,
            stores.Data
                .Select(store => Normalize(store.StoreCode))
                .Where(code => code != null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        );
    }

    private static bool TryResolveDateRange(
        string? startDate,
        string? endDate,
        DateOnly defaultEndDate,
        out (DateOnly StartDate, DateOnly EndDate) range,
        out string? error
    )
    {
        error = null;
        var hasStartDate = !string.IsNullOrWhiteSpace(startDate);
        var hasEndDate = !string.IsNullOrWhiteSpace(endDate);
        if (hasStartDate != hasEndDate)
        {
            range = default;
            error = "开始日期和结束日期必须同时提供，或同时省略。";
            return false;
        }

        if (!hasStartDate)
        {
            range = (defaultEndDate.AddDays(-89), defaultEndDate);
            return true;
        }

        var resolvedStartDate = ParseBusinessDate(startDate!, "开始日期", out error);
        if (error != null)
        {
            range = default;
            return false;
        }
        var resolvedEndDate = ParseBusinessDate(endDate!, "结束日期", out error);
        if (error != null || resolvedStartDate > resolvedEndDate)
        {
            range = default;
            error ??= "开始日期不能晚于结束日期。";
            return false;
        }
        if (resolvedEndDate == DateOnly.MaxValue)
        {
            range = default;
            error = "结束日期不能晚于 9999-12-30。";
            return false;
        }
        range = (resolvedStartDate, resolvedEndDate);
        return true;
    }

    private static DateOnly ParseBusinessDate(string raw, string label, out string? error)
    {
        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            error = null;
            return date;
        }
        error = $"{label}必须使用 YYYY-MM-DD 格式。";
        return default;
    }

    private static DateOnly ResolveBusinessToday(TimeProvider timeProvider)
    {
        var australiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), australiaTimeZone).DateTime);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
