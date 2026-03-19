using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/local-supplier-invoices")]
    [Authorize]
    public class ReactLocalSupplierInvoicesController : ControllerBase
    {
        private readonly ILocalSupplierInvoicesReactService _service;

        public ReactLocalSupplierInvoicesController(ILocalSupplierInvoicesReactService service)
        {
            _service = service;
        }

        [HttpPost("grid")]
        //  [Authorize(Roles = "Admin,WarehouseManager,Manager")]
        public async Task<IActionResult> Grid([FromBody] GridRequestDto request)
        {
            var result = await _service.GetGridDataAsync(request);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = new { Items = result.Items, Total = result.Total },
                        message = result.Message,
                    }
                );
            return Ok(
                new
                {
                    success = false,
                    data = new
                    {
                        Items = result.Items ?? new List<LocalSupplierInvoiceListDto>(),
                        Total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpGet("{invoiceGuid}")]
        // [Authorize(Roles = "Admin,WarehouseManager,Manager")]
        public async Task<IActionResult> GetInvoice(string invoiceGuid)
        {
            var result = await _service.GetInvoiceAsync(invoiceGuid);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return NotFound(new { success = false, message = result.Message });
        }

        [HttpGet("{invoiceGuid}/details")]

        public async Task<IActionResult> GetDetails(string invoiceGuid)
        {
            var result = await _service.GetDetailsAsync(invoiceGuid);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost]

        public async Task<IActionResult> Create([FromBody] CreateInvoiceRequest dto)
        {
            var result = await _service.CreateAsync(dto);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPut("{invoiceGuid}")]

        public async Task<IActionResult> Update(
            string invoiceGuid,
            [FromBody] UpdateInvoiceRequest dto
        )
        {
            var result = await _service.UpdateAsync(invoiceGuid, dto);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("{invoiceGuid}/details/batch-upsert")]

        public async Task<IActionResult> BatchUpsertDetails(
            string invoiceGuid,
            [FromBody] List<InvoiceDetailUpsertItemDto> items
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchUpsertDetailsAsync(invoiceGuid, items, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpDelete("{invoiceGuid}")]

        public async Task<IActionResult> Delete(string invoiceGuid)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.DeleteAsync(invoiceGuid, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("detect/supplier-item")]

        public async Task<IActionResult> DetectSupplierItem(
            [FromBody] DetectSupplierItemRequest dto
        )
        {
            var result = await _service.DetectSupplierItemAsync(dto);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("detect/barcode")]

        public async Task<IActionResult> DetectBarcode([FromBody] DetectBarcodeRequest dto)
        {
            var result = await _service.DetectBarcodeAsync(dto);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("update-to-store-prices")]

        public async Task<IActionResult> UpdateToStorePrices(
            [FromBody] UpdateToStorePricesRequest dto
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpdateDetailsToStorePricesAsync(dto, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("check-products")]

        public async Task<IActionResult> CheckProducts([FromBody] CheckProductsRequest dto)
        {
            var result = await _service.CheckProductsAsync(dto);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("{invoiceGuid}/details/paste")]

        public async Task<IActionResult> PasteDetails(
            [FromRoute] string invoiceGuid,
            [FromBody] PasteDetailsRequest dto
        )
        {
            var user = User.Identity?.Name ?? "system";
            dto.InvoiceGuid = invoiceGuid;
            var result = await _service.PasteDetailsAsync(dto, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPut("{invoiceGuid}/details/{detailGuid}/action")]

        public async Task<IActionResult> UpdateDetailAction(
            [FromRoute] string invoiceGuid,
            [FromRoute] string detailGuid,
            [FromBody] UpdateDetailActionRequest dto
        )
        {
            var result = await _service.UpdateDetailActionAsync(invoiceGuid, detailGuid, dto.Action);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpDelete("{invoiceGuid}/details")]
        public async Task<IActionResult> DeleteDetails(
            [FromRoute] string invoiceGuid,
            [FromBody] List<string> detailGuids
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.DeleteDetailsAsync(invoiceGuid, detailGuids, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpGet("{invoiceGuid}/barcode-abnormal-details")]
        public async Task<IActionResult> GetBarcodeAbnormalDetails(
            [FromRoute] string invoiceGuid
        )
        {
            var result = await _service.GetBarcodeAbnormalDetailsAsync(invoiceGuid);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpGet("{invoiceGuid}/products-by-barcode")]
        public async Task<IActionResult> GetProductsByBarcode(
            [FromRoute] string invoiceGuid,
            [FromQuery] string barcode
        )
        {
            var result = await _service.GetProductsByBarcodeAsync(invoiceGuid, barcode);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(new { success = false, message = result.Message });
        }
    }
}
