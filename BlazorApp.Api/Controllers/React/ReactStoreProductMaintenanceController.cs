using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-product-maintenance")]
    [AllowAnonymous]
    public class ReactStoreProductMaintenanceController : ControllerBase
    {
        private readonly IStoreProductMaintenanceReactService _service;
        private readonly IDeviceRegistrationService _deviceRegistrationService;
        private readonly IMapper _mapper;
        private readonly ISqlSugarClient _db;
        private readonly ILogger<ReactStoreProductMaintenanceController> _logger;

        public ReactStoreProductMaintenanceController(
            IStoreProductMaintenanceReactService service,
            IDeviceRegistrationService deviceRegistrationService,
            IMapper mapper,
            SqlSugarContext context,
            ILogger<ReactStoreProductMaintenanceController> logger
        )
        {
            _service = service;
            _deviceRegistrationService = deviceRegistrationService;
            _mapper = mapper;
            _db = context.Db;
            _logger = logger;
        }

        [HttpPost("lookup")]
        public async Task<IActionResult> Lookup([FromBody] StoreProductLookupRequestDto request)
        {
            var access = await ResolveAccessContextAsync();
            Console.WriteLine(
                $"[StoreProductMaintenance][Lookup] keyword='{request.Keyword}', requestedStore='{request.StoreCode}', allowed={access.IsAllowed}, scope={FormatStoreScope(access.StoreCodes)}, actor='{access.ActorLabel}'"
            );
            if (!access.IsAllowed)
            {
                Console.WriteLine(
                    $"[StoreProductMaintenance][Lookup] unauthorized: {access.Message}"
                );
                return Unauthorized(ApiResponse<List<StoreProductLookupItemDto>>.Error(access.Message));
            }

            var result = await _service.LookupAsync(request, access.StoreCodes);
            return Ok(result);
        }

        [HttpGet("{productCode}")]
        public async Task<IActionResult> GetDetail(string productCode, [FromQuery] string? storeCode = null)
        {
            var access = await ResolveAccessContextAsync();
            Console.WriteLine(
                $"[StoreProductMaintenance][Detail] productCode='{productCode}', requestedStore='{storeCode}', allowed={access.IsAllowed}, scope={FormatStoreScope(access.StoreCodes)}, actor='{access.ActorLabel}'"
            );
            if (!access.IsAllowed)
            {
                Console.WriteLine(
                    $"[StoreProductMaintenance][Detail] unauthorized: {access.Message}"
                );
                return Unauthorized(ApiResponse<StoreProductDetailDto>.Error(access.Message));
            }

            var result = await _service.GetDetailAsync(productCode, storeCode, access.StoreCodes);
            return Ok(result);
        }

        [HttpPost("evaluate-auto-pricing")]
        public async Task<IActionResult> EvaluateAutoPricing(
            [FromBody] EvaluateStoreProductAutoPricingDto request
        )
        {
            var access = await ResolveAccessContextAsync();
            Console.WriteLine(
                $"[StoreProductMaintenance][EvaluateAutoPricing] productCode='{request.ProductCode}', requestedStore='{request.StoreCode}', forceAuto={request.ForceAutoPricing}, allowed={access.IsAllowed}, scope={FormatStoreScope(access.StoreCodes)}, actor='{access.ActorLabel}'"
            );
            if (!access.IsAllowed)
            {
                Console.WriteLine(
                    $"[StoreProductMaintenance][EvaluateAutoPricing] unauthorized: {access.Message}"
                );
                return Unauthorized(ApiResponse<EvaluateStoreProductAutoPricingResultDto>.Error(access.Message));
            }

            var result = await _service.EvaluateAutoPricingAsync(request, access.StoreCodes);
            return Ok(result);
        }

        [HttpPut("store-prices/{uuid}")]
        public async Task<IActionResult> UpdateStorePrice(
            string uuid,
            [FromBody] UpdateStoreProductPriceDto request
        )
        {
            var access = await ResolveAccessContextAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(ApiResponse<StoreProductStorePriceDto>.Error(access.Message));
            }

            var result = await _service.UpdateStorePriceAsync(
                uuid,
                request,
                access.ActorLabel,
                access.StoreCodes
            );
            return Ok(result);
        }

        [HttpPut("products/{productCode}/type")]
        public async Task<IActionResult> UpdateProductType(
            string productCode,
            [FromBody] UpdateStoreProductTypeDto request
        )
        {
            var access = await ResolveAccessContextAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(ApiResponse<StoreProductTypeUpdateResultDto>.Error(access.Message));
            }

            var result = await _service.UpdateProductTypeAsync(
                productCode,
                request,
                access.ActorLabel,
                access.StoreCodes
            );
            return Ok(result);
        }

        [HttpPut("multi-codes/{uuid}")]
        public async Task<IActionResult> UpdateMultiCode(
            string uuid,
            [FromBody] UpdateStoreProductMultiCodeDto request
        )
        {
            var access = await ResolveAccessContextAsync();
            if (!access.IsAllowed)
            {
                return Unauthorized(ApiResponse<StoreProductMultiCodeDto>.Error(access.Message));
            }

            var result = await _service.UpdateMultiCodeAsync(
                uuid,
                request,
                access.ActorLabel,
                access.StoreCodes
            );
            return Ok(result);
        }

        private async Task<StoreAccessContext> ResolveAccessContextAsync()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                try
                {
                    if (HasElevatedStoreAccess())
                    {
                        return new StoreAccessContext
                        {
                            IsAllowed = true,
                            ActorLabel = User.Identity?.Name ?? "system",
                            StoreCodes = null,
                        };
                    }

                    var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var actorLabel = User.Identity?.Name ?? "system";
                    if (string.IsNullOrWhiteSpace(userGuid) && !string.IsNullOrWhiteSpace(actorLabel))
                    {
                        userGuid = await _db.Queryable<User>()
                            .Where(u => u.Username == actorLabel && !u.IsDeleted)
                            .Select(u => u.UserGUID)
                            .FirstAsync();
                    }

                    if (string.IsNullOrWhiteSpace(userGuid))
                    {
                        return new StoreAccessContext
                        {
                            IsAllowed = false,
                            Message = "未找到当前用户信息",
                        };
                    }

                    var storeCodes = await _db.Queryable<UserStore>()
                        .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                        .Where((us, s) => us.UserGUID == userGuid && !us.IsDeleted && !s.IsDeleted)
                        .Select((us, s) => s.StoreCode)
                        .ToListAsync();

                    return new StoreAccessContext
                    {
                        IsAllowed = true,
                        ActorLabel = actorLabel,
                        StoreCodes = storeCodes,
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "解析登录用户可访问分店失败");
                    return new StoreAccessContext
                    {
                        IsAllowed = false,
                        Message = "解析当前用户分店权限失败",
                    };
                }
            }

            var hardwareId = Request.Headers["X-Device-Id"].FirstOrDefault();
            var authCode = Request.Headers["X-Auth-Code"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(hardwareId) || string.IsNullOrWhiteSpace(authCode))
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "未登录且缺少设备授权信息",
                };
            }

            var isValid = await _deviceRegistrationService.ValidateDeviceAuthCodeAsync(
                hardwareId,
                authCode
            );
            if (!isValid)
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "设备授权无效",
                };
            }

            var deviceEntity = await _deviceRegistrationService.GetDeviceByHardwareIdAsync(hardwareId);
            if (deviceEntity == null)
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "设备不存在",
                };
            }

            var device = _mapper.Map<DeviceDataDto>(deviceEntity);
            if (device.Status != 1 || string.IsNullOrWhiteSpace(device.StoreCode))
            {
                return new StoreAccessContext
                {
                    IsAllowed = false,
                    Message = "设备未启用或未绑定分店",
                };
            }

            return new StoreAccessContext
            {
                IsAllowed = true,
                ActorLabel = $"device:{device.HardwareId}",
                StoreCodes = new List<string> { device.StoreCode },
            };
        }

        private bool HasElevatedStoreAccess()
        {
            return HasRole("Admin")
                || HasRole("Manager")
                || HasRole("WarehouseManager")
                || HasRole("WarehouseStaff");
        }

        private bool HasRole(string role)
        {
            return User?.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role
                && claim.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            ) == true;
        }

        private static string FormatStoreScope(List<string>? storeCodes)
        {
            if (storeCodes == null)
            {
                return "ALL";
            }

            return storeCodes.Count == 0 ? "NONE" : string.Join(",", storeCodes);
        }

        private sealed class StoreAccessContext
        {
            public bool IsAllowed { get; set; }
            public string Message { get; set; } = "未授权";
            public string ActorLabel { get; set; } = "system";
            public List<string>? StoreCodes { get; set; }
        }
    }
}
