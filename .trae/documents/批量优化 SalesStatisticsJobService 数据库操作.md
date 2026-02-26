## 优化目标

解决 5 个方法中的 N+1 查询问题，将循环中的多次数据库操作优化为批量操作

## 需要优化的方法

1. `UpdateSupplierStatistics` (第 382-499 行) - 供应商统计
2. `UpdateStoreStatistics` (第 314-380 行) - 分店统计
3. `UpdateSupplierStatistics` 重载 (第 661-820 行) - 指定供应商统计
4. `UpdateStoreSupplierStatistics` (第 1004-1170 行) - 门店供应商统计
5. `UpdateStoreStatistics` 重载 (第 580-659 行) - 指定分店统计

## 优化策略（参考 UpdateHourlyStatistics 第 240-298 行的正确实现）

### 步骤 1：一次性查询所有已存在的记录

```csharp
var existingRecords = await _context
    .Db.Queryable<YourStatistic>()
    .Where(s => s.Date == targetDate && codes.Contains(s.Code))
    .ToListAsync();
```

### 步骤 2：构建字典便于查找

```csharp
var existingDict = existingRecords.ToDictionary(
    s => $"{s.Date}_{s.Code}",
    s => s
);
```

### 步骤 3：准备新数据（在内存中构建统计对象列表）

```csharp
var statisticsList = new List<YourStatistic>();
foreach (var data in rawData)
{
    statisticsList.Add(new YourStatistic { ... });
}
```

### 步骤 4：分离插入和更新

```csharp
var toInsert = new List<YourStatistic>();
var toUpdate = new List<YourStatistic>();

foreach (var stat in statisticsList)
{
    var key = $"{stat.Date}_{stat.Code}";
    
    if (existingDict.TryGetValue(key, out var existing))
    {
        stat.Id = existing.Id;  // 保留主键
        toUpdate.Add(stat);
    }
    else
    {
        toInsert.Add(stat);
    }
}
```

### 步骤 5：批量插入和更新

```csharp
if (toInsert.Any())
{
    await _context.Db.Insertable(toInsert).ExecuteCommandAsync();
}

if (toUpdate.Any())
{
    await _context.Db.Updateable(toUpdate).ExecuteCommandAsync();
}
```

## 预期效果

* **优化前**：50-200 条记录需要 150-400 次数据库操作

* **优化后**：50-200 条记录仅需 2-4 次数据库操作（查询 + 插入 + 更新）

* **性能提升**：约 95%+ 的数据库操作减少

