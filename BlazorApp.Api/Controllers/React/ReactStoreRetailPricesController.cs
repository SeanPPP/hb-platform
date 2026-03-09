using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-retail-prices")]
    [Authorize]
    public class ReactStoreRetailPricesController : ControllerBase
    {
        private readonly IStoreRetailPriceReactService _service;
        private readonly ILogger<ReactStoreRetailPricesController> _logger;

        public ReactStoreRetailPricesController(
            IStoreRetailPriceReactService service,
            ILogger<ReactStoreRetailPricesController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost("grid")]

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
                        Items = result.Items ?? new List<StoreRetailPriceListDto>(),
                        Total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpGet("{uuid}")]

        public async Task<IActionResult> GetByUuid(string uuid)
        {
            var result = await _service.GetByUuidAsync(uuid);
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

        [HttpPost]

        public async Task<IActionResult> Create([FromBody] CreateStoreRetailPriceDto dto)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.CreateAsync(dto, user);
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

        [HttpPut("{uuid}")]

        public async Task<IActionResult> Update(
            string uuid,
            [FromBody] UpdateStoreRetailPriceDto dto
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpdateAsync(uuid, dto, user);
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

        [HttpDelete("{uuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Delete(string uuid)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.DeleteAsync(uuid, user);
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

        [HttpPost("batch-upsert")]

        public async Task<IActionResult> BatchUpsert(
            [FromBody] List<StoreRetailPriceUpsertItemDto> items
        )
        {
            var requestId = System.Guid.NewGuid().ToString("N");
            var user = User.Identity?.Name ?? "system";

            _logger.LogInformation(
                $"[{requestId}] BatchUpsert 开始, 用户: {user}, 请求数据: {JsonSerializer.Serialize(items)}"
            );

            if (items == null || items.Count == 0)
            {
                _logger.LogWarning($"[{requestId}] BatchUpsert 失败: 请求数据为空");
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "请求数据不能为空",
                        errors = new[] { "items 参数不能为空" },
                    }
                );
            }

            if (!ModelState.IsValid)
            {
                var modelErrors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .SelectMany(x => x.Value!.Errors.Select(e => $"{x.Key}: {e.ErrorMessage}"))
                    .ToList();
                _logger.LogWarning(
                    $"[{requestId}] BatchUpsert 失败: ModelState 验证失败 - {JsonSerializer.Serialize(modelErrors)}"
                );
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "数据验证失败",
                        errors = modelErrors,
                    }
                );
            }

            try
            {
                var result = await _service.BatchUpsertAsync(items, user);

                if (result.Success)
                {
                    _logger.LogInformation(
                        $"[{requestId}] BatchUpsert 成功: 插入{result.Data?.Inserted}条, 更新{result.Data?.Updated}条, 失败{result.Data?.Failed}条"
                    );
                    return Ok(
                        new
                        {
                            success = true,
                            data = result.Data,
                            message = result.Message,
                        }
                    );
                }
                else
                {
                    var errors = new List<string> { result.Message ?? "未知错误" };
                    if (result.Data?.Errors != null && result.Data.Errors.Any())
                    {
                        errors.AddRange(result.Data.Errors);
                    }
                    _logger.LogWarning(
                        $"[{requestId}] BatchUpsert 失败: {result.Message} - {JsonSerializer.Serialize(errors)}"
                    );
                    return BadRequest(new { success = false, message = result.Message, errors });
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"[{requestId}] BatchUpsert 异常: {ex.Message}");
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "服务器内部错误",
                        errors = new[] { ex.Message },
                    }
                );
            }
        }

        [HttpPost("upsert-active-stores")]

        public async Task<IActionResult> UpsertForActiveStores(
            [FromBody] List<StoreRetailPriceUpsertForActiveStoresItemDto> items
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpsertForActiveStoresAsync(items, user);
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

        [HttpDelete("batch-delete")]

        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDelete([FromBody] List<string> uuids)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchDeleteAsync(uuids, user);
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

        [HttpPut("batch-special")]

        public async Task<IActionResult> BatchSpecial([FromBody] BatchUpdateSpecialRequestDto dto)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchUpdateSpecialFlagAsync(
                dto.ProductCodes,
                dto.IsSpecial,
                user
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

        [HttpPost("batch-by-uuids")]

        public async Task<IActionResult> BatchByUuids([FromBody] List<string> uuids)
        {
            var result = await _service.GetListByUuidsAsync(uuids);
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
