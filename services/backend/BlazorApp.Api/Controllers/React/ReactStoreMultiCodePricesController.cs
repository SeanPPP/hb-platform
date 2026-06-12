using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-multi-code-prices")]
    [Authorize]
    public class ReactStoreMultiCodePricesController : ControllerBase
    {
        private readonly IStoreMultiCodePricesReactService _service;

        public ReactStoreMultiCodePricesController(IStoreMultiCodePricesReactService service)
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
                        data = new { Items = result.Items, Total = result.Total },
                        message = result.Message,
                    }
                );
            return Ok(
                new
                {
                    success = false,
                    data = new
                    {
                        Items = result.Items ?? new List<StoreMultiCodePriceListDto>(),
                        Total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpPost("batch-upsert")]

        public async Task<IActionResult> BatchUpsert(
            [FromBody] List<StoreMultiCodePriceUpsertItemDto> items
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchUpsertAsync(items, user);
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

        [HttpPost("upsert-active-stores")]

        public async Task<IActionResult> UpsertForActiveStores(
            [FromBody] List<StoreMultiCodePriceUpsertForActiveStoresItemDto> items
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpsertForActiveStoresAsync(items, user);
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

        [HttpPut("batch-special")]

        public async Task<IActionResult> BatchSpecial([FromBody] BatchUpdateSpecialRequestDtoMC dto)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchUpdateSpecialFlagAsync(
                dto.ProductCodes,
                dto.IsSpecial,
                user
            );
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

        [HttpPost("batch-by-uuids")]

        public async Task<IActionResult> BatchByUuids([FromBody] List<string> uuids)
        {
            var result = await _service.GetListByUuidsAsync(uuids);
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
