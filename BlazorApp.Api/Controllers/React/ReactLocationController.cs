using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/locations")]
    [Authorize]
    public class ReactLocationController : ControllerBase
    {
        private readonly ILocationReactService _locationService;
        private readonly ILogger<ReactLocationController> _logger;

        public ReactLocationController(
            ILocationReactService locationService,
            ILogger<ReactLocationController> logger
        )
        {
            _locationService = locationService;
            _logger = logger;
        }

        [HttpPost("list")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetList([FromBody] LocationReactFilterDto filter)
        {
            try
            {
                if (filter == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                _logger.LogInformation(
                    "获取货位列表: Page={Page}, PageSize={PageSize}, LocationType={LocationType}, IsUsed={IsUsed}",
                    filter.PageNumber,
                    filter.PageSize,
                    filter.LocationType,
                    filter.IsUsed
                );

                var result = await _locationService.GetPagedListAsync(filter);

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        items = result.Items,
                        total = result.Total,
                        pageNumber = result.PageNumber,
                        pageSize = result.PageSize,
                    },
                    message = "获取货位列表成功",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货位列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("{locationGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetById(string locationGuid)
        {
            try
            {
                var result = await _locationService.GetByIdAsync(locationGuid);
                if (!result.Success)
                {
                    return NotFound(new { success = false, message = result.Message });
                }
                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货位详情失败: {LocationGuid}", locationGuid);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Create([FromBody] CreateLocationReactDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                var result = await _locationService.CreateAsync(dto);
                if (!result.Success)
                {
                    return BadRequest(new { success = false, message = result.Message });
                }
                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建货位失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPut("{locationGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Update(string locationGuid, [FromBody] UpdateLocationReactDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                var result = await _locationService.UpdateAsync(locationGuid, dto);
                if (!result.Success)
                {
                    if (result.ErrorCode == "NOT_FOUND")
                        return NotFound(new { success = false, message = result.Message });
                    return BadRequest(new { success = false, message = result.Message });
                }
                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新货位失败: {LocationGuid}", locationGuid);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("{locationGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Delete(string locationGuid)
        {
            try
            {
                var result = await _locationService.DeleteAsync(locationGuid);
                if (!result.Success)
                {
                    if (result.ErrorCode == "NOT_FOUND")
                        return NotFound(new { success = false, message = result.Message });
                    return BadRequest(new { success = false, message = result.Message });
                }
                return Ok(new { success = true, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除货位失败: {LocationGuid}", locationGuid);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }
}
