using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/posm-sales-orders")]
    [AllowAnonymous]
    public class PosmSalesOrderController : ControllerBase
    {
        private readonly IPosmSalesOrderReactService _service;
        private readonly ITaxInvoiceService _taxInvoiceService;
        private readonly ILogger<PosmSalesOrderController> _logger;

        public PosmSalesOrderController(
            IPosmSalesOrderReactService service,
            ITaxInvoiceService taxInvoiceService,
            ILogger<PosmSalesOrderController> logger
        )
        {
            _service = service;
            _taxInvoiceService = taxInvoiceService;
            _logger = logger;
        }

        [HttpPost("list")]
        public async Task<IActionResult> GetSalesOrderList([FromBody] PosmSalesOrderQueryParams queryParams)
        {
            try
            {
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
