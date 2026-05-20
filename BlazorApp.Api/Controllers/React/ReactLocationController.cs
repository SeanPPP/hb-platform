using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/locations")]
    [Authorize]
    public class ReactLocationController : ControllerBase
    {
        private readonly ILocationReactService _locationService;
        private readonly ILogger<ReactLocationController> _logger;
        private readonly IDeviceRegistrationService _deviceRegistrationService;

        public ReactLocationController(
            ILocationReactService locationService,
            ILogger<ReactLocationController> logger,
            IDeviceRegistrationService deviceRegistrationService
        )
        {
            _locationService = locationService;
            _logger = logger;
            _deviceRegistrationService = deviceRegistrationService;
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
        [AllowAnonymous]
        public async Task<IActionResult> GetById(string locationGuid)
        {
            var access = await ResolveReadAccessAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(new { success = false, message = access.Message });
            }

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

        [HttpGet("lookup")]
        [AllowAnonymous]
        public async Task<IActionResult> Lookup([FromQuery] string keyword)
        {
            var access = await ResolveReadAccessAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(new { success = false, message = access.Message });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return BadRequest(new { success = false, message = "查询关键字不能为空" });
                }

                var result = await _locationService.LookupAsync(keyword);
                return Ok(new { success = true, data = result, message = "获取成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查找货位失败: {Keyword}", keyword);
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
                    return BadRequest(ToErrorBody(result));
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
                        return NotFound(ToErrorBody(result));
                    return BadRequest(ToErrorBody(result));
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
                        return NotFound(ToErrorBody(result));
                    return BadRequest(ToErrorBody(result));
                }
                return Ok(new { success = true, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除货位失败: {LocationGuid}", locationGuid);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("{locationGuid}/products/{productCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BindProduct(string locationGuid, string productCode)
        {
            try
            {
                var result = await _locationService.BindProductAsync(locationGuid, productCode);
                if (!result.Success)
                {
                    if (result.ErrorCode == "NOT_FOUND")
                    {
                        return NotFound(ToErrorBody(result));
                    }

                    return BadRequest(ToErrorBody(result));
                }

                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "绑定货位商品失败: {LocationGuid} {ProductCode}", locationGuid, productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpGet("products/resolve")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> ResolveProduct([FromQuery] string keyword)
        {
            try
            {
                var result = await _locationService.ResolveProductAsync(keyword);
                if (!result.Success)
                {
                    if (result.ErrorCode == "NOT_FOUND")
                    {
                        return NotFound(ToErrorBody(result));
                    }

                    return BadRequest(ToErrorBody(result));
                }

                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析货位绑定商品失败: {Keyword}", keyword);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpPost("{locationGuid}/products/bind")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BindProductByIdentifier(
            string locationGuid,
            [FromBody] BindLocationProductReactDto dto
        )
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                var productIdentifier = dto.ProductIdentifier
                    ?? dto.ProductCode
                    ?? dto.ItemNumber
                    ?? dto.Barcode;

                var result = await _locationService.BindProductAsync(locationGuid, productIdentifier ?? string.Empty);
                if (!result.Success)
                {
                    if (result.ErrorCode == "NOT_FOUND")
                    {
                        return NotFound(ToErrorBody(result));
                    }

                    return BadRequest(ToErrorBody(result));
                }

                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "绑定货位商品失败: {LocationGuid}", locationGuid);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        [HttpDelete("{locationGuid}/products/{productCode}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UnbindProduct(string locationGuid, string productCode)
        {
            try
            {
                var result = await _locationService.UnbindProductAsync(locationGuid, productCode);
                if (!result.Success)
                {
                    if (result.ErrorCode == "NOT_FOUND")
                    {
                        return NotFound(ToErrorBody(result));
                    }

                    return BadRequest(ToErrorBody(result));
                }

                return Ok(new { success = true, data = result.Data, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解绑货位商品失败: {LocationGuid} {ProductCode}", locationGuid, productCode);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        private async Task<LocationReadAccessContext> ResolveReadAccessAsync()
        {
            if (User?.Identity?.IsAuthenticated == true
                && HasAnyRole("Admin", "WarehouseManager", "WarehouseStaff"))
            {
                return LocationReadAccessContext.Allow();
            }

            var deviceId = Request.Headers["X-Device-Id"].FirstOrDefault();
            var authCode = Request.Headers["X-Auth-Code"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(authCode))
            {
                return LocationReadAccessContext.Deny("缺少仓库访问凭证");
            }

            var isValid = await _deviceRegistrationService.ValidateDeviceAuthCodeAsync(deviceId, authCode);
            if (!isValid)
            {
                return LocationReadAccessContext.Deny("设备授权无效");
            }

            var deviceEntity = await _deviceRegistrationService.GetDeviceByHardwareIdAsync(deviceId);
            if (deviceEntity == null)
            {
                return LocationReadAccessContext.Deny("设备不存在");
            }

            return deviceEntity.设备状态 == 1
                ? LocationReadAccessContext.Allow()
                : LocationReadAccessContext.Deny("设备未启用");
        }

        private bool HasAnyRole(params string[] roles)
        {
            return roles.Any(role =>
                User.IsInRole(role)
                || User.Claims.Any(c =>
                    c.Type == ClaimTypes.Role && string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase))
            );
        }

        private static object ToErrorBody<T>(ApiResponse<T> result)
        {
            return new
            {
                success = false,
                message = result.Message,
                errorCode = result.ErrorCode,
                details = result.Details,
            };
        }

        private sealed record LocationReadAccessContext(bool IsAllowed, string? Message)
        {
            public static LocationReadAccessContext Allow() => new(true, null);

            public static LocationReadAccessContext Deny(string message) => new(false, message);
        }
    }
}
