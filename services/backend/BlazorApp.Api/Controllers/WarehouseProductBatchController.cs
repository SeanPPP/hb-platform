using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 仓库商品批量管理API控制器
    /// 权限要求：Admin 或 Warehouse（仓库管理员）
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class WarehouseProductBatchController : ControllerBase
    {
        private readonly IWarehouseProductBatchService _service;
        private readonly ILogger<WarehouseProductBatchController> _logger;

        public WarehouseProductBatchController(
            IWarehouseProductBatchService service,
            ILogger<WarehouseProductBatchController> logger)
        {
            _service = service;
            _logger = logger;
        }

        #region 查询接口

        /// <summary>
        /// 根据过滤条件获取商品列表（分页）
        /// </summary>
        /// <param name="filter">过滤条件</param>
        /// <returns>分页结果</returns>
        [HttpPost("filter")]
        [ProducesResponseType(typeof(PagedResultDto<WarehouseProductBatchDto>), 200)]
        public async Task<IActionResult> GetByFilter([FromBody] WarehouseProductBatchFilterDto filter)
        {
            try
            {
                var result = await _service.GetByFilterAsync(filter);
                return Ok(new
                {
                    success = true,
                    data = result,
                    message = $"查询成功，共 {result.TotalCount} 条数据"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询仓库商品失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "查询失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 获取所有可用仓位列表
        /// </summary>
        /// <returns>仓位列表</returns>
        [HttpGet("locations")]
        [ProducesResponseType(typeof(List<LocationOptionDto>), 200)]
        public async Task<IActionResult> GetAvailableLocations()
        {
            try
            {
                var locations = await _service.GetAvailableLocationsAsync();
                return Ok(new
                {
                    success = true,
                    data = locations,
                    message = $"获取成功，共 {locations.Count} 个仓位"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取仓位列表失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "获取仓位列表失败：" + ex.Message
                });
            }
        }

        #endregion

        #region 更新接口

        /// <summary>
        /// 批量更新商品信息（全部保存）
        /// </summary>
        /// <param name="request">批量更新请求</param>
        /// <returns>更新结果</returns>
        [HttpPost("batch-update")]
        [ProducesResponseType(typeof(BatchUpdateResult), 200)]
        public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateRequest request)
        {
            try
            {
                if (request.Products == null || request.Products.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "商品列表不能为空"
                    });
                }

                var result = await _service.BatchUpdateAsync(request);

                return Ok(new
                {
                    success = result.Success,
                    data = result,
                    message = result.Success
                        ? $"批量更新成功，共更新 {result.UpdatedCount} 条"
                        : $"批量更新部分成功，成功 {result.UpdatedCount} 条，失败 {result.FailedCount} 条"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "批量更新失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 增量保存（保存单条或部分修改）
        /// </summary>
        /// <param name="request">增量保存请求</param>
        /// <returns>保存结果</returns>
        [HttpPost("incremental-save")]
        [ProducesResponseType(typeof(IncrementalSaveResult), 200)]
        public async Task<IActionResult> IncrementalSave([FromBody] IncrementalSaveRequest request)
        {
            try
            {
                if (request.Products == null || request.Products.Count == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "商品数据不能为空"
                    });
                }

                var result = await _service.IncrementalSaveAsync(request);

                return Ok(new
                {
                    success = result.Success,
                    data = result,
                    message = result.Success
                        ? $"保存成功，共保存 {result.SavedCount} 条"
                        : $"保存部分成功，成功 {result.SavedCount} 条，失败 {result.FailedCount} 条"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "增量保存失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "增量保存失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 更新商品的仓位信息
        /// </summary>
        /// <param name="request">仓位编辑请求</param>
        /// <returns>是否成功</returns>
        [HttpPost("update-location")]
        public async Task<IActionResult> UpdateLocation([FromBody] LocationEditDto request)
        {
            try
            {
                var success = await _service.UpdateLocationAsync(request);

                return Ok(new
                {
                    success,
                    message = success ? "仓位更新成功" : "仓位更新失败"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新仓位失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "更新仓位失败：" + ex.Message
                });
            }
        }

        #endregion

        #region 批量操作接口

        /// <summary>
        /// 批量设置价格
        /// </summary>
        /// <param name="request">批量设置价格请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("bulk-set-price")]
        [ProducesResponseType(typeof(BulkOperationResult), 200)]
        public async Task<IActionResult> BulkSetPrice([FromBody] BulkSetPriceRequest request)
        {
            try
            {
                var result = await _service.BulkSetPriceAsync(request);

                return Ok(new
                {
                    success = result.Success,
                    data = result,
                    message = result.Success
                        ? $"批量设置价格成功，影响 {result.AffectedCount} 条"
                        : result.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量设置价格失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "批量设置价格失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 批量调整库存
        /// </summary>
        /// <param name="request">批量调整库存请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("bulk-adjust-stock")]
        [ProducesResponseType(typeof(BulkOperationResult), 200)]
        public async Task<IActionResult> BulkAdjustStock([FromBody] BulkAdjustStockRequest request)
        {
            try
            {
                var result = await _service.BulkAdjustStockAsync(request);

                return Ok(new
                {
                    success = result.Success,
                    data = result,
                    message = result.Success
                        ? $"批量调整库存成功，影响 {result.AffectedCount} 条"
                        : result.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量调整库存失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "批量调整库存失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 批量设置使用状态
        /// </summary>
        /// <param name="request">批量设置状态请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("bulk-set-status")]
        [ProducesResponseType(typeof(BulkOperationResult), 200)]
        public async Task<IActionResult> BulkSetStatus([FromBody] BulkSetStatusRequest request)
        {
            try
            {
                var result = await _service.BulkSetStatusAsync(request);

                return Ok(new
                {
                    success = result.Success,
                    data = result,
                    message = result.Success
                        ? $"批量设置状态成功，影响 {result.AffectedCount} 条"
                        : result.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量设置状态失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "批量设置状态失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 批量设置仓位
        /// </summary>
        /// <param name="request">批量设置仓位请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("bulk-set-location")]
        [ProducesResponseType(typeof(BulkOperationResult), 200)]
        public async Task<IActionResult> BulkSetLocation([FromBody] BulkSetLocationRequest request)
        {
            try
            {
                var result = await _service.BulkSetLocationAsync(request);

                return Ok(new
                {
                    success = result.Success,
                    data = result,
                    message = result.Success
                        ? $"批量设置仓位成功，影响 {result.AffectedCount} 条"
                        : result.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量设置仓位失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "批量设置仓位失败：" + ex.Message
                });
            }
        }

        #endregion

        #region 导出接口

        /// <summary>
        /// 导出为Excel
        /// </summary>
        /// <param name="filter">过滤条件</param>
        /// <returns>Excel文件</returns>
        [HttpPost("export-excel")]
        public async Task<IActionResult> ExportToExcel([FromBody] WarehouseProductBatchFilterDto filter)
        {
            try
            {
                var fileBytes = await _service.ExportToExcelAsync(filter);
                var fileName = $"仓库商品数据_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (NotImplementedException)
            {
                return StatusCode(501, new
                {
                    success = false,
                    message = "Excel导出功能尚未实现"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出Excel失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "导出Excel失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 导出为PDF
        /// </summary>
        /// <param name="filter">过滤条件</param>
        /// <returns>PDF文件</returns>
        [HttpPost("export-pdf")]
        public async Task<IActionResult> ExportToPdf([FromBody] WarehouseProductBatchFilterDto filter)
        {
            try
            {
                var fileBytes = await _service.ExportToPdfAsync(filter);
                var fileName = $"仓库商品数据_{DateTime.Now:yyyyMMddHHmmss}.pdf";

                return File(fileBytes, "application/pdf", fileName);
            }
            catch (NotImplementedException)
            {
                return StatusCode(501, new
                {
                    success = false,
                    message = "PDF导出功能尚未实现"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出PDF失败");
                return StatusCode(500, new
                {
                    success = false,
                    message = "导出PDF失败：" + ex.Message
                });
            }
        }

        #endregion
    }
}

