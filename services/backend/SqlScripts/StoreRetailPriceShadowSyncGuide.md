# StoreRetailPrice 影子表全量同步说明

## 目标

这套脚本用于 SQL Server 上的 `StoreRetailPrice` HQ 全量同步场景，重点解决两件事：

1. `StoreRetailPrice_Shadow` 承接约 1000w 行全量数据装载
2. 装载失败或校验失败时，正式表 `StoreRetailPrice` 不受影响

切换策略是先在影子表完成全量导入和业务键校验，最后只在短事务里执行 `sp_getapplock + sp_rename`，把停顿窗口压到最小。

## 新增文件

- `SqlScripts/CreateStoreRetailPriceShadowInfrastructure.sql`

## 脚本包含内容

1. `dbo.StoreRetailPriceSyncRun`
   用于记录每次同步的状态、行数、重复键统计、备份表名和错误信息。

2. `dbo.usp_StoreRetailPriceShadow_Prepare`
   从正式表复制列结构，重建 `StoreRetailPrice_Shadow`，并补齐主键、业务键校验索引，以及与当前正式表习惯对齐的核心查询索引。

补充说明：
`SELECT TOP (0) INTO` 会复制列结构，但不会自动复制原表的触发器、默认约束和非主键索引。因此这份脚本已经补了核心主键和同步索引；如果正式表后续新增额外数据库对象，需要同步扩展这份脚本。

3. `dbo.usp_StoreRetailPriceShadow_Validate`
   对影子表执行业务键重复校验，默认校验未删除数据上的 `StoreCode + ProductCode + SupplierCode`。

4. `dbo.usp_StoreRetailPriceShadow_Swap`
   在极短事务里获取应用锁并切换表名，把原正式表保留为 `StoreRetailPrice_Backup_*`。

## 推荐执行顺序

```sql
DECLARE @SyncRunId BIGINT;

EXEC dbo.usp_StoreRetailPriceShadow_Prepare
    @SyncRunId = @SyncRunId OUTPUT,
    @TriggeredBy = N'HQ_FULL_SYNC';

-- 这里执行 HQ 全量装载，把数据写入 dbo.StoreRetailPrice_Shadow

EXEC dbo.usp_StoreRetailPriceShadow_Validate
    @SyncRunId = @SyncRunId,
    @SourceRowCount = NULL;

EXEC dbo.usp_StoreRetailPriceShadow_Swap
    @SyncRunId = @SyncRunId,
    @LockTimeoutMs = 10000;

SELECT TOP (20) *
FROM dbo.StoreRetailPriceSyncRun
ORDER BY SyncRunId DESC;
```

## 失败边界

- `Prepare` 失败: 只影响 `StoreRetailPrice_Shadow`，正式表不变
- 全量装载失败: 只影响 `StoreRetailPrice_Shadow`，正式表不变
- `Validate` 失败: 只标记当前同步失败，正式表不变
- `Swap` 失败: 事务回滚，正式表继续保持原状

也就是说，只有 `dbo.usp_StoreRetailPriceShadow_Swap` 成功提交后，正式表才会真正切换。

## 回滚示例

切换成功后，如果要回退到旧表，可从 `dbo.StoreRetailPriceSyncRun.BackupTableName` 取出对应的 `StoreRetailPrice_Backup_*` 名称，再执行脚本内提供的回滚示例。

示例思路：

1. 用 `sp_getapplock` 获取与切换相同的应用锁
2. 把当前 `StoreRetailPrice` 改名为临时回滚失败名
3. 把 `StoreRetailPrice_Backup_*` 改回 `StoreRetailPrice`
4. 提交事务

## 验证建议

上线前至少做下面几项验证：

1. 在测试库执行 `CreateStoreRetailPriceShadowInfrastructure.sql`，确认对象创建成功
2. 调用 `dbo.usp_StoreRetailPriceShadow_Prepare`，确认影子表、主键、索引和日志记录生成成功
3. 向 `StoreRetailPrice_Shadow` 人工插入一组重复业务键数据，调用 `dbo.usp_StoreRetailPriceShadow_Validate`，确认会失败且正式表未被切换
4. 用一批无重复数据执行 `Validate + Swap`，确认正式表切换成功且生成 `StoreRetailPrice_Backup_*`
5. 按回滚示例演练一次回退流程
