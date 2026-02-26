## 目标
- 将以下 HQ 表的数据源从 `HqSqlSugarContext` 切换为 `HBSalesSqlSugarContext`：
  - `CBP_DIC_国内供应商信息表`
  - `CPT_DIC_货号前缀信息表`
  - `CPT_DIC_商品信息字典表`
  - `CPT_DIC_商品套装信息表`
- 保持现有分页、过滤、批量写入、事务与日志行为不变。

## 代码改动
### 1. 服务构造函数注入
- 文件：`BlazorApp.Api/Services/React/DataSyncReactService.cs`
- 变更：在构造函数新增注入 `HBSalesSqlSugarContext hbSalesContext` 并保存为 `_hbSalesContext` 字段。

### 2. 切换数据源（四个方法）
- 方法与文件位置：
  - `SyncDomesticProductsFromHqAsync`（商品字典）
  - `SyncDomesticSetProductsFromHqAsync`（套装信息）
  - `SyncProductPrefixCodesFromHqAsync`（货号前缀）
  - `SyncChinaSuppliersFromHqAsync`（国内供应商）
- 变更点：
  - 统计总数、分页查询与过滤改为使用 `_hbSalesContext.Db.Queryable<...>`。
  - 若原代码使用 `HqSqlSugarContext.CreateConcurrentConnection`，改为直接用 `_hbSalesContext.Db`（`SqlSugarScope` 线程安全）。
  - 其余映射与本地写入逻辑保持不变。

### 3. 连接与配置
- 确认 `appsettings.json` 存在 `HBSalesConnection` 并已在 `Program.cs` 注册 `HBSalesSqlSugarContext`（如 `AddSingleton`/`AddScoped`）。
- 无需新增并发连接方法，使用 `SqlSugarScope` 的 `_hbSalesContext.Db` 即可满足并发读取。

## 注意与验证
- 保持现有 `WhereIF` 与子查询过滤（如使用状态等）原样，避免数据质量问题。
- 编译与构建：`dotnet build` 成功；接口调用四个端点进行验证，日志仍输出批次统计与汇总。
- 不影响其他方法（如分店价格、分店多码、套装多码、货柜详情）。

## 交付
- 完成四个方法的数据源切换与构造函数改造；通过构建与基本接口自测。

请确认以上计划，确认后我将立即实施改动与验证。