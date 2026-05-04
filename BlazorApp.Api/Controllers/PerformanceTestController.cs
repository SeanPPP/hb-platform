using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BlazorApp.Api.Filters;
using BlazorApp.Api.Utils;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Controllers
{
    /// <summary>
    /// 性能测试控制器
    /// 用于测试仓库商品API的查询性能
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    [Authorize(Roles = "Admin")] // 仅管理员可以执行性能测试
    [DevelopmentOnly]
    [Obsolete("Performance test controller. Keep disabled outside Development and remove after confirming no runtime usage.")]
    public class PerformanceTestController : ControllerBase
    {
        private readonly IWarehouseProductService _warehouseProductService;
        private readonly ILogger<PerformanceTestController> _logger;

        public PerformanceTestController(
            IWarehouseProductService warehouseProductService,
            ILogger<PerformanceTestController> logger)
        {
            _warehouseProductService = warehouseProductService;
            _logger = logger;
        }

        /// <summary>
        /// 执行基础查询性能测试
        /// </summary>
        /// <returns>性能测试结果</returns>
        [HttpPost("basic-query")]
        public async Task<IActionResult> RunBasicQueryPerformanceTest()
        {
            try
            {
                _logger.LogInformation("开始执行基础查询性能测试");

                var testHelper = new PerformanceTestHelper(_warehouseProductService,
                    HttpContext.RequestServices.GetRequiredService<ILogger<PerformanceTestHelper>>());

                var result = await testHelper.RunBasicQueryPerformanceTestAsync();
                var report = testHelper.GeneratePerformanceReport(result);

                _logger.LogInformation("基础查询性能测试完成，平均响应时间: {AverageTime}ms", result.AverageResponseTime);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        Result = result,
                        Report = report
                    },
                    Message = "基础查询性能测试完成"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "基础查询性能测试失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "性能测试失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 执行大数据量查询性能测试
        /// </summary>
        /// <returns>性能测试结果</returns>
        [HttpPost("large-data-query")]
        public async Task<IActionResult> RunLargeDataQueryPerformanceTest()
        {
            try
            {
                _logger.LogInformation("开始执行大数据量查询性能测试");

                var testHelper = new PerformanceTestHelper(_warehouseProductService,
                    HttpContext.RequestServices.GetRequiredService<ILogger<PerformanceTestHelper>>());

                var result = await testHelper.RunLargeDataQueryPerformanceTestAsync();
                var report = testHelper.GeneratePerformanceReport(result);

                _logger.LogInformation("大数据量查询性能测试完成，平均响应时间: {AverageTime}ms", result.AverageResponseTime);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        Result = result,
                        Report = report
                    },
                    Message = "大数据量查询性能测试完成"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "大数据量查询性能测试失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "性能测试失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 执行并发查询性能测试
        /// </summary>
        /// <param name="concurrentCount">并发数量，默认10</param>
        /// <returns>性能测试结果</returns>
        [HttpPost("concurrent-query")]
        public async Task<IActionResult> RunConcurrentQueryPerformanceTest([FromQuery] int concurrentCount = 10)
        {
            try
            {
                // 限制并发数量，避免过度压测
                if (concurrentCount < 1 || concurrentCount > 50)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "并发数量必须在1-50之间"
                    });
                }

                _logger.LogInformation("开始执行并发查询性能测试，并发数量: {ConcurrentCount}", concurrentCount);

                var testHelper = new PerformanceTestHelper(_warehouseProductService,
                    HttpContext.RequestServices.GetRequiredService<ILogger<PerformanceTestHelper>>());

                var result = await testHelper.RunConcurrentQueryPerformanceTestAsync(concurrentCount);
                var report = testHelper.GeneratePerformanceReport(result);

                _logger.LogInformation("并发查询性能测试完成，平均响应时间: {AverageTime}ms", result.AverageResponseTime);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        Result = result,
                        Report = report,
                        ConcurrentCount = concurrentCount
                    },
                    Message = $"{concurrentCount}个并发查询性能测试完成"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "并发查询性能测试失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "性能测试失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 执行完整性能测试套件
        /// </summary>
        /// <returns>完整性能测试结果</returns>
        [HttpPost("full-suite")]
        public async Task<IActionResult> RunFullPerformanceTestSuite()
        {
            try
            {
                _logger.LogInformation("开始执行完整性能测试套件");

                var testHelper = new PerformanceTestHelper(_warehouseProductService,
                    HttpContext.RequestServices.GetRequiredService<ILogger<PerformanceTestHelper>>());

                var results = new List<object>();
                var reports = new List<string>();

                // 1. 基础查询测试
                var basicTest = await testHelper.RunBasicQueryPerformanceTestAsync();
                var basicReport = testHelper.GeneratePerformanceReport(basicTest);
                results.Add(basicTest);
                reports.Add(basicReport);

                // 2. 大数据量查询测试
                var largeDataTest = await testHelper.RunLargeDataQueryPerformanceTestAsync();
                var largeDataReport = testHelper.GeneratePerformanceReport(largeDataTest);
                results.Add(largeDataTest);
                reports.Add(largeDataReport);

                // 3. 并发查询测试（5个并发）
                var concurrentTest = await testHelper.RunConcurrentQueryPerformanceTestAsync(5);
                var concurrentReport = testHelper.GeneratePerformanceReport(concurrentTest);
                results.Add(concurrentTest);
                reports.Add(concurrentReport);

                // 生成汇总报告
                var summaryReport = GenerateSummaryReport(results.Cast<PerformanceTestResult>().ToList());

                _logger.LogInformation("完整性能测试套件执行完成");

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        TestResults = results,
                        IndividualReports = reports,
                        SummaryReport = summaryReport,
                        TestDateTime = DateTime.UtcNow
                    },
                    Message = "完整性能测试套件执行完成"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完整性能测试套件执行失败");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = "性能测试失败，请稍后重试"
                });
            }
        }

        /// <summary>
        /// 获取性能测试建议
        /// </summary>
        /// <returns>性能优化建议</returns>
        [HttpGet("recommendations")]
        public IActionResult GetPerformanceRecommendations()
        {
            var recommendations = new
            {
                QueryOptimization = new[]
                {
                    "使用适当的索引来加速查询",
                    "避免在WHERE子句中使用函数",
                    "使用LIMIT限制返回的记录数",
                    "对于大数据量查询，考虑使用游标分页"
                },
                IndexSuggestions = new[]
                {
                    "在ProductName字段上创建索引以支持搜索",
                    "在CategoryGUID字段上创建索引以支持分类过滤",
                    "在Brand字段上创建索引以支持品牌过滤",
                    "在价格字段上创建复合索引以支持价格范围查询",
                    "在库存相关字段上创建复合索引以支持库存查询"
                },
                CachingStrategies = new[]
                {
                    "对分类树等相对静态的数据进行缓存",
                    "对统计数据进行定时缓存更新",
                    "使用Redis缓存热门查询结果",
                    "实现查询结果的本地内存缓存"
                },
                DatabaseOptimization = new[]
                {
                    "定期更新数据库统计信息",
                    "监控和分析慢查询日志",
                    "考虑数据库连接池优化",
                    "对于读写分离场景，考虑主从复制"
                },
                ApplicationOptimization = new[]
                {
                    "使用异步编程模式",
                    "实现合适的错误处理和重试机制",
                    "使用合适的HTTP缓存头",
                    "考虑实现API限流机制"
                }
            };

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = recommendations,
                Message = "性能优化建议"
            });
        }

        /// <summary>
        /// 生成汇总报告
        /// </summary>
        private string GenerateSummaryReport(List<PerformanceTestResult> results)
        {
            var report = new System.Text.StringBuilder();

            report.AppendLine("=== 性能测试套件汇总报告 ===");
            report.AppendLine($"测试时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine($"总测试套件数量: {results.Count}");
            report.AppendLine();

            var overallAverage = results.Average(r => r.AverageResponseTime);
            var overallMax = results.Max(r => r.MaxResponseTime);
            var overallMin = results.Min(r => r.MinResponseTime);

            report.AppendLine("整体性能指标:");
            report.AppendLine($"平均响应时间: {overallAverage:F2}ms");
            report.AppendLine($"最快响应时间: {overallMin}ms");
            report.AppendLine($"最慢响应时间: {overallMax}ms");
            report.AppendLine();

            report.AppendLine("各测试套件性能:");
            foreach (var result in results)
            {
                report.AppendLine($"- {result.TestName}: 平均 {result.AverageResponseTime:F2}ms (范围: {result.MinResponseTime}-{result.MaxResponseTime}ms)");
            }
            report.AppendLine();

            // 性能评估
            report.AppendLine("性能评估:");
            if (overallAverage < 50)
            {
                report.AppendLine("🟢 优秀 - 平均响应时间小于50ms");
            }
            else if (overallAverage < 100)
            {
                report.AppendLine("🟡 良好 - 平均响应时间在50-100ms之间");
            }
            else if (overallAverage < 200)
            {
                report.AppendLine("🟠 一般 - 平均响应时间在100-200ms之间，建议优化");
            }
            else
            {
                report.AppendLine("🔴 需要优化 - 平均响应时间超过200ms，强烈建议优化");
            }

            return report.ToString();
        }
    }
}
