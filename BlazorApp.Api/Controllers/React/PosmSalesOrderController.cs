using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/posm-sales-orders")]
    [Authorize]
    public class PosmSalesOrderController : ControllerBase
    {
        private readonly IPosmSalesOrderReactService _service;
        private readonly ITaxInvoiceService _taxInvoiceService;
        private readonly SqlSugarContext _dbContext;
        private readonly ILogger<PosmSalesOrderController> _logger;

        public PosmSalesOrderController(
            IPosmSalesOrderReactService service,
            ITaxInvoiceService taxInvoiceService,
            SqlSugarContext dbContext,
            ILogger<PosmSalesOrderController> logger
        )
        {
            _service = service;
            _taxInvoiceService = taxInvoiceService;
            _dbContext = dbContext;
            _logger = logger;
        }

        private bool IsAdmin()
        {
            var user = User;
            if (user == null) return false;
            return user.Claims.Any(c =>
                c.Type == ClaimTypes.Role
                && (c.Value.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                    || c.Value.Equals("WarehouseManager", StringComparison.OrdinalIgnoreCase))
            );
        }

        private string GetCurrentUserGuid()
        {
            return User?.FindFirst("userId")?.Value
                ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? string.Empty;
        }

        private async Task<List<string>> GetCurrentUserStoreCodesAsync()
        {
            var result = new List<string>();
            var userGuid = GetCurrentUserGuid();
            if (string.IsNullOrEmpty(userGuid))
                return result;

            var storeGuids = await _dbContext.Db.Queryable<UserStore>()
                .Where(us => us.UserGUID == userGuid)
                .Select(us => us.StoreGUID)
                .ToListAsync();

            if (!storeGuids.Any())
                return result;

            var codes = await _dbContext.Db.Queryable<Store>()
                .Where(s => storeGuids.Contains(s.StoreGUID))
                .Select(s => s.StoreCode)
                .ToListAsync();

            result.AddRange(codes.Where(c => !string.IsNullOrEmpty(c)));
            return result;
        }

        [HttpPost("list")]
        public async Task<IActionResult> GetSalesOrderList([FromBody] PosmSalesOrderQueryParams queryParams)
        {
            try
            {
                if (!IsAdmin())
                {
                    var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                    if (userStoreCodes.Any())
                    {
                        if (!string.IsNullOrWhiteSpace(queryParams.BranchCode))
                        {
                            if (!userStoreCodes.Contains(queryParams.BranchCode))
                            {
                                return Ok(new { success = true, data = new PagedListReactDto<PosmSalesOrderDto> { Items = new List<PosmSalesOrderDto>(), Total = 0, PageNumber = queryParams.PageNumber, PageSize = queryParams.PageSize } });
                            }
                        }
                        else
                        {
                            queryParams.BranchCodes = userStoreCodes;
                        }
                    }
                }

                var result = await _service.GetSalesOrderListAsync(queryParams);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSalesOrderList failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("detail/{orderGuid}")]
        public async Task<IActionResult> GetSalesOrderDetail(string orderGuid)
        {
            try
            {
                var result = await _service.GetSalesOrderDetailAsync(orderGuid);

                if (result.Success && result.Data?.Order != null && !IsAdmin())
                {
                    var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                    if (userStoreCodes.Any() && !string.IsNullOrEmpty(result.Data.Order.BranchCode))
                    {
                        if (!userStoreCodes.Contains(result.Data.Order.BranchCode))
                        {
                            return Forbid();
                        }
                    }
                }

                return Ok(new
                {
                    success = result.Success,
                    data = result.Data,
                    message = result.Message,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSalesOrderDetail failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("tax-invoice/{orderGuid}")]
        public async Task<IActionResult> GetTaxInvoicePdf(string orderGuid)
        {
            try
            {
                if (!IsAdmin())
                {
                    var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                    if (userStoreCodes.Any())
                    {
                        var result = await _service.GetSalesOrderDetailAsync(orderGuid);
                        if (result.Success && result.Data?.Order != null
                            && !string.IsNullOrEmpty(result.Data.Order.BranchCode)
                            && !userStoreCodes.Contains(result.Data.Order.BranchCode))
                        {
                            return Forbid();
                        }
                    }
                }

                var pdfBytes = await _taxInvoiceService.GenerateTaxInvoicePdfAsync(orderGuid);
                return File(pdfBytes, "application/pdf", $"TaxInvoice_{orderGuid}.pdf");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "GetTaxInvoicePdf failed: {Message}", ex.Message);
                return NotFound(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTaxInvoicePdf failed");
                return StatusCode(500, new { success = false, message = "生成PDF失败" });
            }
        }
    }
}
