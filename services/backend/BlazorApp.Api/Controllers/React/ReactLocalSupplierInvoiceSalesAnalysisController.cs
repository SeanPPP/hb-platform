using System.Security.Claims;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/local-supplier-invoices")]
    [Authorize(Policy = Permissions.LocalPurchase.View)]
    public class ReactLocalSupplierInvoiceSalesAnalysisController : ControllerBase
    {
        private readonly ILocalSupplierInvoiceSalesAnalysisService _service;
        private readonly IUserService _userService;
        private readonly SqlSugarContext _dbContext;
        private readonly ILogger<ReactLocalSupplierInvoiceSalesAnalysisController> _logger;

        public ReactLocalSupplierInvoiceSalesAnalysisController(
            ILocalSupplierInvoiceSalesAnalysisService service,
            IUserService userService,
            SqlSugarContext dbContext,
            ILogger<ReactLocalSupplierInvoiceSalesAnalysisController> logger
        )
        {
            _service = service;
            _userService = userService;
            _dbContext = dbContext;
            _logger = logger;
        }

        [Authorize(Policy = Permissions.LocalPurchase.View)]
        [HttpGet("{invoiceGuid}/sales-analysis")]
        public async Task<IActionResult> GetSalesAnalysis(string invoiceGuid)
        {
            var storeScope = await ResolveStoreScopeAsync(null, requireStoreSelectionWhenMissing: false);
            if (storeScope.Forbidden)
            {
                return Forbid();
            }

            if (!await CanAccessInvoiceAsync(invoiceGuid, storeScope))
            {
                return Forbid();
            }

            var result = await _service.GetAnalysisAsync(invoiceGuid);
            if (result.Success)
            {
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            }

            return NotFound(new { success = false, message = result.Message });
        }

        [Authorize(Policy = Permissions.LocalPurchase.View)]
        [HttpGet("purchase-sales-analysis")]
        public async Task<IActionResult> GetPurchaseSalesAnalysis(
            [FromQuery] LocalSupplierPurchaseSalesAnalysisQueryDto query
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query.StoreCode))
                {
                    return BadRequest(
                        ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.Error(
                            "请先选择分店。",
                            "VALIDATION_ERROR"
                        )
                    );
                }

                if (string.IsNullOrWhiteSpace(query.SupplierCode))
                {
                    return BadRequest(
                        ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.Error(
                            "请先选择供应商。",
                            "VALIDATION_ERROR"
                        )
                    );
                }

                var storeScope = await ResolveStoreScopeAsync(query.StoreCode);
                if (storeScope.Forbidden)
                {
                    return Forbid();
                }

                if (storeScope.RequiresStoreSelection)
                {
                    return BadRequest(
                        ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.Error(
                            "当前账号关联多个门店，请先选择一个门店。",
                            "STORE_REQUIRED"
                        )
                    );
                }

                query.StoreCode = storeScope.SelectedStoreCode ?? query.StoreCode;
                var result = await _service.GetPurchaseSalesAnalysisAsync(
                    query,
                    storeScope.ScopedStoreCodes
                );

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店供应商进货销量分析查询失败");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<LocalSupplierPurchaseSalesAnalysisResponseDto>.Error(
                        "分店供应商进货销量分析查询失败"
                    )
                );
            }
        }

        [Authorize(Policy = Permissions.LocalPurchase.View)]
        [HttpGet("purchase-sales-analysis/store-options")]
        public async Task<IActionResult> GetPurchaseSalesAnalysisStoreOptions()
        {
            try
            {
                var storeScope = await ResolveStoreScopeAsync(null, requireStoreSelectionWhenMissing: false);
                if (storeScope.Forbidden)
                {
                    return Forbid();
                }

                var result = await _service.GetStoreOptionsAsync(storeScope.ScopedStoreCodes);
                return Ok(ApiResponse<List<LocalSupplierPurchaseSalesAnalysisStoreOptionDto>>.OK(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店供应商进货销量分析分店选项加载失败");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<List<LocalSupplierPurchaseSalesAnalysisStoreOptionDto>>.Error(
                        "分店供应商进货销量分析分店选项加载失败"
                    )
                );
            }
        }

        [Authorize(Policy = Permissions.LocalPurchase.View)]
        [HttpGet("purchase-sales-analysis/supplier-options")]
        public async Task<IActionResult> GetPurchaseSalesAnalysisSupplierOptions(
            [FromQuery] string? storeCode
        )
        {
            try
            {
                // 供应商候选跟随门店权限收口，避免普通用户看到无权门店的进货供应商。
                var storeScope = await ResolveStoreScopeAsync(
                    storeCode,
                    requireStoreSelectionWhenMissing: false
                );
                if (storeScope.Forbidden)
                {
                    return Forbid();
                }

                var result = await _service.GetSupplierOptionsAsync(
                    storeScope.ScopedStoreCodes,
                    storeScope.SelectedStoreCode ?? storeCode
                );
                return Ok(
                    ApiResponse<List<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto>>.OK(result)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分店供应商进货销量分析供应商选项加载失败");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResponse<List<LocalSupplierPurchaseSalesAnalysisSupplierOptionDto>>.Error(
                        "分店供应商进货销量分析供应商选项加载失败"
                    )
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

            // 多门店普通用户必须显式选店，避免把多个分店的进货节奏混成一张表。
            return new StoreScopeResult
            {
                RequiresStoreSelection = true,
                ScopedStoreCodes = accessibleStoreCodes,
            };
        }

        private async Task<bool> CanAccessInvoiceAsync(string invoiceGuid, StoreScopeResult storeScope)
        {
            if (storeScope.ScopedStoreCodes == null || storeScope.ScopedStoreCodes.Count == 0)
            {
                return true;
            }

            var invoiceStoreCode = await _dbContext.Db.Queryable<StoreLocalSupplierInvoice>()
                .Where(invoice => invoice.InvoiceGUID == invoiceGuid && invoice.IsDeleted == false)
                .Select(invoice => invoice.StoreCode)
                .FirstAsync();

            return !string.IsNullOrWhiteSpace(invoiceStoreCode)
                && storeScope.ScopedStoreCodes.Contains(
                    invoiceStoreCode,
                    StringComparer.OrdinalIgnoreCase
                );
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
