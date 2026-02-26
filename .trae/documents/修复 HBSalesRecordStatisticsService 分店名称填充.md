# 修复 HBSalesRecordStatisticsService 分店名称填充

## 背景

`HBSalesRecordStatisticsService` 中 `BranchName` 字段只是简单地赋值为 `stat.BranchCode`（分店代码），而不是从 `Store` 表中读取真实的分店名称。需要添加分店名称映射功能。

## 实施步骤

### 1. 添加 Store 上下文注入

在 `HBSalesRecordStatisticsService` 类中添加 `MainSqlSugarContext` 依赖注入：

```csharp
private readonly MainSqlSugarContext _mainContext;

public HBSalesRecordStatisticsService(
    IServiceScopeFactory scopeFactory,
    ILogger<HBSalesRecordStatisticsService> logger,
    ScheduledTaskLogService taskLogService,
    MainSqlSugarContext mainContext  // 新增
)
{
    _scopeFactory = scopeFactory;
    _logger = logger;
    _taskLogService = taskLogService;
    _mainContext = mainContext;  // 新增
}
```

### 2. 修改 ProcessDateRange 方法

在 `ProcessDateRange` 方法中加载分店字典：

```csharp
var result = new HBSalesStatisticsResult();

try
{
    // 新增：加载分店字典
    var allBranchCodes = salesData.Select(d => d.BranchCode).Distinct().ToList();
    var stores = await _mainContext
        .Db.Queryable<Store>()
        .Where(s => allBranchCodes.Contains(s.StoreCode))
        .ToListAsync();
    var storeDict = stores.ToDictionary(s => s.StoreCode, s => s);
    
    _logger.LogInformation("加载了 {StoreCount} 个分店信息", storeDict.Count);
    
    // ... 原有的代码
```

将分店字典传递给各个统计处理方法：

```csharp
result.HourlyStatistics = await ProcessHourlyStoreStatistics(
    validSalesData,
    storeDict,  // 新增参数
    startDate,
    endDate
);
result.StoreStatistics = await ProcessStoreDateStatistics(
    validSalesData,
    storeDict,  // 新增参数
    startDate,
    endDate
);
```

### 3. 修改 ProcessHourlyStoreStatistics 方法

添加 `storeDict` 参数并使用它填充 `BranchName`：

```csharp
private async Task<List<HourlySalesStatistic>> ProcessHourlyStoreStatistics(
    List<SalesDataAggregate> salesData,
    Dictionary<string, Store> storeDict,  // 新增参数
    DateTime startDate,
    DateTime endDate
)
{
    // ... 现有代码
    
    foreach (var stat in groupedData)
    {
        var averageOrderValue =
            stat.CustomerCount > 0 ? stat.TotalAmount / stat.CustomerCount : 0m;

        var store = storeDict.GetValueOrDefault(stat.BranchCode);  // 新增：获取分店信息
        
        statisticsList.Add(
            new HourlySalesStatistic
            {
                Date = stat.Date,
                Hour = stat.Hour,
                BranchCode = stat.BranchCode,
                BranchName = store?.StoreName ?? stat.BranchCode ?? string.Empty,  // 修改：使用分店名称
                TotalAmount = stat.TotalAmount,
                TotalQuantity = (int)stat.TotalQuantity,
                CustomerCount = stat.CustomerCount,
                AverageOrderValue = averageOrderValue,
                UpdateTime = DateTime.Now,
            }
        );
    }
    
    return statisticsList;
}
```

### 4. 修改 ProcessStoreDateStatistics 方法

添加 `storeDict` 参数并使用它填充 `BranchName`：

```csharp
private async Task<List<StoreSalesStatistic>> ProcessStoreDateStatistics(
    List<SalesDataAggregate> salesData,
    Dictionary<string, Store> storeDict,  // 新增参数
    DateTime startDate,
    DateTime endDate
)
{
    // ... 现有代码
    
    foreach (var stat in groupedData)
    {
        var averageOrderValue =
            stat.OrderCount > 0 ? stat.TotalAmount / stat.OrderCount : 0m;

        var store = storeDict.GetValueOrDefault(stat.BranchCode);  // 新增：获取分店信息
        
        statisticsList.Add(
            new StoreSalesStatistic
            {
                Date = stat.Date,
                BranchCode = stat.BranchCode,
                BranchName = store?.StoreName ?? stat.BranchCode ?? string.Empty,  // 修改：使用分店名称
                TotalAmount = stat.TotalAmount,
                TotalQuantity = (int)stat.TotalQuantity,
                OrderCount = stat.OrderCount,
                CustomerCount = stat.CustomerCount,
                AverageOrderValue = averageOrderValue,
                UpdateTime = DateTime.Now,
            }
        );
    }
    
    return statisticsList;
}
```

## 修改文件

- `d:\Development\cline\blazor\BlazorApp.Api\Services\HBSalesRecordStatisticsService.cs`

## 预期效果

- `BranchName` 字段将显示真实的分店名称（从 `Store` 表读取）

- 如果找不到分店信息，则回退到分店代码（与 `SalesStatisticsJobService` 保持一致）

- 统计数据的分店信息将更加准确和完整

