using System.Diagnostics;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Utils
{
    /// <summary>
    /// 性能测试辅助工具
    /// 用于测试和监控仓库商品API的查询性能
    /// </summary>
    public class PerformanceTestHelper
    {
        private readonly IWarehouseProductService _warehouseProductService;
        private readonly ILogger<PerformanceTestHelper> _logger;

        public PerformanceTestHelper(
            IWarehouseProductService warehouseProductService,
            ILogger<PerformanceTestHelper> logger)
        {
            _warehouseProductService = warehouseProductService;
            _logger = logger;
        }

        /// <summary>
        /// 执行基础查询性能测试
        /// </summary>
        public async Task<PerformanceTestResult> RunBasicQueryPerformanceTestAsync()
        {
            var results = new List<QueryTestCase>();

            // 测试用例1: 简单分页查询
            var simpleQuery = new WarehouseProductQueryDto { PageNumber = 1, PageSize = 20 };
            var result1 = await MeasureQueryPerformanceAsync("简单分页查询", 
                () => _warehouseProductService.GetPagedProductsAsync(simpleQuery));
            results.Add(result1);

            // 测试用例2: 关键字搜索
            var keywordQuery = new WarehouseProductQueryDto 
            { 
                PageNumber = 1, 
                PageSize = 20, 
                Keyword = "测试" 
            };
            var result2 = await MeasureQueryPerformanceAsync("关键字搜索查询", 
                () => _warehouseProductService.GetPagedProductsAsync(keywordQuery));
            results.Add(result2);

            // 测试用例3: 分类过滤查询
            var categoryQuery = new WarehouseProductQueryDto 
            { 
                PageNumber = 1, 
                PageSize = 20, 
                CategoryGUID = "test-category-guid",
                IncludeSubCategories = true
            };
            var result3 = await MeasureQueryPerformanceAsync("分类过滤查询", 
                () => _warehouseProductService.GetPagedProductsAsync(categoryQuery));
            results.Add(result3);

            // 测试用例4: 复合条件查询
            var complexQuery = new WarehouseProductQueryDto 
            { 
                PageNumber = 1, 
                PageSize = 20, 
                Keyword = "手机",
               
                MinPrice = 1000,
                MaxPrice = 5000,
                IsActive = true,
                HasStockAlert = false
            };
            var result4 = await MeasureQueryPerformanceAsync("复合条件查询", 
                () => _warehouseProductService.GetPagedProductsAsync(complexQuery));
            results.Add(result4);

            // 测试用例5: 统计查询
            var result5 = await MeasureQueryPerformanceAsync("统计查询", 
                () => _warehouseProductService.GetProductStatsAsync());
            results.Add(result5);

            // 测试用例6: 库存预警查询
            var result6 = await MeasureQueryPerformanceAsync("库存预警查询", 
                () => _warehouseProductService.GetStockAlertProductsAsync());
            results.Add(result6);

            return new PerformanceTestResult
            {
                TestName = "基础查询性能测试",
                TestCases = results,
                TestDateTime = DateTime.UtcNow,
                AverageResponseTime = results.Average(r => r.ResponseTimeMs),
                MaxResponseTime = results.Max(r => r.ResponseTimeMs),
                MinResponseTime = results.Min(r => r.ResponseTimeMs)
            };
        }

        /// <summary>
        /// 执行大数据量查询性能测试
        /// </summary>
        public async Task<PerformanceTestResult> RunLargeDataQueryPerformanceTestAsync()
        {
            var results = new List<QueryTestCase>();

            // 测试不同页码的查询性能
            var pageNumbers = new[] { 1, 10, 50, 100, 500 };
            
            foreach (var pageNumber in pageNumbers)
            {
                var query = new WarehouseProductQueryDto 
                { 
                    PageNumber = pageNumber, 
                    PageSize = 50 
                };
                
                var result = await MeasureQueryPerformanceAsync($"第{pageNumber}页查询", 
                    () => _warehouseProductService.GetPagedProductsAsync(query));
                results.Add(result);
            }

            // 测试不同页面大小的查询性能
            var pageSizes = new[] { 10, 20, 50, 100, 200 };
            
            foreach (var pageSize in pageSizes)
            {
                var query = new WarehouseProductQueryDto 
                { 
                    PageNumber = 1, 
                    PageSize = pageSize 
                };
                
                var result = await MeasureQueryPerformanceAsync($"每页{pageSize}条记录查询", 
                    () => _warehouseProductService.GetPagedProductsAsync(query));
                results.Add(result);
            }

            return new PerformanceTestResult
            {
                TestName = "大数据量查询性能测试",
                TestCases = results,
                TestDateTime = DateTime.UtcNow,
                AverageResponseTime = results.Average(r => r.ResponseTimeMs),
                MaxResponseTime = results.Max(r => r.ResponseTimeMs),
                MinResponseTime = results.Min(r => r.ResponseTimeMs)
            };
        }

        /// <summary>
        /// 执行并发查询性能测试
        /// </summary>
        public async Task<PerformanceTestResult> RunConcurrentQueryPerformanceTestAsync(int concurrentCount = 10)
        {
            var results = new List<QueryTestCase>();
            var tasks = new List<Task<QueryTestCase>>();

            var query = new WarehouseProductQueryDto { PageNumber = 1, PageSize = 20 };

            // 创建并发任务
            for (int i = 0; i < concurrentCount; i++)
            {
                var taskIndex = i;
                var task = MeasureQueryPerformanceAsync($"并发查询-{taskIndex + 1}", 
                    () => _warehouseProductService.GetPagedProductsAsync(query));
                tasks.Add(task);
            }

            // 等待所有任务完成
            var taskResults = await Task.WhenAll(tasks);
            results.AddRange(taskResults);

            return new PerformanceTestResult
            {
                TestName = $"{concurrentCount}个并发查询性能测试",
                TestCases = results,
                TestDateTime = DateTime.UtcNow,
                AverageResponseTime = results.Average(r => r.ResponseTimeMs),
                MaxResponseTime = results.Max(r => r.ResponseTimeMs),
                MinResponseTime = results.Min(r => r.ResponseTimeMs)
            };
        }

        /// <summary>
        /// 测量查询性能
        /// </summary>
        private async Task<QueryTestCase> MeasureQueryPerformanceAsync<T>(string testName, Func<Task<T>> queryFunc)
        {
            var stopwatch = Stopwatch.StartNew();
            Exception? exception = null;
            var success = false;
            var resultCount = 0;

            try
            {
                var result = await queryFunc();
                success = true;

                // 尝试获取结果数量
                if (result is WarehouseProductPagedResultDto pagedResult)
                {
                    resultCount = pagedResult.Items?.Count ?? 0;
                }
                else if (result is IEnumerable<object> enumerable)
                {
                    resultCount = enumerable.Count();
                }
                else if (result != null)
                {
                    resultCount = 1;
                }
            }
            catch (Exception ex)
            {
                exception = ex;
                _logger.LogError(ex, "查询性能测试失败: {TestName}", testName);
            }
            finally
            {
                stopwatch.Stop();
            }

            var testCase = new QueryTestCase
            {
                TestName = testName,
                ResponseTimeMs = stopwatch.ElapsedMilliseconds,
                Success = success,
                ResultCount = resultCount,
                ErrorMessage = exception?.Message
            };

            _logger.LogInformation("性能测试完成: {TestName}, 耗时: {ResponseTime}ms, 成功: {Success}, 结果数量: {ResultCount}",
                testName, testCase.ResponseTimeMs, success, resultCount);

            return testCase;
        }

        /// <summary>
        /// 生成性能测试报告
        /// </summary>
        public string GeneratePerformanceReport(PerformanceTestResult result)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine($"=== {result.TestName} ===");
            report.AppendLine($"测试时间: {result.TestDateTime:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine($"测试用例数量: {result.TestCases.Count}");
            report.AppendLine($"平均响应时间: {result.AverageResponseTime:F2}ms");
            report.AppendLine($"最快响应时间: {result.MinResponseTime}ms");
            report.AppendLine($"最慢响应时间: {result.MaxResponseTime}ms");
            report.AppendLine();

            report.AppendLine("详细结果:");
            foreach (var testCase in result.TestCases)
            {
                var status = testCase.Success ? "✓" : "✗";
                report.AppendLine($"{status} {testCase.TestName}: {testCase.ResponseTimeMs}ms ({testCase.ResultCount} 条结果)");
                
                if (!testCase.Success && !string.IsNullOrEmpty(testCase.ErrorMessage))
                {
                    report.AppendLine($"   错误: {testCase.ErrorMessage}");
                }
            }

            report.AppendLine();
            report.AppendLine("性能分析:");
            
            var slowQueries = result.TestCases.Where(tc => tc.ResponseTimeMs > 200).ToList();
            if (slowQueries.Any())
            {
                report.AppendLine($"⚠️  发现 {slowQueries.Count} 个慢查询 (>200ms):");
                foreach (var slow in slowQueries)
                {
                    report.AppendLine($"   - {slow.TestName}: {slow.ResponseTimeMs}ms");
                }
            }
            else
            {
                report.AppendLine("✓ 所有查询响应时间都在200ms以内");
            }

            var failedQueries = result.TestCases.Where(tc => !tc.Success).ToList();
            if (failedQueries.Any())
            {
                report.AppendLine($"❌ 发现 {failedQueries.Count} 个失败查询:");
                foreach (var failed in failedQueries)
                {
                    report.AppendLine($"   - {failed.TestName}: {failed.ErrorMessage}");
                }
            }
            else
            {
                report.AppendLine("✓ 所有查询都执行成功");
            }

            return report.ToString();
        }
    }

    /// <summary>
    /// 性能测试结果
    /// </summary>
    public class PerformanceTestResult
    {
        public string TestName { get; set; } = string.Empty;
        public DateTime TestDateTime { get; set; }
        public List<QueryTestCase> TestCases { get; set; } = new List<QueryTestCase>();
        public double AverageResponseTime { get; set; }
        public long MaxResponseTime { get; set; }
        public long MinResponseTime { get; set; }
    }

    /// <summary>
    /// 查询测试用例
    /// </summary>
    public class QueryTestCase
    {
        public string TestName { get; set; } = string.Empty;
        public long ResponseTimeMs { get; set; }
        public bool Success { get; set; }
        public int ResultCount { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
