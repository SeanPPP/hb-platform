using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// React 货柜管理控制器
    /// 专门为React前端提供的API接口
    /// </summary>
    [ApiController]
    [Route("api/react/v1/containers")]
    [Authorize]
    public class ReactContainerController : ControllerBase
    {
        private readonly IContainerReactService _containerReactService;
        private readonly ILogger<ReactContainerController> _logger;

        public ReactContainerController(
            IContainerReactService containerReactService,
            ILogger<ReactContainerController> logger
        )
        {
            _containerReactService = containerReactService;
            _logger = logger;
        }

        /// <summary>
        /// 获取货柜列表（React专用）
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>货柜列表</returns>
        [HttpPost("list")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> GetContainers([FromBody] ContainerQueryRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜列表: Page={Page}, PageSize={PageSize}, DateType={DateType}",
                    request.Page,
                    request.PageSize,
                    request.DateType
                );

                var result = await _containerReactService.GetContainersAsync(request);

                return Ok(
                    new
                    {
                        success = true,
                        data = new
                        {
                            items = result.Containers,
                            total = result.TotalCount,
                            page = result.Page,
                            pageSize = result.PageSize,
                        },
                        message = "获取货柜列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取货柜详情（React专用）
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <returns>货柜详情</returns>
        [HttpGet("{containerGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> GetContainerDetail(string containerGuid)
        {
            try
            {
                if (string.IsNullOrEmpty(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜详情: ContainerGuid={ContainerGuid}",
                    containerGuid
                );

                var result = await _containerReactService.GetContainerDetailAsync(containerGuid);

                if (result == null)
                {
                    return NotFound(new { success = false, message = "货柜不存在" });
                }

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取货柜详情成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜详情失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 更新货柜信息（React专用）
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <param name="dto">更新DTO</param>
        /// <returns>更新结果</returns>
        [HttpPut("{containerGuid}")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> UpdateContainer(
            string containerGuid,
            [FromBody] UpdateContainerDto dto
        )
        {
            try
            {
                if (string.IsNullOrEmpty(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "更新数据不能为空" });
                }

                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v =>
                        v.Errors.Select(e => e.ErrorMessage)
                    );
                    return BadRequest(
                        new
                        {
                            success = false,
                            message = $"输入验证失败: {string.Join(", ", errors)}",
                        }
                    );
                }

                _logger.LogInformation(
                    "更新货柜信息: ContainerGuid={ContainerGuid}",
                    containerGuid
                );

                var result = await _containerReactService.UpdateContainerAsync(containerGuid, dto);

                if (result)
                {
                    return Ok(new { success = true, message = "更新成功" });
                }
                else
                {
                    return NotFound(new { success = false, message = "货柜不存在或更新失败" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "更新货柜信息失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取货柜商品列表（React专用）
        /// </summary>
        /// <param name="containerGuid">货柜GUID</param>
        /// <returns>商品列表</returns>
        [HttpGet("{containerGuid}/products")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> GetContainerProducts(string containerGuid)
        {
            try
            {
                if (string.IsNullOrEmpty(containerGuid))
                {
                    return BadRequest(new { success = false, message = "货柜GUID不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜商品列表: ContainerGuid={ContainerGuid}",
                    containerGuid
                );

                var result = await _containerReactService.GetContainerProductsAsync(containerGuid);

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取商品列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "获取货柜商品列表失败, ContainerGuid: {ContainerGuid}",
                    containerGuid
                );
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取符合条件的所有货柜商品明细列表（React专用）
        /// </summary>
        /// <param name="request">查询请求</param>
        /// <returns>商品明细列表</returns>
        [HttpPost("filtered-products")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> GetFilteredContainerProducts(
            [FromBody] ContainerQueryRequest request
        )
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "请求参数不能为空" });
                }

                _logger.LogInformation(
                    "获取货柜商品明细列表: DateType={DateType}, ItemNumberFilter={ItemNumberFilter}",
                    request.DateType,
                    request.ItemNumberFilter
                );

                var result = await _containerReactService.GetFilteredContainerProductsAsync(
                    request
                );

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取商品明细列表成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取货柜商品明细列表失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量更新货柜明细（React专用）
        /// </summary>
        /// <param name="updates">明细更新列表</param>
        /// <returns>更新结果</returns>
        [HttpPost("batch-update-details")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchUpdateDetails(
            [FromBody] List<UpdateContainerDetailDto> updates
        )
        {
            try
            {
                if (updates == null || !updates.Any())
                {
                    return BadRequest(new { success = false, message = "更新列表不能为空" });
                }

                var totalUpdated = await _containerReactService.BatchUpdateDetailsAsync(updates);

                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功更新 {totalUpdated} 条明细",
                        data = new { totalUpdated, totalRequested = updates.Count },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新货柜明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量删除货柜明细（React专用）
        /// </summary>
        /// <param name="request">包含待删除的 HGUID 列表</param>
        /// <returns>删除结果</returns>
        [HttpPost("batch-delete-details")]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> BatchDeleteDetails(
            [FromBody] BatchDeleteDetailsRequestDto request
        )
        {
            try
            {
                if (request == null || request.Hguids == null || !request.Hguids.Any())
                {
                    return BadRequest(new { success = false, message = "删除列表不能为空" });
                }

                var totalDeleted = await _containerReactService.BatchDeleteDetailsAsync(
                    request.Hguids
                );

                return Ok(
                    new
                    {
                        success = true,
                        message = $"成功删除 {totalDeleted} 条明细",
                        data = new { totalDeleted, totalRequested = request.Hguids.Count },
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除货柜明细失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取日期过滤选项（React专用）
        /// </summary>
        /// <returns>日期选项列表</returns>
        [HttpGet("date-filter-options")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> GetDateFilterOptions()
        {
            try
            {
                _logger.LogInformation("获取日期过滤选项");

                var result = await _containerReactService.GetDateFilterOptionsAsync();

                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "获取日期过滤选项成功",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取日期过滤选项失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 创建新货柜（React专用）
        /// </summary>
        /// <param name="dto">创建货柜DTO</param>
        /// <returns>创建结果</returns>
        [HttpPost]
        [Authorize(Roles = "Admin,WarehouseManager")]
        public async Task<IActionResult> CreateContainer([FromBody] CreateContainerDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { success = false, message = "创建数据不能为空" });
                }

                if (string.IsNullOrWhiteSpace(dto.货柜编号))
                {
                    return BadRequest(new { success = false, message = "货柜编号不能为空" });
                }

                var containerGuid = await _containerReactService.CreateContainerAsync(dto);

                return Ok(
                    new
                    {
                        success = true,
                        message = "创建成功",
                        data = new { containerGuid },
                    }
                );
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "创建货柜失败: {Message}", ex.Message);
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建货柜失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 检查货柜明细冲突（按 ProductCode）
        /// </summary>
        [HttpPost("check-conflicts")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> CheckConflicts([FromBody] CheckConflictsRequestDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ContainerId))
                {
                    return BadRequest(new { success = false, message = "ContainerId 不能为空" });
                }
                var codes =
                    request
                        .Items?.Select(i => i.ProductCode)
                        ?.Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct()
                        .ToList() ?? new List<string>();
                if (!codes.Any())
                {
                    return Ok(
                        new
                        {
                            success = true,
                            data = Array.Empty<ContainerConflictItemDto>(),
                            message = "无待检查的编码",
                        }
                    );
                }

                var conflicts = await _containerReactService.CheckConflictsAsync(
                    request.ContainerId,
                    codes
                );
                return Ok(
                    new
                    {
                        success = true,
                        data = conflicts,
                        message = "检查完成",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查货柜明细冲突失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 批量分配商品到货柜（支持覆盖/增加数量）
        /// </summary>
        [HttpPost("assign-products")]
        [Authorize(Roles = "Admin,WarehouseManager,User")]
        public async Task<IActionResult> AssignProducts([FromBody] AssignProductsRequestDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ContainerId))
                {
                    return BadRequest(new { success = false, message = "ContainerId 不能为空" });
                }
                if (request.Items == null || !request.Items.Any())
                {
                    return BadRequest(new { success = false, message = "Items 不能为空" });
                }
                var resolution = string.Equals(
                    request.Resolution,
                    "override",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "override"
                    : "increase";
                var result = await _containerReactService.AssignProductsAsync(
                    request.ContainerId,
                    request.Items,
                    resolution,
                    request.Notes
                );
                return Ok(
                    new
                    {
                        success = true,
                        data = result,
                        message = "分配完成",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量分配商品到货柜失败");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }
    }
}
