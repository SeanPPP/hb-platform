using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
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
        private readonly ILocalSupplierInvoiceHqSyncService _hqSyncService;
        private readonly ILocalSupplierInvoiceHqProductSyncService? _hqProductSyncService;
        private readonly SqlSugarContext _dbContext;

        public ReactLocalSupplierInvoicesController(
            ILocalSupplierInvoicesReactService service,
            SqlSugarContext dbContext,
            ILocalSupplierInvoiceHqSyncService hqSyncService,
            ILocalSupplierInvoiceHqProductSyncService? hqProductSyncService = null
        )
        {
            _service = service;
            _dbContext = dbContext;
            _hqSyncService = hqSyncService;
            _hqProductSyncService = hqProductSyncService;
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

        private async Task<bool> CanAccessAllStoresAsync(IEnumerable<string?> storeCodes)
        {
            if (IsFullStoreAccessUser())
                return true;

            var requestedStoreCodes = storeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct()
                .ToList();
            if (!requestedStoreCodes.Any())
                return false;

            var userStoreCodes = await GetCurrentUserStoreCodesAsync();
            return requestedStoreCodes.All(userStoreCodes.Contains);
        }

        private async Task<bool> CanAccessAllEnabledStoresAsync()
        {
            if (IsFullStoreAccessUser())
                return true;

            var activeStoreCodes = await _dbContext.Db.Queryable<Store>()
                .Where(store => store.IsActive == true && store.IsDeleted == false)
                .Select(store => store.StoreCode)
                .ToListAsync();
            activeStoreCodes = activeStoreCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            if (!activeStoreCodes.Any())
                return false;

            var userStoreCodes = await GetCurrentUserStoreCodesAsync();
            return activeStoreCodes.All(userStoreCodes.Contains);
        }

        [HttpPost("grid")]
        [Authorize(Policy = Permissions.LocalPurchase.View)]
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
        [Authorize(Policy = Permissions.LocalPurchase.View)]
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
        [Authorize(Policy = Permissions.LocalPurchase.View)]
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

        [HttpPost("{invoiceGuid}/details/grid")]
        [Authorize(Policy = Permissions.LocalPurchase.View)]
        public async Task<IActionResult> GetDetailsGrid(
            string invoiceGuid,
            [FromBody] GridRequestDto request
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();

            var result = await _service.GetDetailsGridAsync(invoiceGuid, request);
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
                        Items = result.Items ?? new List<LocalSupplierInvoiceItemDto>(),
                        Total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpPost]
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
        public async Task<IActionResult> Update(
            string invoiceGuid,
            [FromBody] UpdateInvoiceRequest dto
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();
            if (!string.IsNullOrWhiteSpace(dto.StoreCode) && !await CanAccessStoreAsync(dto.StoreCode))
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
        public async Task<IActionResult> UpdateToStorePrices(
            [FromBody] UpdateToStorePricesRequest dto
        )
        {
            if (!await CanAccessInvoiceAsync(dto.InvoiceGuid))
                return Forbid();
            if (dto.TargetStoreCodes == null || !await CanAccessAllStoresAsync(dto.TargetStoreCodes))
                return Forbid();
            if (dto.UpdateHqProduct && !await CanAccessAllEnabledStoresAsync())
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
            return BadRequest(
                new
                {
                    success = false,
                    message = result.Message,
                    details = result.Details ?? result.Data,
                }
            );
        }

        [HttpPost("check-products")]
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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

        [HttpPost("{invoiceGuid}/details/ensure-hq-products")]
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
        public async Task<IActionResult> EnsureHqProducts(
            [FromRoute] string invoiceGuid,
            [FromBody] EnsureHqProductsRequest dto
        )
        {
            if (!await CanAccessInvoiceAsync(invoiceGuid))
                return Forbid();
            if (dto.TargetStoreCodes == null || !await CanAccessAllStoresAsync(dto.TargetStoreCodes))
                return Forbid();
            if (!await CanAccessAllEnabledStoresAsync())
                return Forbid();
            if (_hqProductSyncService == null)
                return BadRequest(new { success = false, message = "HQ商品同步服务未注册" });

            var user = User.Identity?.Name ?? "system";
            var result = await _hqProductSyncService.EnsureHqProductsAsync(invoiceGuid, dto, user);
            if (result.Success)
                return Ok(
                    new
                    {
                        success = true,
                        data = result.Data,
                        message = result.Message,
                    }
                );
            return BadRequest(
                new
                {
                    success = false,
                    message = result.Message,
                    details = result.Details ?? result.Data,
                }
            );
        }

        [HttpPost("{invoiceGuid}/details/paste")]
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
        [Authorize(Policy = Permissions.LocalPurchase.View)]
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
        [Authorize(Policy = Permissions.LocalPurchase.View)]
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
        [Authorize(Policy = Permissions.LocalPurchase.View)]
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
        public async Task<IActionResult> CheckInvoiceNoExists([FromBody] CheckInvoiceNoExistsRequest dto)
        {
            if (!await CanAccessStoreAsync(dto.StoreCode))
                return Forbid();

            var result = await _service.CheckInvoiceNoExistsAsync(dto.StoreCode, dto.SupplierCode, dto.InvoiceNo);
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
        [Authorize(Policy = Permissions.LocalPurchase.Edit)]
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
            return BadRequest(new { success = false, message = result.Message, code = result.Code, details = result.Details });
        }

        [HttpPost("push-to-hq")]
        [Authorize(Policy = Permissions.LocalPurchase.PushToHq)]
        public async Task<IActionResult> PushInvoicesToHq([FromBody] PushToHqRequest request)
        {
            try
            {
                if (request.InvoiceGuids == null || !request.InvoiceGuids.Any())
                    return BadRequest(new { success = false, message = "请选择要推送的进货单" });
                foreach (var invoiceGuid in request.InvoiceGuids)
                {
                    if (!await CanAccessInvoiceAsync(invoiceGuid))
                        return Forbid();
                }

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

        [HttpPost("sync-from-hq")]
        [Authorize(Roles = "Admin,管理员")]
        public async Task<IActionResult> SyncFromHq([FromBody] LocalSupplierInvoiceHqSyncRequest? request)
        {
            request ??= new LocalSupplierInvoiceHqSyncRequest();

            if (request.EndDate.HasValue
                && request.StartDate.HasValue
                && request.EndDate.Value < request.StartDate.Value)
            {
                return BadRequest(
                    ApiResponse<LocalSupplierInvoiceHqSyncResult>.Error(
                        "结束日期不能早于起始日期",
                        "INVALID_DATE_RANGE"
                    )
                );
            }

            var result = await _hqSyncService.SyncForPageAsync(
                request.SelectedStoreCodes,
                request.StartDate,
                request.EndDate
            );
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
