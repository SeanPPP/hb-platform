using System.Threading.Tasks;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/store-product-prices")]
    [Authorize]
    public class ReactStoreProductPricesController : ControllerBase
    {
        private readonly IStoreProductPriceReactService _service;
        private readonly ILogger<ReactStoreProductPricesController> _logger;

        public ReactStoreProductPricesController(
            IStoreProductPriceReactService service,
            ILogger<ReactStoreProductPricesController> logger
        )
        {
            _service = service;
            _logger = logger;
        }

        [HttpPost("grid")]
        public async Task<IActionResult> Grid([FromBody] StoreProductPriceQueryDto query)
        {
            if (string.IsNullOrWhiteSpace(query.StoreCode))
            {
                return Ok(new
                {
                    success = false,
                    message = "请选择分店"
                });
            }

            var result = await _service.GetGridDataAsync(query);
            if (result.Success)
            {
                return Ok(new
                {
                    success = true,
                    data = new { Items = result.Items, Total = result.Total },
                    message = result.Message
                });
            }

            return Ok(new
            {
                success = false,
                data = new { Items = result.Items, Total = result.Total },
                message = result.Message
            });
        }

        [HttpPost("batch-update")]
        public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateStoreRetailPriceDto dto)
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.BatchUpdateStoreRetailPricesAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("sync-to-other-stores")]
        public async Task<IActionResult> SyncToOtherStores([FromBody] SyncToOtherStoresDto dto)
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.SyncToOtherStoresAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }

        [HttpPost("copy-store-data")]
        public async Task<IActionResult> CopyStoreData([FromBody] CopyStoreDataDto dto)
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var result = await _service.CopyStoreDataAsync(dto, updatedBy);
            if (result.Success)
            {
                return Ok(result);
            }

            return BadRequest(result);
        }
    }
}
