using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using AutoMapper;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 义乌订单管理控制器
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize]
    public class YiwuOrdersController : ControllerBase
    {
        private readonly IYiwuOrderService _orderService;
        private readonly IMapper _mapper;
        private readonly ILogger<YiwuOrdersController> _logger;

        public YiwuOrdersController(
            IYiwuOrderService orderService,
            IMapper mapper,
            ILogger<YiwuOrdersController> logger)
        {
            _orderService = orderService;
            _mapper = mapper;
            _logger = logger;
        }

        #region 义乌订单主表API

        /// <summary>
        /// 获取义乌订单列表
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetOrders(
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? keyword = null)
        {
            try
            {
                var result = await _orderService.GetOrdersAsync(pageIndex, pageSize, keyword);
                return Ok(new ApiResponse<PagedResult<YIWU_Order>>
                {
                    Success = true,
                    Data = result,
                    Message = "获取订单列表成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取义乌订单列表失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取订单列表失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 根据ID获取订单详情
        /// </summary>
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetOrder(int id)
        {
            try
            {
                var order = await _orderService.GetOrderByIdAsync(id);
                if (order == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单不存在"
                    });
                }

                return Ok(new ApiResponse<YIWU_Order>
                {
                    Success = true,
                    Data = order,
                    Message = "获取订单详情成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取义乌订单详情失败，ID: {OrderId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取订单详情失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 根据订单编号获取订单
        /// </summary>
        [HttpGet("by-order-no/{orderNo}")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetOrderByOrderNo(string orderNo)
        {
            try
            {
                var order = await _orderService.GetOrderByOrderNoAsync(orderNo);
                if (order == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单不存在"
                    });
                }

                return Ok(new ApiResponse<YIWU_Order>
                {
                    Success = true,
                    Data = order,
                    Message = "获取订单详情成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据订单编号获取义乌订单失败，OrderNo: {OrderNo}", orderNo);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取订单详情失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 创建义乌订单
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateYiwuOrderDto createDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "输入数据无效",
                        Details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList()
                    });
                }

                var order = new YIWU_Order
                {
                    SupplierCode = createDto.SupplierCode,
                    Remarks = createDto.Remarks,
                    OrderStatus = 0 // 默认草稿状态
                };

                var createdOrder = await _orderService.CreateOrderAsync(order);

                // 如果有订单明细，批量创建
                if (createDto.OrderDetails?.Any() == true)
                {
                    var orderDetails = createDto.OrderDetails.Select(d => new YIWU_OrderDetail
                    {
                        OrderNo = createdOrder.OrderNo,
                        ProductCode = d.ProductCode,
                        HBProductNo = d.HBProductNo,
                        Barcode = d.Barcode,
                        EnglishName = d.EnglishName,
                        DomesticPrice = d.DomesticPrice,
                        OEMPrice = d.OEMPrice,
                        ProductImage = d.ProductImage,
                        PackingQuantity = d.PackingQuantity,
                        UnitVolume = d.UnitVolume,
                        MiddlePackQuantity = d.MiddlePackQuantity,
                        UsageStatus = d.UsageStatus,
                        SupplierCode = d.SupplierCode,
                        SupplierName = d.SupplierName,
                        OrderQuantity = d.OrderQuantity,
                        OrderBoxes = d.OrderBoxes
                    }).ToList();

                    await _orderService.CreateOrderDetailsAsync(orderDetails);
                    
                    // 重新获取包含明细的订单
                    createdOrder = await _orderService.GetOrderByIdAsync(createdOrder.ID);
                }

                return Ok(new ApiResponse<YIWU_Order>
                {
                    Success = true,
                    Data = createdOrder,
                    Message = "创建订单成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建义乌订单失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "创建订单失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 更新义乌订单
        /// </summary>
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateOrder(int id, [FromBody] UpdateYiwuOrderDto updateDto)
        {
            try
            {
                if (id != updateDto.ID)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单ID不匹配"
                    });
                }

                var existingOrder = await _orderService.GetOrderByIdAsync(id);
                if (existingOrder == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单不存在"
                    });
                }

                // 更新订单信息
                existingOrder.SupplierCode = updateDto.SupplierCode ?? existingOrder.SupplierCode;
                existingOrder.OrderStatus = updateDto.OrderStatus ?? existingOrder.OrderStatus;
                existingOrder.Remarks = updateDto.Remarks ?? existingOrder.Remarks;

                var result = await _orderService.UpdateOrderAsync(existingOrder);
                if (!result)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "更新订单失败"
                    });
                }

                // 重新获取更新后的订单
                var updatedOrder = await _orderService.GetOrderByIdAsync(id);

                return Ok(new ApiResponse<YIWU_Order>
                {
                    Success = true,
                    Data = updatedOrder,
                    Message = "更新订单成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新义乌订单失败，ID: {OrderId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "更新订单失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 删除义乌订单
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteOrder(int id)
        {
            try
            {
                var existingOrder = await _orderService.GetOrderByIdAsync(id);
                if (existingOrder == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单不存在"
                    });
                }

                var result = await _orderService.DeleteOrderAsync(id);
                if (!result)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "删除订单失败"
                    });
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "删除订单成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除义乌订单失败，ID: {OrderId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "删除订单失败：" + ex.Message
                });
            }
        }

        #endregion

        #region 义乌订单明细API

        /// <summary>
        /// 获取订单明细列表
        /// </summary>
        [HttpGet("details")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetOrderDetails(
            [FromQuery] string? orderNo = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _orderService.GetOrderDetailsAsync(orderNo, pageIndex, pageSize);
                return Ok(new ApiResponse<PagedResult<YIWU_OrderDetail>>
                {
                    Success = true,
                    Data = result,
                    Message = "获取订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取义乌订单明细失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取订单明细失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 根据ID获取订单明细
        /// </summary>
        [HttpGet("details/{id}")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetOrderDetail(int id)
        {
            try
            {
                var detail = await _orderService.GetOrderDetailByIdAsync(id);
                if (detail == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单明细不存在"
                    });
                }

                return Ok(new ApiResponse<YIWU_OrderDetail>
                {
                    Success = true,
                    Data = detail,
                    Message = "获取订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取义乌订单明细失败，ID: {DetailId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取订单明细失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 创建订单明细
        /// </summary>
        [HttpPost("details")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateOrderDetail([FromBody] CreateYiwuOrderDetailDto createDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "输入数据无效",
                        Details = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList()
                    });
                }

                var orderDetail = _mapper.Map<YIWU_OrderDetail>(createDto);
                var createdDetail = await _orderService.CreateOrderDetailAsync(orderDetail);

                return Ok(new ApiResponse<YIWU_OrderDetail>
                {
                    Success = true,
                    Data = createdDetail,
                    Message = "创建订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建义乌订单明细失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "创建订单明细失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 更新订单明细
        /// </summary>
        [HttpPut("details/{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateOrderDetail(int id, [FromBody] YIWU_OrderDetail orderDetail)
        {
            try
            {
                if (id != orderDetail.ID)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单明细ID不匹配"
                    });
                }

                var result = await _orderService.UpdateOrderDetailAsync(orderDetail);
                if (!result)
                {
                    return StatusCode(500, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "更新订单明细失败"
                    });
                }

                var updatedDetail = await _orderService.GetOrderDetailByIdAsync(id);
                return Ok(new ApiResponse<YIWU_OrderDetail>
                {
                    Success = true,
                    Data = updatedDetail,
                    Message = "更新订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新义乌订单明细失败，ID: {DetailId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "更新订单明细失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 删除订单明细
        /// </summary>
        [HttpDelete("details/{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteOrderDetail(int id)
        {
            try
            {
                var result = await _orderService.DeleteOrderDetailAsync(id);
                if (!result)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "订单明细不存在或删除失败"
                    });
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "删除订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除义乌订单明细失败，ID: {DetailId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "删除订单明细失败：" + ex.Message
                });
            }
        }

        #endregion

        #region PDA订单转义乌订单

        /// <summary>
        /// 从PDA订单明细创建义乌订单
        /// </summary>
        [HttpPost("create-from-pda")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateOrdersFromPDA([FromBody] PDAToYiwuOrderDto? options = null)
        {
            try
            {
                var createdOrders = await _orderService.CreateOrdersFromPDAAsync();
                
                if (!createdOrders.Any())
                {
                    return Ok(new ApiResponse<List<YIWU_Order>>
                    {
                        Success = true,
                        Data = createdOrders,
                        Message = "没有找到PDA订单明细数据，未创建任何订单"
                    });
                }

                return Ok(new ApiResponse<List<YIWU_Order>>
                {
                    Success = true,
                    Data = createdOrders,
                    Message = $"成功从PDA订单创建了 {createdOrders.Count} 个义乌订单"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从PDA订单创建义乌订单失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "从PDA订单创建义乌订单失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 获取PDA订单明细
        /// </summary>
        [HttpGet("pda-details")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetPDAOrderDetails()
        {
            try
            {
                var pdaDetails = await _orderService.GetPDAOrderDetailsAsync();
                return Ok(new ApiResponse<List<YIWU_OrderDetail>>
                {
                    Success = true,
                    Data = pdaDetails,
                    Message = "获取PDA订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取PDA订单明细失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取PDA订单明细失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 按供应商分组PDA订单明细
        /// </summary>
        [HttpGet("pda-details/group-by-supplier")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GroupPDADetailsBySupplier()
        {
            try
            {
                var groupedDetails = await _orderService.GroupPDADetailsBySupplierAsync();
                return Ok(new ApiResponse<Dictionary<string, List<YIWU_OrderDetail>>>
                {
                    Success = true,
                    Data = groupedDetails,
                    Message = "按供应商分组PDA订单明细成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按供应商分组PDA订单明细失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "按供应商分组PDA订单明细失败：" + ex.Message
                });
            }
        }

        #endregion

        #region 导出功能

        /// <summary>
        /// 导出义乌订单到Excel
        /// </summary>
        [HttpGet("{id}/export/excel")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> ExportToExcel(int id, [FromQuery] bool includeImages = false)
        {
            try
            {
                byte[] fileBytes;
                string fileName;

                if (includeImages)
                {
                    fileBytes = await _orderService.ExportOrderToExcelWithImagesAsync(id);
                    fileName = $"义乌订单_{id}_带图片_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                }
                else
                {
                    fileBytes = await _orderService.ExportOrderToExcelAsync(id);
                    fileName = $"义乌订单_{id}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                }

                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出义乌订单到Excel失败，ID: {OrderId}", id);
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "导出Excel失败：" + ex.Message
                });
            }
        }



        #endregion

        #region 批量导出功能

        /// <summary>
        /// 批量导出多个义乌订单到Excel（带图片）
        /// </summary>
        [HttpPost("export/batch/excel")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> BatchExportToExcel([FromBody] BatchExportRequest request)
        {
            try
            {
                if (request?.OrderIds == null || !request.OrderIds.Any())
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "请选择要导出的订单"
                    });
                }

                if (request.OrderIds.Count() > 100)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "单次最多只能导出100个订单"
                    });
                }

                _logger.LogInformation("开始批量导出 {OrderCount} 个订单", request.OrderIds.Count());

                var fileBytes = await _orderService.ExportMultipleOrdersToExcelWithImagesAsync(
                    request.OrderIds, 
                    request.MaxConcurrency ?? 3);

                var fileName = $"批量义乌订单_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量导出义乌订单到Excel失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "批量导出Excel失败：" + ex.Message
                });
            }
        }

        #endregion

        #region 统计功能

        /// <summary>
        /// 获取订单统计信息
        /// </summary>
        [HttpGet("statistics")]
        [Authorize(Roles = "Admin,Manager,User")]
        public async Task<IActionResult> GetStatistics()
        {
            try
            {
                var statistics = await _orderService.GetOrderStatisticsAsync();
                return Ok(new ApiResponse<OrderStatisticsDto>
                {
                    Success = true,
                    Data = statistics,
                    Message = "获取订单统计信息成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取订单统计信息失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "获取订单统计信息失败：" + ex.Message
                });
            }
        }

        /// <summary>
        /// 生成新的订单编号
        /// </summary>
        [HttpGet("generate-order-no")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GenerateOrderNo()
        {
            try
            {
                var orderNo = await _orderService.GenerateOrderNoAsync();
                return Ok(new ApiResponse<string>
                {
                    Success = true,
                    Data = orderNo,
                    Message = "生成订单编号成功"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成订单编号失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "生成订单编号失败：" + ex.Message
                });
            }
        }

        #endregion
    }
}