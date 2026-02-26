# 修改 HBSalesRecordStatisticsService 合并 HBSales 和 POSM 数据源

## 背景

当前 `HBSalesRecordStatisticsService` 只从 HBSales 数据库读取销售数据，需要同时从 POSM 数据库读取数据，然后合并生成统计。这样可以确保 2025 年部分分店新老数据库有同时使用日期的数据完整统计。

## 实施步骤

### 1. 修改 ProcessDateRange 方法签名

添加 `POSMSqlSugarContext` 参数：

```csharp
private async Task<HBSalesStatisticsResult> ProcessDateRange(
    DateTime startDate,
    DateTime endDate,
    HBSalesRecordSqlSugarContext hbSalesContext,
    POSMSqlSugarContext posmContext,  // 新增参数
    Dictionary<string, PosmProductSupplierMapping> supplierMapping
)
```

### 2. 修改 ImportAndStatistics2025Concurrent 方法

添加 POSM 上下文：

```csharp
using var scope = _scopeFactory.CreateScope();
var hbSalesContext = scope.ServiceProvider.GetRequiredService<HBSalesRecordSqlSugarContext>();
var posmContext = scope.ServiceProvider.GetRequiredService<POSMSqlSugarContext>();  // 新增

var statistics = await ProcessDateRange(
    dateRange.StartDate,
    dateRange.EndDate,
    hbSalesContext,
    posmContext,  // 传入
    supplierMapping
);
```

### 3. 修改统计处理方法

为每个统计处理方法添加 POSM 数据读取和合并逻辑：

#### ProcessDailyStatistics()
- 从 HBSales 读取原始数据
- 从 POSM 读取原始数据（参考 SalesStatisticsJobService 的查询逻辑）
- 合并两个数据源的数据
- 生成统计

#### ProcessHourlyStoreStatistics()
- 从 HBSales 读取原始数据
- 从 POSM 读取原始数据
- 合并数据
- 生成统计

#### ProcessStoreDateStatistics()
- 从 HBSales 读取原始数据
- 从 POSM 读取原始数据
- 合并数据
- 生成统计

#### ProcessSupplierDateStatistics()
- 从 HBSales 读取原始数据
- 从 POSM 读取原始数据
- 合并数据
- 生成统计

#### ProcessStoreSupplierStatistics()
- 从 HBSales 读取原始数据
- 从 POSM 读取原始数据
- 合并数据
- 生成统计

### 4. 数据合并逻辑

对于每种统计类型，定义合并方法：

```csharp
private List<SalesDataAggregate> MergeSalesData(
    List<SalesOrderDetailRecord> hbSalesData,
    List<SalesOrderDetail> posmData,
    DateTime startDate,
    DateTime endDate
)
{
    var merged = new List<SalesDataAggregate>();
    
    // 添加 HBSales 数据
    foreach (var record in hbSalesData)
    {
        merged.Add(new SalesDataAggregate
        {
            Date = record.B结账日期!.Value.Date,
            BranchCode = record.B分店代码,
            ProductCode = record.B产品编号,
            TotalAmount = record.B合计金额 ?? 0m,
            TotalQuantity = record.B数量 ?? 0m,
            OrderCount = 1,
            CustomerCount = 1,
        });
    }
    
    // 添加 POSM 数据
    foreach (var order in posmData)
    {
        merged.Add(new SalesDataAggregate
        {
            Date = order.OrderTime!.Value.Date,
            BranchCode = order.BranchCode,
            ProductCode = order.ProductCode,
            TotalAmount = order.TotalAmount - order.DiscountAmount,
            TotalQuantity = order.ItemCount,
            OrderCount = 1,
            CustomerCount = 1,
        });
    }
    
    return merged;
}
```

### 5. 修改批量更新方法

确保所有批量更新方法都使用覆盖更新而非累加：

```csharp
// 覆盖更新示例
existing.TotalAmount = stat.TotalAmount;  // 覆盖，不是累加
existing.TotalQuantity = stat.TotalQuantity;
```

## 修改文件

- `BlazorApp.Api/Services/HBSalesRecordStatisticsService.cs`

## 预期效果

- HBSalesRecordStatisticsService 将同时从 HBSales 和 POSM 数据源读取数据

- 合并后的数据完整覆盖 2025 年的所有销售记录

- 统计数据采用覆盖更新方式，确保数据一致性

- 类似于 SalesStatisticsJobService 的处理方式

