using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// 商品经营分析报表控制器。
    /// </summary>
    [ApiController]
    [Route("api/react/v1/product-movement-report")]
    [Authorize(Policy = Permissions.Reports.ProductMovementView)]
    public class ProductMovementReportController : ControllerBase
    {
        private readonly IProductMovementReportService _service;
        private readonly IUserService _userService;
        private readonly ILogger<ProductMovementReportController> _logger;

        public ProductMovementReportController(
            IProductMovementReportService service,
            IUserService userService,
            ILogger<ProductMovementReportController> logger
        )
        {
            _service = service;
            _userService = userService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetReport([FromQuery] ProductMovementReportQueryDto query)
        {
            try
            {
                var storeScope = await ResolveStoreScopeAsync(query.StoreCode);
                if (storeScope.Forbidden)
                {
                    return Forbid();
                }

                if (storeScope.RequiresStoreSelection)
                {
                    return BadRequest(
                        ApiResponse<ProductMovementReportResponseDto>.Error(
                            "当前账号关联多个门店，请先选择一个门店。",
                            "STORE_REQUIRED"
                        )
                    );
                }

                query.StoreCode = storeScope.SelectedStoreCode ?? query.StoreCode;
                var result = await _service.GetReportAsync(query, storeScope.ScopedStoreCodes);
                return Ok(ApiResponse<ProductMovementReportResponseDto>.OK(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品经营分析报表查询失败");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<ProductMovementReportResponseDto>.Error("商品经营分析报表查询失败")
                );
            }
        }

        [HttpGet("store-options")]
        public async Task<IActionResult> GetStoreOptions()
        {
            try
            {
                var storeScope = await ResolveStoreScopeAsync(null, requireStoreSelectionWhenMissing: false);
                if (storeScope.Forbidden)
                {
                    return Forbid();
                }

                var result = await _service.GetStoreOptionsAsync(storeScope.ScopedStoreCodes);
                return Ok(ApiResponse<List<ProductMovementReportStoreOptionDto>>.OK(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "商品经营分析分店选项加载失败");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<List<ProductMovementReportStoreOptionDto>>.Error("商品经营分析分店选项加载失败")
                );
            }
        }

        private async Task<StoreScopeResult> ResolveStoreScopeAsync(
            string? requestedStoreCode,
            bool requireStoreSelectionWhenMissing = true
        )
        {
            if (IsAdminOrWarehouseManager())
            {
                return new StoreScopeResult
                {
                    SelectedStoreCode = NormalizeStoreCode(requestedStoreCode),
                    ScopedStoreCodes = string.IsNullOrWhiteSpace(requestedStoreCode)
                        ? null
                        : new[] { NormalizeStoreCode(requestedStoreCode)! },
                };
            }

            var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return new StoreScopeResult { Forbidden = true };
            }

            var userResult = await _userService.GetUserByGuidAsync(userGuid);
            var accessibleStoreCodes = userResult.Data?.Stores?
                .Select(store => NormalizeStoreCode(store.StoreCode))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (accessibleStoreCodes.Count == 0)
            {
                return new StoreScopeResult { Forbidden = true };
            }

            var normalizedRequestedStore = NormalizeStoreCode(requestedStoreCode);
            if (!string.IsNullOrWhiteSpace(normalizedRequestedStore))
            {
                return accessibleStoreCodes.Contains(
                    normalizedRequestedStore,
                    StringComparer.OrdinalIgnoreCase
                )
                    ? new StoreScopeResult
                    {
                        SelectedStoreCode = normalizedRequestedStore,
                        ScopedStoreCodes = new[] { normalizedRequestedStore },
                    }
                    : new StoreScopeResult { Forbidden = true };
            }

            if (accessibleStoreCodes.Count == 1)
            {
                return new StoreScopeResult
                {
                    SelectedStoreCode = accessibleStoreCodes[0],
                    ScopedStoreCodes = accessibleStoreCodes,
                };
            }

            if (!requireStoreSelectionWhenMissing)
            {
                return new StoreScopeResult
                {
                    ScopedStoreCodes = accessibleStoreCodes,
                };
            }

            // 多门店店长查询报表时必须显式选择门店，避免把多店经营预警混成一个列表。
            return new StoreScopeResult
            {
                RequiresStoreSelection = true,
                ScopedStoreCodes = accessibleStoreCodes,
            };
        }

        private bool IsAdminOrWarehouseManager()
        {
            return HasRole("Admin")
                || HasRole("管理员")
                || HasRole("WarehouseManager")
                || HasRole("仓库经理");
        }

        private bool HasRole(string role)
        {
            return User.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && claim.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            );
        }

        private static string? NormalizeStoreCode(string? storeCode)
        {
            return string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim();
        }

        private sealed class StoreScopeResult
        {
            public bool Forbidden { get; set; }
            public bool RequiresStoreSelection { get; set; }
            public string? SelectedStoreCode { get; set; }
            public IReadOnlyList<string>? ScopedStoreCodes { get; set; }
        }
    }
}
