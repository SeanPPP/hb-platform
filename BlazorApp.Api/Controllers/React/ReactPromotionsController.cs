using System;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/promotions")]
    [Authorize]
    public class ReactPromotionsController : ControllerBase
    {
        private readonly IPromotionReactService _service;

        public ReactPromotionsController(IPromotionReactService service)
        {
            _service = service;
        }

        [HttpPost("grid")]
        public async Task<ActionResult<GridResponseDto<PromotionListDto>>> Grid(
            [FromBody] GridRequestDto request
        )
        {
            var res = await _service.GetGridAsync(request);
            return Ok(res);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<PromotionDetailDto>>> Get(string id)
        {
            var res = await _service.GetByIdAsync(id);
            return Ok(res);
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<PromotionDetailDto>>> Create(
            [FromBody] CreatePromotionDto dto
        )
        {
            var res = await _service.CreateAsync(dto);
            return Ok(res);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<PromotionDetailDto>>> Update(
            string id,
            [FromBody] UpdatePromotionDto dto
        )
        {
            var res = await _service.UpdateAsync(id, dto);
            return Ok(res);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(string id)
        {
            var res = await _service.DeleteAsync(id);
            return Ok(res);
        }

        [HttpPost("{id}/enable")]
        public async Task<ActionResult<ApiResponse<bool>>> Enable(
            string id,
            [FromQuery] bool enable = true
        )
        {
            var res = await _service.EnableAsync(id, enable);
            return Ok(res);
        }

        [HttpPost("evaluate")]
        public async Task<ActionResult<ApiResponse<PromotionEvaluateResponse>>> Evaluate([FromBody] PromotionEvaluateRequest req)
        {
            var res = await _service.EvaluateAsync(req);
            return Ok(res);
        }

        [HttpGet("valid")]
        public async Task<ActionResult<ApiResponse<System.Collections.Generic.List<PromotionListDto>>>> GetValidByStore([FromQuery] string storeCode, [FromQuery] DateTime? asOf)
        {
            var res = await _service.GetValidByStoreAsync(storeCode, asOf);
            return Ok(res);
        }

        [HttpGet("valid/by-product")]
        public async Task<ActionResult<ApiResponse<System.Collections.Generic.List<PromotionListDto>>>> GetValidByProduct([FromQuery] string productCode, [FromQuery] string storeCode, [FromQuery] DateTime? asOf)
        {
            var res = await _service.GetValidByProductAndStoreAsync(productCode, storeCode, asOf);
            return Ok(res);
        }
    }
}
