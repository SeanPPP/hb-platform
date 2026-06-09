using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/scheduled-task/runtime-control")]
    public class ScheduledTaskRuntimeControlController : ControllerBase
    {
        private readonly ScheduledTaskRuntimeControlService _service;

        public ScheduledTaskRuntimeControlController(ScheduledTaskRuntimeControlService service)
        {
            _service = service;
        }

        [HttpGet]
        [Authorize(Policy = Permissions.System.ViewLogs)]
        public async Task<ActionResult<ApiResponse<ScheduledTaskRuntimeControlStatusDto>>> GetStatus()
        {
            var status = await _service.GetStatusAsync();
            return Ok(ApiResponse<ScheduledTaskRuntimeControlStatusDto>.OK(status, "查询成功"));
        }

        [HttpPost]
        [Authorize(Policy = Permissions.System.ManageScheduledTasks)]
        public async Task<ActionResult<ApiResponse<ScheduledTaskRuntimeControlStatusDto>>> Update(
            [FromBody] ScheduledTaskRuntimeControlUpdateDto request
        )
        {
            var status = await _service.UpdateControlAsync(request, User.Identity?.Name);
            return Ok(ApiResponse<ScheduledTaskRuntimeControlStatusDto>.OK(status, "更新成功"));
        }
    }
}
