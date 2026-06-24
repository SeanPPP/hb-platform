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
