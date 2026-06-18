using System.Security.Claims;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React 货柜商品创建控制器。
    /// </summary>
    [ApiController]
    [Route("api/react/v1/container-products")]
    [Authorize]
    public class ReactContainerProductsController : ControllerBase
    {
        private readonly IContainerProductCreationJobService _jobService;
        private readonly ILogger<ReactContainerProductsController> _logger;

        public ReactContainerProductsController(
            IContainerProductCreationJobService jobService,
            ILogger<ReactContainerProductsController> logger
        )
        {
            _jobService = jobService;
            _logger = logger;
        }

        /// <summary>
        /// 创建货柜明细新商品后台 job。
        /// </summary>
        [HttpPost("create-new-products/jobs")]
        [Authorize(Policy = Permissions.Container.Edit)]
        [Authorize(Policy = Permissions.PosProducts.Manage)]
        public async Task<IActionResult> StartCreateNewProductsJob(
            [FromBody] ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken
        )
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                if (string.IsNullOrWhiteSpace(request.OperationId))
                {
                    return BadRequest(new { success = false, message = "operationId 不能为空" });
                }

                if (string.IsNullOrWhiteSpace(request.ContainerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜 GUID 不能为空" });
                }

                if (request.DetailHguids == null || request.DetailHguids.Count == 0)
                {
                    return BadRequest(new { success = false, message = "明细 GUID 不能为空" });
                }

                var userId = ResolveUserId();
                var job = await _jobService.StartJobAsync(userId, request, cancellationToken);
                return Ok(new { success = true, data = job, message = "创建新商品 job 已提交" });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交货柜创建新商品 job 失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 提交当前货柜全部明细后台 job。
        /// </summary>
        [HttpPost("submit-container/jobs")]
        [Authorize(Policy = Permissions.Container.Edit)]
        [Authorize(Policy = Permissions.PosProducts.Manage)]
        public async Task<IActionResult> StartSubmitContainerJob(
            [FromBody] ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken
        )
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                if (string.IsNullOrWhiteSpace(request.OperationId))
                {
                    return BadRequest(new { success = false, message = "operationId 不能为空" });
                }

                if (string.IsNullOrWhiteSpace(request.ContainerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜 GUID 不能为空" });
                }

                request.SubmitContainer = true;
                request.DetailHguids = new List<string>();

                var userId = ResolveUserId();
                var job = await _jobService.StartJobAsync(userId, request, cancellationToken);
                return Ok(new { success = true, data = job, message = "提交货柜 job 已提交" });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交货柜 job 失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 查询货柜明细创建新商品 job。
        /// </summary>
        [HttpGet("create-new-products/jobs/{jobId}")]
        [Authorize(Policy = Permissions.Container.Edit)]
        [Authorize(Policy = Permissions.PosProducts.Manage)]
        public async Task<IActionResult> GetCreateNewProductsJob(
            string jobId,
            CancellationToken cancellationToken
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(jobId))
                {
                    return BadRequest(new { success = false, message = "jobId 不能为空" });
                }

                var userId = ResolveUserId();
                var job = await _jobService.GetJobAsync(userId, jobId, cancellationToken);
                if (job == null)
                {
                    return NotFound(new { success = false, message = "创建新商品 job 不存在或已过期" });
                }

                return Ok(new { success = true, data = job, message = "查询成功" });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询货柜创建新商品 job 失败: {JobId}", jobId);
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        private string ResolveUserId()
        {
            return User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("userId")
                ?? User.FindFirstValue("userGuid")
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? User.FindFirstValue("sub")
                ?? "anonymous";
        }
    }
}
