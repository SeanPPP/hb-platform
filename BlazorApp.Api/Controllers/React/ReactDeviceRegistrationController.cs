using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/device-registration")]
    [Authorize]
    public class ReactDeviceRegistrationController : ControllerBase
    {
        private readonly IDeviceRegistrationReactService _service;

        public ReactDeviceRegistrationController(IDeviceRegistrationReactService service)
        {
            _service = service;
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
                        data = new { items = result.Items, total = result.Total },
                        message = result.Message,
                    }
                );
            return Ok(
                new
                {
                    success = false,
                    data = new
                    {
                        items = result.Items
                            ?? new System.Collections.Generic.List<DeviceRegistrationListDto>(),
                        total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var result = await _service.GetByIdAsync(id);
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

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateDeviceRegistrationDto dto)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpdateAsync(id, dto, user);
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
