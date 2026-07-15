using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/EmployeeProfiles")]
    [Route("api/employee-profiles")]
    [Authorize]
    public class EmployeeProfilesController : ControllerBase
    {
        private readonly IEmployeeProfileService _service;
        private readonly ILogger<EmployeeProfilesController> _logger;
        private readonly EmployeeProfileMediaService _mediaService;
        private readonly EmployeeCashierBarcodeService _barcodeService;

        public EmployeeProfilesController(
            IEmployeeProfileService service,
            ILogger<EmployeeProfilesController> logger,
            EmployeeProfileMediaService mediaService,
            EmployeeCashierBarcodeService barcodeService
        )
        {
            _service = service;
            _logger = logger;
            _mediaService = mediaService;
            _barcodeService = barcodeService;
        }

        [HttpGet("admin")]
        [Authorize(Roles = "Admin,管理员")]
        [Authorize(Policy = Permissions.EmployeeProfiles.View)]
        public async Task<IActionResult> GetAdminList([FromQuery] EmployeeProfileQueryDto query)
        {
            try
            {
                return Ok(await _service.GetAdminListAsync(query));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取员工个人信息列表失败");
                return StatusCode(
                    500,
                    ApiResponse<PagedResult<EmployeeProfileListItemDto>>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpGet("admin/{userGuid}")]
        [Authorize(Roles = "Admin,管理员")]
        [Authorize(Policy = Permissions.EmployeeProfiles.View)]
        public async Task<IActionResult> GetAdminDetail(string userGuid)
        {
            try
            {
                return Ok(await _service.GetAdminDetailAsync(userGuid));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取员工个人信息详情失败，UserGUID: {UserGUID}", userGuid);
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPut("admin/{userGuid}")]
        [Authorize(Roles = "Admin,管理员")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> UpsertAdmin(string userGuid, [FromBody] EmployeeProfileUpsertDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<EmployeeProfileDetailDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                return Ok(await _service.UpsertAdminAsync(userGuid, dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存员工个人信息失败，UserGUID: {UserGUID}", userGuid);
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpGet("me")]
        [Authorize(Policy = Permissions.EmployeeProfiles.View)]
        public async Task<IActionResult> GetSelf()
        {
            try
            {
                return Ok(await _service.GetSelfAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取当前员工个人信息失败");
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPut("me")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> UpsertSelf([FromBody] EmployeeProfileUpsertDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(
                        ApiResponse<EmployeeProfileDetailDto>.Error(
                            "请求参数验证失败",
                            "VALIDATION_ERROR",
                            ModelState
                        )
                    );
                }

                return Ok(await _service.UpsertSelfAsync(dto));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存当前员工个人信息失败");
                return StatusCode(
                    500,
                    ApiResponse<EmployeeProfileDetailDto>.Error(
                        "服务器内部错误",
                        "INTERNAL_SERVER_ERROR"
                    )
                );
            }
        }

        [HttpPost("me/image-upload-signature")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> CreateImageUploadSignature(
            [FromBody] EmployeeImageUploadSignatureRequest request
        )
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Error("请求参数验证失败", "VALIDATION_ERROR", ModelState));
            }
            return Ok(await _mediaService.CreateUploadSignatureAsync(request));
        }

        [HttpPost("me/images/complete")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> CompleteImage(
            [FromBody] EmployeeImageCompleteRequest request,
            CancellationToken cancellationToken
        )
        {
            var result = await _mediaService.CompleteAsync(request, cancellationToken);
            return result.Success ? Ok(await _service.GetSelfAsync()) : Ok(result);
        }

        [HttpDelete("me/images/{kind}")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> DeleteImage(string kind, CancellationToken cancellationToken)
        {
            var result = await _mediaService.DeleteAsync(kind, cancellationToken);
            return result.Success ? Ok(await _service.GetSelfAsync()) : Ok(result);
        }

        [HttpGet("me/cashier-barcode")]
        [Authorize(Policy = Permissions.EmployeeProfiles.View)]
        public async Task<IActionResult> GetCashierBarcode() => Ok(await _barcodeService.GetAsync());

        [HttpPost("me/cashier-barcode/refresh")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> RefreshCashierBarcode() =>
            Ok(await _barcodeService.RefreshAsync());

        [HttpPost("me/cashier-barcode/print-confirmation")]
        [Authorize(Policy = Permissions.EmployeeProfiles.Edit)]
        public async Task<IActionResult> ConfirmCashierBarcodePrint(
            [FromBody] EmployeeCashierBarcodePrintConfirmationRequest request
        ) => Ok(await _barcodeService.ConfirmPrintAsync(request));
    }
}
