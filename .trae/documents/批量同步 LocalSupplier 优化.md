## 原因分析
- 当前实现在事务内对每条记录执行：读取是否存在 → 插入/更新（`foreach` 中每次命中数据库）。这会产生 N+1 次往返、锁持有时间长、吞吐低。
- `DIC_供应商信息表` 无时间戳字段，`since` 目前无法生效，导致每次都全量扫描。
- 插入/更新逐条执行，没有利用 SqlSugar 的批量写入能力（`Insertable(list)`、`Updateable(list)` 或 `Fastest<T>`）。

## 优化目标
- 移除循环内的数据库读写，将数据库写入改为批量。
- 单次读取本地已有供应商，内存分流出待新增和待更新集合。
- 在同一个事务中批量插入、批量更新，显著降低往返成本并提升吞吐。

## 改动点（仅修改 `SyncFromDicAsync`）
- 读取 HQ 源表全部数据后先过滤无效记录，统计 `SkippedCount`。
- 一次性读取本地未删除的供应商形成字典（key=`LocalSupplierCode`）。
- 在内存中构建 `toInsert`、`toUpdate` 两个列表；不在 `foreach` 中执行任何数据库操作。
- 事务内批量写入：
  - 默认使用 `db.Insertable(toInsert).ExecuteCommandAsync()` 和 `db.Updateable(toUpdate).UpdateColumns(...).ExecuteCommandAsync()`。
  - 如数据量很大（>5万），可切换 SQL Server 专用的 `db.Fastest<HBLocalSupplier>().BulkCopyAsync(...) / BulkUpdate(...)` 以进一步提升速度。

## 代码示例（替换 try {...} 内部主体，保持现有事务与返回结构）
```csharp
var dicList = await hb.Queryable<DIC_供应商信息表>().ToListAsync();
var now = DateTime.UtcNow;

var valid = dicList.Where(d =>
    !string.IsNullOrWhiteSpace(d.H供应商编码) &&
    !string.IsNullOrWhiteSpace(d.H供应商名称)
).ToList();
result.SkippedCount += dicList.Count - valid.Count;

var existing = await db.Queryable<HBLocalSupplier>()
    .Where(x => !x.IsDeleted)
    .ToListAsync();
var map = existing.ToDictionary(x => x.LocalSupplierCode, x => x);

var toInsert = new List<HBLocalSupplier>();
var toUpdate = new List<HBLocalSupplier>();

foreach (var d in valid)
{
    var code = d.H供应商编码!;
    var name = d.H供应商名称!;
    if (!map.TryGetValue(code, out var e))
    {
        toInsert.Add(new HBLocalSupplier
        {
            Guid = Guid.NewGuid().ToString(),
            LocalSupplierCode = code,
            Name = name,
            Status = 1,
            ContactPerson = d.H联系人,
            Email = d.HEMAIL地址,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = "System",
            UpdatedBy = "System",
            IsDeleted = false,
        });
    }
    else
    {
        e.Name = name;
        e.ContactPerson = d.H联系人;
        e.Email = d.HEMAIL地址;
        if (overwrite) e.Status = 1;
        e.UpdatedAt = now;
        e.UpdatedBy = "System";
        toUpdate.Add(e);
    }
}

await db.Ado.BeginTranAsync();
try
{
    if (toInsert.Count > 0)
        await db.Insertable(toInsert).ExecuteCommandAsync();
        // 如数据量极大且为 SQL Server，可用：await db.Fastest<HBLocalSupplier>().BulkCopyAsync(toInsert);

    if (toUpdate.Count > 0)
        await db.Updateable(toUpdate)
            .UpdateColumns(x => new { x.Name, x.ContactPerson, x.Email, x.Status, x.UpdatedAt, x.UpdatedBy })
            .ExecuteCommandAsync();
        // SQL Server 可选：await db.Fastest<HBLocalSupplier>().BulkUpdateAsync(toUpdate);

    result.CreatedCount += toInsert.Count;
    result.UpdatedCount += toUpdate.Count;

    await db.Ado.CommitTranAsync();
    return ApiResponse<LocalSupplierSyncResultDto>.OK(result, "同步完成");
}
catch (Exception ex)
{
    await db.Ado.RollbackTranAsync();
    _logger.LogError(ex, "LocalSupplier 同步失败");
    result.Errors.Add(ex.Message);
    return ApiResponse<LocalSupplierSyncResultDto>.Error("同步失败", "SYNC_ERROR", result);
}
```

## 说明
- 上述 `foreach` 仅在内存中分类，不进行数据库操作，满足“不要在循环里操作数据库”的要求。
- `Updateable(toUpdate)` 会按实体的主键 `Guid` 执行批量更新，`UpdateColumns` 限定仅更新必要字段，减少写入开销。
- 若启用 `Fastest<T>`，需确认使用 SQL Server 提供程序；否则使用默认批量写入即可。
- 目前源表缺少时间字段，`since` 暂保留参数但不启用过滤；未来若加上时间戳，可在 HQ 查询处追加 `WhereIF(since != null, x => x.更新时间 >= since)`。

## 验证计划
- 构造 1k、10k、50k 三档数据进行对比测试，记录总体耗时和每秒吞吐。
- 验证 `result.CreatedCount/UpdatedCount/SkippedCount` 与实际批量大小一致。
- 检查唯一约束冲突（`uk_local_supplier_code`）是否按预期避免；必要时在内存构建阶段去重。

## 可能风险与处理
- 批量数据过大导致单次 SQL 过长：可将 `toInsert/toUpdate` 分批（如每 5000 条一批）循环提交，但每批仍为一次数据库写入。
- 事务时间仍然较长：可根据数据量动态分批并在日志中记录批次大小，便于后续调优。

请确认是否按上述方案实施（默认使用批量 `Insertable/Updateable`；如数据库为 SQL Server 并且数据量很大，可开启 `Fastest<T>`）。