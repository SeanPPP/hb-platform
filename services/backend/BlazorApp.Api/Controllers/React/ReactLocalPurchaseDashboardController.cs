using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/local-purchase-dashboard")]
    [Authorize(Policy = Permissions.LocalPurchase.View)]
    public class ReactLocalPurchaseDashboardController : ControllerBase
    {
        private readonly ILocalPurchaseDashboardService _service;
        private readonly IUserService _userService;
        private readonly ILogger<ReactLocalPurchaseDashboardController> _logger;

        public ReactLocalPurchaseDashboardController(
            ILocalPurchaseDashboardService service,
            IUserService userService,
            ILogger<ReactLocalPurchaseDashboardController> logger
        )
        {
            _service = service;
            _userService = userService;
            _logger = logger;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.LocalPurchase.View)]
        public async Task<IActionResult> GetDashboard(
            [FromQuery] string? endMonth,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var scope = await ResolveStoreScopeAsync();
                if (scope.Forbidden)
                {
                    return Forbid();
                }

                var result = await _service.GetDashboardAsync(
                    endMonth ?? string.Empty,
                    scope.ServiceScope,
                    cancellationToken
                );
                return ToActionResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 客户端已取消请求时继续向上抛出，避免被误报为服务端 500。
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "进货金额看板接口执行失败 EndMonth={EndMonth}", endMonth);
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<LocalPurchaseDashboardResponseDto>.Error("进货金额看板查询失败")
                );
            }
        }

        [HttpGet("stores/{storeCode}/suppliers")]
        [Authorize(Policy = Permissions.LocalPurchase.View)]
        public async Task<IActionResult> GetStoreSuppliers(
            string storeCode,
            [FromQuery] string? endMonth,
            CancellationToken cancellationToken
        )
        {
            try
            {
                var scope = await ResolveStoreScopeAsync();
                if (scope.Forbidden || !scope.CanAccess(storeCode))
                {
                    return Forbid();
                }

                var result = await _service.GetStoreSuppliersAsync(
                    storeCode,
                    endMonth ?? string.Empty,
                    scope.ServiceScope,
                    cancellationToken
                );
                if (string.Equals(result.ErrorCode, "FORBIDDEN", StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }

                return ToActionResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 抽屉切换分店或月份时允许数据库查询及时终止。
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "分店供应商进货金额接口执行失败 StoreCode={StoreCode}, EndMonth={EndMonth}",
                    storeCode,
                    endMonth
                );
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<LocalPurchaseDashboardStoreSuppliersDto>.Error(
                        "分店供应商进货金额查询失败"
                    )
                );
            }
        }

        private IActionResult ToActionResult<T>(ApiResponse<T> result)
        {
            if (result.Success)
            {
                return Ok(result);
            }

            return string.Equals(
                result.ErrorCode,
                "VALIDATION_ERROR",
                StringComparison.OrdinalIgnoreCase
            )
                ? BadRequest(result)
                : StatusCode(StatusCodes.Status500InternalServerError, result);
        }

        private async Task<DashboardStoreScope> ResolveStoreScopeAsync()
        {
            if (HasFullStoreScope(User))
            {
                // 只有管理员或仓库经理分支会显式构造全店范围。
                return new DashboardStoreScope(LocalPurchaseDashboardStoreScope.AllStores());
            }

            var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return new DashboardStoreScope(
                    LocalPurchaseDashboardStoreScope.Restricted(Array.Empty<string>()),
                    true
                );
            }

            var userResult = await _userService.GetUserByGuidAsync(userGuid);
            var storeCodes = userResult.Data?.Stores?
                .Select(store => NormalizeStoreCode(store.StoreCode))
                .Where(code => code != null)
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            return storeCodes.Count == 0
                ? new DashboardStoreScope(
                    LocalPurchaseDashboardStoreScope.Restricted(storeCodes),
                    true
                )
                : new DashboardStoreScope(LocalPurchaseDashboardStoreScope.Restricted(storeCodes));
        }

        internal static bool HasFullStoreScope(ClaimsPrincipal user)
        {
            ArgumentNullException.ThrowIfNull(user);
            // 统一复用系统角色别名，避免历史管理员或仓库经理账号被错误降级为分店范围。
            var fullStoreScopeRoles = Permissions.SuperAdminRoleNames
                .Concat(Permissions.WarehouseManagerRoleNames);
            return user.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && fullStoreScopeRoles.Contains(
                    claim.Value,
                    StringComparer.OrdinalIgnoreCase
                )
            );
        }

        private static string? NormalizeStoreCode(string? storeCode)
        {
            return string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim();
        }

        private sealed record DashboardStoreScope(
            LocalPurchaseDashboardStoreScope ServiceScope,
            bool Forbidden = false
        )
        {
            public bool CanAccess(string? storeCode)
            {
                var normalized = NormalizeStoreCode(storeCode);
                if (normalized == null)
                {
                    return false;
                }

                return ServiceScope.IncludesAllStores
                    || ServiceScope.StoreCodes.Contains(
                        normalized,
                        StringComparer.OrdinalIgnoreCase
                    );
            }
        }
    }
}
