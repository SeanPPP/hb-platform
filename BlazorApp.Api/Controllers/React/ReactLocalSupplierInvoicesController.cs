using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/local-supplier-invoices")]
    [Authorize]
    public class ReactLocalSupplierInvoicesController : ControllerBase
    {
        private readonly ILocalSupplierInvoicesReactService _service;
        private readonly SqlSugarContext _dbContext;

        public ReactLocalSupplierInvoicesController(
            ILocalSupplierInvoicesReactService service,
            SqlSugarContext dbContext
        )
        {
            _service = service;
            _dbContext = dbContext;
        }

        private bool IsFullStoreAccessUser()
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
            var userGuid = GetCurrentUserGuid();
            if (string.IsNullOrEmpty(userGuid))
                return new List<string>();

            var storeGuids = await _dbContext.Db.Queryable<UserStore>()
                .Where(us => us.UserGUID == userGuid)
                .Select(us => us.StoreGUID)
                .ToListAsync();

            if (!storeGuids.Any())
                return new List<string>();

            var codes = await _dbContext.Db.Queryable<Store>()
                .Where(s => storeGuids.Contains(s.StoreGUID))
                .Select(s => s.StoreCode)
                .ToListAsync();

            return codes.Where(c => !string.IsNullOrEmpty(c)).ToList();
        }

        private async Task<bool> CanAccessInvoiceAsync(string invoiceGuid)
        {
            if (IsFullStoreAccessUser())
                return true;

            var userStoreCodes = await GetCurrentUserStoreCodesAsync();
            if (!userStoreCodes.Any())
                return false;

            var storeCode = await _dbContext.Db.Queryable<StoreLocalSupplierInvoice>()
                .Where(i => i.InvoiceGUID == invoiceGuid && i.IsDeleted == false)
                .Select(i => i.StoreCode)
                .FirstAsync();

            return !string.IsNullOrEmpty(storeCode) && userStoreCodes.Contains(storeCode);
        }

        private async Task<bool> CanAccessStoreAsync(string? storeCode)
        {
            if (IsFullStoreAccessUser())
                return true;

            if (string.IsNullOrEmpty(storeCode))
                return false;

            var userStoreCodes = await GetCurrentUserStoreCodesAsync();
            return userStoreCodes.Contains(storeCode);
        }

        [HttpPost("grid")]
        //  [Authorize(Roles = "Admin,WarehouseManager,Manager")]
        public async Task<IActionResult> Grid([FromBody] GridRequestDto request)
        {
            var allowedStoreCodes = IsFullStoreAccessUser()
                ? null
                : await GetCurrentUserStoreCodesAsync();
            var result = await _service.GetGridDataAsync(request, allowedStoreCodes);
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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessStoreAsync(dto.StoreCode))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessStoreAsync(dto.StoreCode))
                return Forbid();

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
            if (!await CanAccessStoreAsync(dto.StoreCode))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(dto.InvoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(dto.InvoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

            var result = await _service.UpdateDetailActionAsync(
                invoiceGuid,
                detailGuid,
                dto.Action
            );
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

        [HttpPut("{invoiceGuid}/details/batch-action")]
        public async Task<IActionResult> BatchUpdateDetailAction(
            [FromRoute] string invoiceGuid,
            [FromBody] BatchUpdateDetailActionRequest dto
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

            var result = await _service.BatchUpdateDetailActionAsync(invoiceGuid, dto);
            if (result.Success)
                return Ok(new { success = true, data = result.Data });
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpDelete("{invoiceGuid}/details")]
        public async Task<IActionResult> DeleteDetails(
            [FromRoute] string invoiceGuid,
            [FromBody] List<string> detailGuids
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
        public async Task<IActionResult> GetBarcodeAbnormalDetails([FromRoute] string invoiceGuid)
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

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

        [HttpGet("{invoiceGuid}/products-by-product-code")]
        public async Task<IActionResult> GetProductsByProductCode(
            [FromRoute] string invoiceGuid,
            [FromQuery] string productCode
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

            var result = await _service.GetProductsByProductCodeAsync(invoiceGuid, productCode);
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

        [HttpPost("check-invoice-no")]
        public async Task<IActionResult> CheckInvoiceNoExists([FromBody] CheckInvoiceNoExistsRequest dto)
        {
            var result = await _service.CheckInvoiceNoExistsAsync(dto.SupplierCode, dto.InvoiceNo);
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

        [HttpPost("{invoiceGuid}/details/batch-execute")]
        [Authorize(Roles = "Admin,WarehouseManager,Manager")]
        public async Task<IActionResult> BatchExecuteActions(
            [FromRoute] string invoiceGuid,
            [FromBody] BatchExecuteActionsRequestDto dto
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchExecuteActionsAsync(invoiceGuid, dto.DetailGuids, user);
            if (result.Success)
                return Ok(new { success = true, data = result.Data, message = result.Message });
            return BadRequest(new { success = false, message = result.Message });
        }

        [HttpPost("push-to-hq")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PushInvoicesToHq([FromBody] PushToHqRequest request)
        {
            try
            {
                if (request.InvoiceGuids == null || !request.InvoiceGuids.Any())
                    return BadRequest(new { success = false, message = "请选择要推送的进货单" });

                var result = await _service.PushInvoicesToHqAsync(request.InvoiceGuids);
                if (result.IsSuccess)
                    return Ok(ApiResponse<SyncResult>.OK(result, result.Message));
                return Ok(ApiResponse<SyncResult>.OK(result, result.Message));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ApiResponse<SyncResult>.Error($"推送异常: {ex.Message}", "INTERNAL_ERROR"));
            }
        }
    }
}
