## 实施计划（更新版）

### 1. 创建实体类 (BlazorApp.Shared/Models/HBSalesRecord/)
- **SalesOrderMain.cs** - 对应 B销售清单主表副本
  - ID, 分店代码(B分店代码), 销售单号(B销售单号), 结账日期(B结账日期), 合计金额(B合计金额), 实收金额(B实收金额), 商品数量(B商品数量)等

- **SalesOrderDetailRecord.cs** - 对应 B销售清单详情表副本
  - ID, 分店代码(B分店代码), 销售单号(B销售单号), 产品编号(B产品编号), 商品名(B商品名), 单价(B单价), 数量(B数量), 合计金额(B合计金额)等

### 2. 创建数据库上下文 (BlazorApp.Api/Data/HBSalesRecordSqlSugarContext.cs)
- 连接到 HBSalesRecord (HOT_POS_CLOUD) 连接字符串
- 配置 SimpleClient 访问两个新实体表
- 添加数据库连接测试和表信息查询方法

### 3. 注册服务
- 在 Program.cs 中注册 `HBSalesRecordSqlSugarContext` 为 Scoped 服务

### 4. 创建统计服务 (BlazorApp.Api/Services/HBSalesRecordStatisticsService.cs)
新服务类，包含以下方法：

- `ImportAndStatistics2025Concurrent()`
  - 入口方法，启动2025年全年统计
  - 将2025-01-01至2025-12-31按10天分批
  - 使用10个并发处理

- `ProcessDateRangeConcurrent(List<DateRange> dateRanges, HBSalesRecordSqlSugarContext hbSalesContext, POSMSqlSugarContext posmContext, SqlSugarContext mainContext)`
  - 并发处理多个日期范围
  - 每个范围使用独立的Scope避免上下文冲突

- `ProcessDateRange(DateTime startDate, DateTime endDate, HBSalesRecordSqlSugarContext hbSalesContext, Dictionary<string, PosmProductSupplierMapping> supplierMapping)`
  - 处理单个日期范围
  - 从 HBSalesRecord 读取销售数据
  - 使用内存中的 supplierMapping 字典匹配供应商
  - 统计并返回结果

- `LoadSupplierMappingToMemory(POSMSqlSugarContext posmContext)`
  - 从 POSM 数据库加载所有 PosmProductSupplierMapping 到内存字典
  - 以 ProductCode 为 Key，便于快速匹配

- `UpdateStatisticsBatch(List<SupplierSalesStatistic> statistics, SqlSugarContext mainContext)`
  - 批量更新/插入统计数据到 SupplierSalesStatistic
  - 实现累加逻辑（已有记录累加，新记录插入）

### 5. 批处理策略
- 将2025年（365天）按10天分批 → 37个批次
- 前36批每批10天，最后一批5天
- 使用 `Parallel.ForEachAsync` 或 `Parallel.ForEach` 实现10个并发
- 每个并发任务创建独立的 ServiceScope

### 6. 数据处理流程
```
1. 启动时加载 PosmProductSupplierMapping 到内存（约10万条记录）
2. 按日期分批：2025-01-01~01-10, 01-11~01-20, ...
3. 10个并发处理：
   - 从 HBSalesRecord 读取销售数据（主表+详情表）
   - 用产品编号匹配内存中的供应商映射
   - 按 Date + SupplierCode 聚合统计
4. 收集所有并发结果后批量写入 SupplierSalesStatistic
5. 实现累加逻辑：相同 Date+SupplierCode 的记录累加金额和数量
```

### 7. 创建手动触发接口（可选）
- 在控制器中添加 API 端点：`POST /api/Statistics/ImportHBSales2025`
- 调用 `ImportAndStatistics2025Concurrent()` 方法
- 返回执行进度和结果

### 8. 技术要点
- **内存字典**: `Dictionary<string, PosmProductSupplierMapping>` 提高匹配速度
- **并发控制**: MaxDegreeOfParallelism = 10
- **上下文隔离**: 每个并发任务使用独立的 ServiceScope
- **累加逻辑**: 先汇总所有并发结果，再批量处理避免冲突
- **错误处理**: 记录失败日期，支持重试机制