using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React
{
    /// <summary>
    /// 销售仪表盘控制器 - 提供销售数据分析和统计功能
    /// 路由: api/react/v1/dashboard
    /// </summary>
    [ApiController]
    [Route("api/react/v1/dashboard")]
    [Authorize]
    public class SalesDashboardController : ControllerBase
    {
        private readonly ISalesDashboardReactService _service;
        private readonly ILogger<SalesDashboardController> _logger;
        private readonly IUserService _userService;
        private readonly ISalesDashboardCacheWarmer _cacheWarmer;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="service">销售仪表盘服务</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="userService">用户服务</param>
        public SalesDashboardController(
            ISalesDashboardReactService service,
            ILogger<SalesDashboardController> logger,
            IUserService userService,
            ISalesDashboardCacheWarmer cacheWarmer
        )
        {
            _service = service;
            _logger = logger;
            _userService = userService;
            _cacheWarmer = cacheWarmer;
        }

        /// <summary>
        /// 检查当前用户是否拥有指定角色（大小写不敏感）
        /// </summary>
        /// <param name="role">角色名称</param>
        /// <returns>是否拥有该角色</returns>
        private bool HasRole(string role)
        {
            var user = HttpContext.User;
            if (user == null)
                return false;
            return user.Claims.Any(c =>
                c.Type == ClaimTypes.Role
                && c.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// 检查当前用户是否为管理员
        /// </summary>
        /// <returns>是否为管理员</returns>
        private bool IsAdmin()
        {
            var user = HttpContext.User;
            if (user == null)
                return false;
            return HasRole("Admin");
        }

        /// <summary>
        /// 获取当前用户可访问的分店代码列表
        /// 管理员返回null（可访问所有分店），普通用户返回其关联的分店代码列表
        /// </summary>
        /// <returns>分店代码列表，管理员返回null</returns>
        private async Task<List<string>?> GetUserBranchCodesAsync()
        {
            // 管理员可访问所有分店
            if (IsAdmin())
                return null;

            // 获取当前用户GUID
            var userGuid = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userGuid))
                return null;

            // 查询用户信息
            var user = await _userService.GetUserByGuidAsync(userGuid);
            if (user?.Success != true || user?.Data == null)
                return null;

            // 返回用户关联的分店代码
            return user.Data.Stores?.Select(s => s.StoreCode).ToList();
        }

        /// <summary>
        /// 获取仪表盘汇总数据
        /// GET api/react/v1/dashboard/summary
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式（按日期/按期间）</param>
        /// <returns>仪表盘汇总数据</returns>
        [HttpGet("summary")]
        public async Task<IActionResult> GetDashboardSummary(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate
        )
        {
            try
            {
                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取汇总数据
                var result = await _service.GetDashboardSummaryAsync(dateRange);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDashboardSummary failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取按小时统计的销售数据
        /// GET api/react/v1/dashboard/hourly-sales
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="supplierCode">供应商代码（可选）</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>小时销售数据列表</returns>
        [HttpGet("hourly-sales")]
        public async Task<IActionResult> GetHourlySales(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] List<string>? branchCodes = null
        )
        {
            try
            {
                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 如果请求中指定了分店代码，需要进行权限校验
                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        // 普通用户：取请求分店与用户可访问分店的交集
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        // 如果交集为空，说明用户无权访问任何请求的分店
                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        // 管理员：允许访问请求的所有分店
                        targetBranchCodes = branchCodes;
                    }
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取小时销售数据
                var result = await _service.GetHourlySalesAsync(dateRange, targetBranchCodes);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetHourlySales failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取分店销售排行
        /// GET api/react/v1/dashboard/store-sales-rank
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="topN">返回前N条记录</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>分店销售排行列表</returns>
        [HttpGet("store-sales-rank")]
        public async Task<IActionResult> GetStoreSalesRank(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] int topN = 100,
            [FromQuery] List<string>? branchCodes = null
        )
        {
            try
            {
                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 权限校验：取请求分店与用户可访问分店的交集
                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        targetBranchCodes = branchCodes;
                    }
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取分店销售排行
                var result = await _service.GetStoreSalesRankAsync(
                    dateRange,
                    targetBranchCodes,
                    topN
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStoreSalesRank failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取供应商销售排行
        /// GET api/react/v1/dashboard/supplier-sales-rank
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="topN">返回前N条记录</param>
        /// <returns>供应商销售排行列表</returns>
        [HttpGet("supplier-sales-rank")]
        public async Task<IActionResult> GetSupplierSalesRank(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] int topN = 200
        )
        {
            try
            {
                // 获取用户可访问的分店代码
                var branchCodes = await GetUserBranchCodesAsync();

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取供应商销售排行
                var result = await _service.GetSupplierSalesRankAsync(dateRange, branchCodes, topN);
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSupplierSalesRank failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取中国供应商销售排行
        /// GET api/react/v1/dashboard/china-supplier-sales-rank
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="topN">返回前N条记录</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>中国供应商销售排行列表</returns>
        [HttpGet("china-supplier-sales-rank")]
        public async Task<IActionResult> GetChinaSupplierSalesRankAsync(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] int topN = 100,
            [FromQuery] List<string>? branchCodes = null
        )
        {
            try
            {
                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 权限校验：取请求分店与用户可访问分店的交集
                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        targetBranchCodes = branchCodes;
                    }
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取中国供应商销售排行
                var result = await _service.GetChinaSupplierSalesRankAsync(
                    dateRange,
                    targetBranchCodes,
                    topN
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierSalesRank failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取供应商在各分店的销售数据
        /// GET api/react/v1/dashboard/supplier-store-sales
        /// </summary>
        /// <param name="supplierCodes">供应商代码列表</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>供应商分店销售数据列表</returns>
        [HttpGet("supplier-store-sales")]
        public async Task<IActionResult> GetSupplierStoreSales(
            [FromQuery] List<string> supplierCodes,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] List<string>? branchCodes = null
        )
        {
            try
            {
                // 校验供应商代码不能为空
                if (supplierCodes == null || !supplierCodes.Any())
                {
                    return BadRequest(new { success = false, message = "供应商代码不能为空" });
                }

                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 权限校验：取请求分店与用户可访问分店的交集
                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        targetBranchCodes = branchCodes;
                    }
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取供应商分店销售数据
                var result = await _service.GetSupplierStoreSalesAsync(
                    dateRange,
                    supplierCodes,
                    targetBranchCodes
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSupplierStoreSales failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取分店供应商销售排行
        /// GET api/react/v1/dashboard/store-supplier-sales
        /// </summary>
        /// <param name="branchCodes">分店代码列表</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="topN">返回前N条记录</param>
        /// <returns>分店供应商销售排行列表</returns>
        [HttpGet("store-supplier-sales")]
        public async Task<IActionResult> GetStoreSupplierSales(
            [FromQuery] List<string> branchCodes,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] int topN = 100
        )
        {
            try
            {
                // 校验分店代码不能为空
                if (branchCodes == null || !branchCodes.Any())
                {
                    return BadRequest(new { success = false, message = "分店代码不能为空" });
                }

                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 权限校验：取请求分店与用户可访问分店的交集
                if (userBranchCodes != null)
                {
                    targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                    if (!targetBranchCodes.Any())
                    {
                        return Ok(new { success = true, data = new List<object>() });
                    }
                }
                else
                {
                    targetBranchCodes = branchCodes;
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取分店供应商销售排行
                var result = await _service.GetStoreSupplierSalesAsync(
                    dateRange,
                    targetBranchCodes,
                    topN
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetStoreSupplierSales failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取中国供应商在各分店的销售数据
        /// GET api/react/v1/dashboard/china-supplier-store-sales
        /// </summary>
        /// <param name="supplierCodes">中国供应商代码列表</param>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <returns>中国供应商分店销售数据列表</returns>
        [HttpGet("china-supplier-store-sales")]
        public async Task<IActionResult> GetChinaSupplierStoreSales(
            [FromQuery] List<string> supplierCodes,
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] List<string>? branchCodes = null
        )
        {
            try
            {
                if (supplierCodes == null || !supplierCodes.Any())
                {
                    return BadRequest(new { success = false, message = "供应商代码不能为空" });
                }

                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        targetBranchCodes = branchCodes;
                    }
                }

                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                var result = await _service.GetChinaSupplierStoreSalesAsync(
                    dateRange,
                    supplierCodes,
                    targetBranchCodes
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierStoreSales failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取澳洲供应商分店销售明细
        /// GET api/react/v1/dashboard/australian-supplier-store-sales-details
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="supplierCodes">供应商代码列表（可选）</param>
        /// <returns>澳洲供应商分店销售明细列表</returns>
        [HttpGet("australian-supplier-store-sales-details")]
        public async Task<IActionResult> GetAustralianSupplierStoreSalesDetails(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] List<string>? branchCodes = null,
            [FromQuery] List<string>? supplierCodes = null
        )
        {
            try
            {
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        targetBranchCodes = branchCodes;
                    }
                }

                var dateRange = new DateRangeDto { StartDate = startDate, EndDate = endDate };

                var result = await _service.GetAustralianSupplierStoreSalesDetailsAsync(
                    dateRange,
                    targetBranchCodes,
                    supplierCodes
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAustralianSupplierStoreSalesDetails failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取中国供应商分店销售明细
        /// GET api/react/v1/dashboard/china-supplier-store-sales-details
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="supplierCodes">供应商代码列表（可选）</param>
        /// <returns>中国供应商分店销售明细列表</returns>
        [HttpGet("china-supplier-store-sales-details")]
        public async Task<IActionResult> GetChinaSupplierStoreSalesDetails(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] List<string>? branchCodes = null,
            [FromQuery] List<string>? supplierCodes = null
        )
        {
            try
            {
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                if (branchCodes != null && branchCodes.Any())
                {
                    if (userBranchCodes != null)
                    {
                        targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                        if (!targetBranchCodes.Any())
                        {
                            return Ok(new { success = true, data = new List<object>() });
                        }
                    }
                    else
                    {
                        targetBranchCodes = branchCodes;
                    }
                }

                var dateRange = new DateRangeDto { StartDate = startDate, EndDate = endDate };

                var result = await _service.GetChinaSupplierStoreSalesDetailsAsync(
                    dateRange,
                    targetBranchCodes,
                    supplierCodes
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChinaSupplierStoreSalesDetails failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取销售商品明细（分页）
        /// GET api/react/v1/dashboard/sales-product-details
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="supplierCodes">供应商代码列表（可选）</param>
        /// <param name="pageIndex">页码（从1开始）</param>
        /// <param name="pageSize">每页记录数</param>
        /// <returns>分页销售商品明细</returns>
        [HttpGet("sales-product-details")]
        public async Task<IActionResult> GetSalesProductDetails(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] List<string>? branchCodes = null,
            [FromQuery] List<string>? supplierCodes = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 100
        )
        {
            try
            {
                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 权限校验：取请求分店与用户可访问分店的交集
                if (userBranchCodes != null && branchCodes != null && branchCodes.Any())
                {
                    targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                    if (!targetBranchCodes.Any())
                    {
                        return Ok(
                            new
                            {
                                success = true,
                                data = new PagedSalesProductDetailDto
                                {
                                    Data = new List<SalesProductDetailDto>(),
                                    Total = 0,
                                    PageIndex = pageIndex,
                                    PageSize = pageSize,
                                },
                            }
                        );
                    }
                }
                else
                {
                    targetBranchCodes = branchCodes;
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取销售商品明细
                var result = await _service.GetSalesProductDetailsAsync(
                    dateRange,
                    targetBranchCodes,
                    supplierCodes,
                    pageIndex,
                    pageSize
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetSalesProductDetails failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取增强版销售商品明细（包含折扣信息，分页）
        /// GET api/react/v1/dashboard/enhanced-sales-product-details
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="branchCodes">分店代码列表（可选）</param>
        /// <param name="localSupplierCodes">本地供应商代码列表（可选）</param>
        /// <param name="chinaSupplierCodes">中国供应商代码列表（可选）</param>
        /// <param name="pageIndex">页码（从1开始）</param>
        /// <param name="pageSize">每页记录数</param>
        /// <returns>分页增强版销售商品明细</returns>
        [HttpGet("enhanced-sales-product-details")]
        public async Task<IActionResult> GetEnhancedSalesProductDetails(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] List<string>? branchCodes = null,
            [FromQuery] List<string>? localSupplierCodes = null,
            [FromQuery] List<string>? chinaSupplierCodes = null,
            [FromQuery] int pageIndex = 1,
            [FromQuery] int pageSize = 100
        )
        {
            try
            {
                // 获取用户可访问的分店代码
                var userBranchCodes = await GetUserBranchCodesAsync();
                List<string>? targetBranchCodes = userBranchCodes;

                // 权限校验：取请求分店与用户可访问分店的交集
                if (userBranchCodes != null && branchCodes != null && branchCodes.Any())
                {
                    targetBranchCodes = branchCodes.Intersect(userBranchCodes).ToList();

                    if (!targetBranchCodes.Any())
                    {
                        return Ok(
                            new
                            {
                                success = true,
                                data = new PagedSalesProductDetailWithDiscountDto
                                {
                                    Data = new List<SalesProductDetailWithDiscountDto>(),
                                    Total = 0,
                                    PageIndex = pageIndex,
                                    PageSize = pageSize,
                                },
                            }
                        );
                    }
                }
                else
                {
                    targetBranchCodes = branchCodes;
                }

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取增强版销售商品明细
                var result = await _service.GetEnhancedSalesProductDetailsAsync(
                    dateRange,
                    targetBranchCodes,
                    localSupplierCodes,
                    chinaSupplierCodes,
                    pageIndex,
                    pageSize
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetEnhancedSalesProductDetails failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 获取指定商品在所有分店的销售数据
        /// GET api/react/v1/dashboard/product-sales-by-branches
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <param name="compareStartDate">对比开始日期（可选）</param>
        /// <param name="compareEndDate">对比结束日期（可选）</param>
        /// <param name="compareMode">对比模式</param>
        /// <param name="productCode">商品代码</param>
        /// <returns>商品在各分店的销售数据</returns>
        [HttpGet("product-sales-by-branches")]
        public async Task<IActionResult> GetProductSalesByAllBranches(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] DateTime? compareStartDate = null,
            [FromQuery] DateTime? compareEndDate = null,
            [FromQuery] CompareMode compareMode = CompareMode.ByDate,
            [FromQuery] string productCode = ""
        )
        {
            try
            {
                // 校验商品代码不能为空
                if (string.IsNullOrEmpty(productCode))
                    return BadRequest(new { success = false, message = "商品代码不能为空" });

                // 构建日期范围DTO
                var dateRange = new DateRangeDto
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    CompareStartDate = compareStartDate,
                    CompareEndDate = compareEndDate,
                    CompareMode = compareMode,
                };

                // 调用服务获取商品在各分店的销售数据
                var result = await _service.GetProductSalesByAllBranchesAsync(
                    dateRange,
                    productCode
                );
                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProductSalesByAllBranches failed");
                return StatusCode(500, new { success = false, message = "服务器内部错误" });
            }
        }

        /// <summary>
        /// 清除销售仪表板缓存
        /// POST api/react/v1/dashboard/cache/clear
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("cache/clear")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ClearCache()
        {
            try
            {
                await _cacheWarmer.ClearCacheAsync();
                return Ok(new { success = true, message = "缓存已清空" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ClearCache failed");
                return StatusCode(500, new { success = false, message = "清除缓存失败" });
            }
        }

        /// <summary>
        /// 预热销售仪表板缓存
        /// POST api/react/v1/dashboard/cache/warmup
        /// </summary>
        /// <param name="startDate">开始日期</param>
        /// <param name="endDate">结束日期</param>
        /// <returns>操作结果</returns>
        [HttpPost("cache/warmup")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> WarmUpCache(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate
        )
        {
            try
            {
                var dateRange = new DateRangeDto { StartDate = startDate, EndDate = endDate };

                await _cacheWarmer.WarmUpAsync(dateRange);
                return Ok(new { success = true, message = "缓存预热完成" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WarmUpCache failed");
                return StatusCode(500, new { success = false, message = "缓存预热失败" });
            }
        }
    }
}
