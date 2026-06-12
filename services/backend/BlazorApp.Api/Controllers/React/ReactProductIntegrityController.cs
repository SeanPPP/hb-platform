using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/product-integrity")]
    [Authorize(Roles = "Admin")]
    public class ReactProductIntegrityController : ControllerBase
    {
        private readonly IProductIntegrityService _integrityService;
        private readonly ILogger<ReactProductIntegrityController> _logger;

        public ReactProductIntegrityController(
            IProductIntegrityService integrityService,
            ILogger<ReactProductIntegrityController> logger
        )
        {
            _integrityService = integrityService;
            _logger = logger;
        }

        [HttpPost("check")]
        public async Task<IActionResult> CheckIntegrity(
            [FromBody] List<string>? storeCodes = null
        )
        {
            try
            {
                _logger.LogInformation("开始检测商品数据一致性");
                var result = await _integrityService.CheckIntegrityAsync(storeCodes);
                if (result.Success)
                {
                    return Ok(result);
                }
                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测商品数据一致性异常");
                return StatusCode(
                    500,
                    ApiResponse<ProductIntegrityCheckResultDto>.Error(
                        "检测异常",
                        "INTERNAL_ERROR"
                    )
                );
            }
        }

        [HttpPost("fix")]
        public async Task<IActionResult> FixIntegrity(
            [FromBody] ProductIntegrityFixRequestDto request
        )
        {
            try
            {
                _logger.LogInformation(
                    "开始修复商品数据一致性（DryRun={DryRun}）",
                    request.DryRun
                );
                var result = await _integrityService.FixIntegrityAsync(request);
                if (result.Success)
                {
                    return Ok(result);
                }
                return BadRequest(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "修复商品数据一致性异常");
                return StatusCode(
                    500,
                    ApiResponse<ProductIntegrityFixResultDto>.Error(
                        "修复异常",
                        "INTERNAL_ERROR"
                    )
                );
            }
        }
    }
}
