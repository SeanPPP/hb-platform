using System.Collections.Generic;
using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-retail-prices")]
    [Authorize]
    public class ReactStoreRetailPricesController : ControllerBase
    {
        private readonly IStoreRetailPriceReactService _service;

        public ReactStoreRetailPricesController(IStoreRetailPriceReactService service)
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
                        Items = result.Items ?? new List<StoreRetailPriceListDto>(),
                        Total = result.Total,
                    },
                    message = result.Message,
                }
            );
        }

        [HttpGet("{uuid}")]
       
        public async Task<IActionResult> GetByUuid(string uuid)
        {
            var result = await _service.GetByUuidAsync(uuid);
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

        [HttpPost]
       
        public async Task<IActionResult> Create([FromBody] CreateStoreRetailPriceDto dto)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.CreateAsync(dto, user);
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

        [HttpPut("{uuid}")]
      
        public async Task<IActionResult> Update(
            string uuid,
            [FromBody] UpdateStoreRetailPriceDto dto
        )
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.UpdateAsync(uuid, dto, user);
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

        [HttpDelete("{uuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> Delete(string uuid)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.DeleteAsync(uuid, user);
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

        [HttpPost("batch-upsert")]
       
        public async Task<IActionResult> BatchUpsert(
            [FromBody] List<StoreRetailPriceUpsertItemDto> items
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
            [FromBody] List<StoreRetailPriceUpsertForActiveStoresItemDto> items
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

        [HttpDelete("batch-delete")]

        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDelete([FromBody] List<string> uuids)
        {
            var user = User.Identity?.Name ?? "system";
            var result = await _service.BatchDeleteAsync(uuids, user);
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
      
        public async Task<IActionResult> BatchSpecial([FromBody] BatchUpdateSpecialRequestDto dto)
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
