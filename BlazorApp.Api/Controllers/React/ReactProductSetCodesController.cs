using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/product-set-codes")]
    [Authorize]
    public class ReactProductSetCodesController : ControllerBase
    {
        private readonly IProductSetCodeReactService _service;
        private readonly ILogger<ReactProductSetCodesController> _logger;

        public ReactProductSetCodesController(
            IProductSetCodeReactService service,
            ILogger<ReactProductSetCodesController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost("grid")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetGrid([FromBody] GridRequestDto request)
        {
            try
            {
                var result = await _service.GetGridDataAsync(request);
                if (result.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = new { items = result.Items, total = result.Total },
                            message = result.Message,
                        }
                    );
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取多码套装商品网格数据失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPut("batch-status")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchStatus([FromBody] BatchUpdateStatusDto dto)
        {
            try
            {
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchUpdateStatusAsync(
                    dto.Ids,
                    dto.IsActive,
                    updatedBy
                );
                if (result.Success)
                {
                    return Ok(new { success = true, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新状态失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPut("batch-prices")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchPrices([FromBody] BatchUpdatePricesDto dto)
        {
            try
            {
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchUpdatePricesAsync(dto.Items, updatedBy);
                if (result.Success)
                {
                    return Ok(new { success = true, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新价格失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("batch-delete")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteSetCodesRequestDto dto)
        {
            try
            {
                if (dto.Ids == null || !dto.Ids.Any())
                {
                    return BadRequest(new { success = false, message = "删除列表不能为空" });
                }
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchDeleteAsync(dto.Ids, updatedBy);
                if (result.Success)
                {
                    return Ok(new { success = true, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPut("batch-barcodes")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchBarcodes([FromBody] BatchUpdateBarcodesDto dto)
        {
            try
            {
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchUpdateBarcodesAsync(dto.Items, updatedBy);
                if (result.Success)
                {
                    return Ok(new { success = true, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新条码失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("batch-create")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchCreate([FromBody] BatchCreateSetCodesDto dto)
        {
            try
            {
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchCreateAsync(dto.Items, updatedBy);
                if (result.Success)
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = new { createdIds = result.Data },
                            message = result.Message,
                        }
                    );
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建套装多码失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("batch-create-with-store-sync")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchCreateWithStoreSync(
            [FromBody] BatchCreateSetCodeWithStoreSyncDto dto
        )
        {
            try
            {
                if (dto.Items == null || !dto.Items.Any())
                {
                    return BadRequest(new { success = false, message = "创建列表不能为空" });
                }
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchCreateWithStoreSyncAsync(dto.Items, updatedBy);
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量创建并同步到分店失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("batch-delete-with-store-sync")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BatchDeleteWithStoreSync(
            [FromBody] BatchDeleteSetCodeWithStoreSyncDto dto
        )
        {
            try
            {
                if (dto.Ids == null || !dto.Ids.Any())
                {
                    return BadRequest(new { success = false, message = "删除列表不能为空" });
                }
                var updatedBy = User.Identity?.Name ?? "system";
                var result = await _service.BatchDeleteWithStoreSyncAsync(
                    dto.Ids,
                    dto.StoreCodes,
                    updatedBy
                );
                if (result.Success)
                {
                    return Ok(new { success = true, data = result.Data, message = result.Message });
                }
                return BadRequest(new { success = false, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除并同步到分店失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }
}
