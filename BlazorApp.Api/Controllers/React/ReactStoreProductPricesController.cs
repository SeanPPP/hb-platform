using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
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
        private readonly ILogger<ReactStoreProductPricesController> _logger;

        public ReactStoreProductPricesController(
            IStoreProductPriceReactService service,
            IStoreRetailPriceReactService retailPriceService,
            ILogger<ReactStoreProductPricesController> logger
        )
        {
            _service = service;
            _retailPriceService = retailPriceService;
            _logger = logger;
        }

        [HttpPost("grid")]
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
        public async Task<IActionResult> SyncToOtherStores([FromBody] SyncToOtherStoresDto dto)
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.SyncToOtherStoresAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("copy-store-data")]
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
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> SyncFromHq([FromBody] SyncRetailPriceFromHqRequest request)
        {
            var result = await _retailPriceService.SyncFromHqAsync(
                request.SelectedStoreCodes,
                request.StartDate
            );
            return Ok(result);
        }
    }
}
