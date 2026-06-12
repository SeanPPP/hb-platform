using System.Text.Json;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-product-prices")]
    [Authorize]
    public class ReactStoreProductPricesController : ControllerBase
    {
        private readonly IStoreProductPriceReactService _service;
        private readonly IStoreRetailPriceReactService _retailPriceService;
        private readonly IUserService _userService;
        private readonly ILogger<ReactStoreProductPricesController> _logger;

        public ReactStoreProductPricesController(
            IStoreProductPriceReactService service,
            IStoreRetailPriceReactService retailPriceService,
            IUserService userService,
            ILogger<ReactStoreProductPricesController> logger
        )
        {
            _service = service;
            _retailPriceService = retailPriceService;
            _userService = userService;
            _logger = logger;
        }

        [HttpPost("grid")]
        [Authorize(Policy = Permissions.StoreProducts.View)]
        public async Task<IActionResult> Grid([FromBody] StoreProductPriceQueryDto query)
        {
            if (string.IsNullOrWhiteSpace(query.StoreCode))
            {
                return Ok(new
                {
                    success = false,
                    message = "请选择分店"
                });
            }

            var result = await _service.GetGridDataAsync(query);
            if (result.Success)
            {
                return Ok(new
                {
                    success = true,
                    data = new { Items = result.Items, Total = result.Total },
                    message = result.Message
                });
            }

            return Ok(new
            {
                success = false,
                data = new { Items = result.Items, Total = result.Total },
                message = result.Message
            });
        }

        [HttpPost("batch-update")]
        [Authorize(Policy = Permissions.StoreProducts.Edit)]
        public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateStoreRetailPriceDto dto)
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.BatchUpdateStoreRetailPricesAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("sync-to-other-stores")]
        [Authorize(Policy = Permissions.StoreProducts.Edit)]
        public async Task<IActionResult> SyncToOtherStores([FromBody] SyncToOtherStoresDto dto)
        {
            if (!await CanAccessSyncStoresAsync(dto.SourceStoreCode, dto.TargetStoreCodes))
            {
                return Forbid();
            }

            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.SyncToOtherStoresAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("copy-store-data")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CopyStoreData([FromBody] CopyStoreDataDto dto)
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.CopyStoreDataAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpGet("copy-store-data/stream")]
        [Authorize(Roles = "Admin")]
        public async Task CopyStoreDataStream(
            [FromQuery] string sourceStoreCode,
            [FromQuery] string[] targetStoreCodes,
            [FromQuery] string mode,
            [FromQuery] bool syncMultiCode,
            CancellationToken cancellationToken)
        {
            if (targetStoreCodes == null || targetStoreCodes.Length == 0)
            {
                Response.ContentType = "application/json";
                await Response.WriteAsync(
                    JsonSerializer.Serialize(new { success = false, message = "请选择目标分店" }),
                    cancellationToken
                );
                return;
            }

            Response.ContentType = "text/event-stream";
            Response.StatusCode = 200;
            Response.Headers["Cache-Control"] = "no-cache, no-store";
            Response.Headers["Connection"] = "keep-alive";
            Response.Headers["X-Accel-Buffering"] = "no";

            var dto = new CopyStoreDataDto
            {
                SourceStoreCode = sourceStoreCode,
                TargetStoreCodes = new System.Collections.Generic.List<string>(targetStoreCodes),
                Mode = mode ?? "Overwrite",
                SyncMultiCode = syncMultiCode
            };

            var updatedBy = User.Identity?.Name ?? "system";

            try
            {
                await foreach (var progress in _service.CopyStoreDataWithProgressAsync(dto, updatedBy, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var json = JsonSerializer.Serialize(progress);
                    await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SSE 连接已断开");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SSE 流异常");
                var errorJson = JsonSerializer.Serialize(new CopyProgressDto
                {
                    EventType = "error",
                    Message = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
                await Response.WriteAsync($"data: {errorJson}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }

        [HttpPost("sync-from-hq")]
        [Authorize(Roles = "Admin,管理员")]
        public async Task<IActionResult> SyncFromHq([FromBody] SyncRetailPriceFromHqRequest? request)
        {
            request ??= new SyncRetailPriceFromHqRequest();

            if (request.SelectedStoreCodes?.Any(x => !string.IsNullOrWhiteSpace(x)) != true)
            {
                return BadRequest(
                    ApiResponse<SyncRetailPriceFromHqResult>.Error(
                        "请选择分店",
                        "INVALID_STORE_SCOPE"
                    )
                );
            }

            if (!request.StartDate.HasValue || !request.EndDate.HasValue)
            {
                return BadRequest(
                    ApiResponse<SyncRetailPriceFromHqResult>.Error(
                        "请选择起止日期",
                        "INVALID_DATE_RANGE"
                    )
                );
            }

            if (request.EndDate.Value < request.StartDate.Value)
            {
                return BadRequest(
                    ApiResponse<SyncRetailPriceFromHqResult>.Error(
                        "结束日期不能早于起始日期",
                        "INVALID_DATE_RANGE"
                    )
                );
            }

            var result = await _retailPriceService.SyncFromHqAsync(
                request.SelectedStoreCodes,
                request.StartDate,
                request.EndDate
            );
            return result.Success ? Ok(result) : BadRequest(result);
        }

        private async Task<bool> CanAccessSyncStoresAsync(string? sourceStoreCode, IEnumerable<string>? targetStoreCodes)
        {
            var storeCodes = await GetAccessibleStoreCodesAsync();
            if (storeCodes == null)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(sourceStoreCode) || !storeCodes.Contains(sourceStoreCode))
            {
                return false;
            }

            if (targetStoreCodes == null)
            {
                return false;
            }

            return targetStoreCodes.All(storeCode => !string.IsNullOrWhiteSpace(storeCode) && storeCodes.Contains(storeCode));
        }

        private async Task<HashSet<string>?> GetAccessibleStoreCodesAsync()
        {
            if (User.IsInRole("Admin")
                || User.IsInRole("管理员")
                || User.IsInRole("WarehouseManager")
                || User.IsInRole("仓库经理")
                || User.IsInRole("WarehouseStaff")
                || User.IsInRole("仓库员工"))
            {
                return null;
            }

            var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return new HashSet<string>();
            }

            var storesResult = await _userService.GetUserStoresAsync(userGuid);
            if (!storesResult.Success || storesResult.Data == null)
            {
                return new HashSet<string>();
            }

            return storesResult.Data
                .Select(store => store.StoreCode)
                .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
