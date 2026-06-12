修改 `StoreSyncService.cs` 文件：

1. **更新 SQL 查询**：在 `GetHqBranchesWithSqlAsync` 方法的 SQL 查询中添加 `BusinessNumber` 字段

2. **新增分店同步**：在 `SyncSingleStoreAsync` 方法的新增部分添加：

   * `ABN = hqBranch.BusinessNumber`

   * `BrandName = "Hot Bargain"`（默认值）

3. **更新分店同步**：在 `SyncSingleStoreAsync` 方法的更新部分添加：

   * 检查并更新 `ABN` 字段（当值发生变化时）

   * 检查并更新 `BrandName` 字段（当值发生变化时，默认为 "Hot Bargain"）

