using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Controllers.React
{
    [ApiController]
    [Route("api/react/v1/products")]
    [Authorize]
    public class ReactProductsController : ControllerBase
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<ReactProductsController> _logger;

        public ReactProductsController(
            SqlSugarContext context,
            ILogger<ReactProductsController> logger
        )
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 创建商品并为所有启用分店初始化分店价格
        /// 不联动零售价更新逻辑
        /// </summary>
        [HttpPost("create-with-prices")]
        [Authorize(Policy = Permissions.StoreProducts.Create)]
        public async Task<IActionResult> CreateWithPrices([FromBody] CreateProductWithPricesDto dto)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dto.ProductName))
                {
                    return BadRequest(new { success = false, message = "商品名称不能为空" });
                }
                if (dto.IsAutoPricing == false)
                {
                    if (!dto.RetailPrice.HasValue || dto.RetailPrice.Value <= 0)
                    {
                        return BadRequest(
                            new { success = false, message = "关闭自动定价时必须提供有效零售价" }
                        );
                    }
                }

                var db = _context.Db;
                var now = DateTime.UtcNow;
                await db.Ado.BeginTranAsync();
                try
                {
                    var product = new Product
                    {
                        UUID = UuidHelper.GenerateUuid7(),
                        ProductCode = Guid.NewGuid().ToString(),
                        ProductCategoryGUID = dto.ProductCategoryGUID,
                        LocalSupplierCode = dto.LocalSupplierCode,
                        ItemNumber = dto.ItemNumber,
                        Barcode = dto.Barcode,
                        ProductName = dto.ProductName,
                        PurchasePrice = dto.PurchasePrice,
                        RetailPrice = dto.RetailPrice,
                        IsAutoPricing = dto.IsAutoPricing,
                        IsSpecialProduct = dto.IsSpecialProduct,
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = User.Identity?.Name ?? "system",
                        UpdatedBy = User.Identity?.Name ?? "system",
                        IsDeleted = false,
                    };

                    await db.Insertable(product).ExecuteCommandAsync();

                    var stores = await db.Queryable<Store>()
                        .Where(s => s.IsDeleted == false && s.IsActive == true)
                        .Select(s => new { s.StoreCode })
                        .ToListAsync();

                    var storeProductCodes = new Dictionary<string, string>();
                    var toInsert = new List<StoreRetailPrice>();
                    foreach (var s in stores)
                    {
                        var spCode = UuidHelper.GenerateUuid7();
                        storeProductCodes[s.StoreCode!] = spCode;
                        var srp = new StoreRetailPrice
                        {
                            UUID = UuidHelper.GenerateUuid7(),
                            StoreCode = s.StoreCode,
                            ProductCode = product.ProductCode,
                            StoreProductCode = spCode,
                            SupplierCode = dto.LocalSupplierCode,
                            PurchasePrice = dto.PurchasePrice,
                            StoreRetailPriceValue = dto.RetailPrice, // 初始零售价（可为空），不随更新进货价联动
                            DiscountRate = null,
                            IsActive = true,
                            IsAutoPricing = dto.IsAutoPricing,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = User.Identity?.Name ?? "system",
                            UpdatedBy = User.Identity?.Name ?? "system",
                            IsDeleted = false,
                        };
                        toInsert.Add(srp);
                    }

                    if (toInsert.Any())
                    {
                        await db.Insertable(toInsert).ExecuteCommandAsync();
                    }

                    await db.Ado.CommitTranAsync();
                    var result = new CreateProductWithPricesResultDto
                    {
                        ProductCode = product.ProductCode!,
                        StoreProductCodes = storeProductCodes,
                    };
                    return Ok(new { success = true, data = result });
                }
                catch (Exception exTran)
                {
                    await db.Ado.RollbackTranAsync();
                    _logger.LogError(exTran, "创建商品及分店价格事务失败");
                    return StatusCode(500, new { success = false, message = "创建失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建商品及分店价格失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 仅更新分店进货价，不更新零售价
        /// </summary>
        [HttpPost("update-purchase")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdatePurchase([FromBody] UpdatePurchaseRequestDto dto)
        {
            try
            {
                if (
                    string.IsNullOrWhiteSpace(dto.StoreCode)
                    || string.IsNullOrWhiteSpace(dto.ProductCode)
                    || dto.NewPurchasePrice == null
                )
                {
                    return BadRequest(new { success = false, message = "参数不完整" });
                }

                var db = _context.Db;
                var entity = await db.Queryable<StoreRetailPrice>()
                    .Where(x =>
                        x.StoreCode == dto.StoreCode
                        && x.ProductCode == dto.ProductCode
                        && x.IsDeleted == false
                    )
                    .FirstAsync();
                if (entity == null)
                {
                    return NotFound(new { success = false, message = "分店价格不存在" });
                }

                entity.PurchasePrice = dto.NewPurchasePrice;
                entity.UpdatedAt = DateTime.UtcNow;
                entity.UpdatedBy = User.Identity?.Name ?? "system";
                await db.Updateable(entity).ExecuteCommandAsync();

                return Ok(
                    new
                    {
                        success = true,
                        data = new { currentPurchasePrice = entity.PurchasePrice },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店进货价失败");
                return StatusCode(500, new { success = false, message = "更新失败" });
            }
        }
    }

    public class UpdatePurchaseRequestDto
    {
        public string StoreCode { get; set; } = string.Empty;
        public string ProductCode { get; set; } = string.Empty;
        public decimal? NewPurchasePrice { get; set; }
    }
}
