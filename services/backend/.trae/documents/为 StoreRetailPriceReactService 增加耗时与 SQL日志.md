## 目标
- 在 `StoreRetailPriceReactService.cs` 的 `GetGridDataAsync` 与 `GetByUuidAsync` 内增加关键步骤耗时记录，并输出数据库执行的 SQL 语句与参数，便于分析。

## 方案
- 使用 `Stopwatch` 衡量：基础查询构建、应用过滤、排序、分页/计数/列表查询、方法总耗时。
- 通过 SqlSugar 的 `db.Aop.OnLogExecuting/OnLogExecuted` 捕获 SQL 与参数；在方法作用域内挂载，执行完立即恢复原有处理器以免影响全局。
- 为一次请求生成 `reqId`，所有日志带该标识，便于串联分析。
- 日志输出采用中文：包含“耗时(ms)”与“SQL/参数”。

## 修改点
- 顶部引入 `System.Diagnostics`。
- `GetGridDataAsync`：在分支（`ToPageListAsync`、`CountAsync`、`ToListAsync`）处分别记录耗时；方法起止记录总耗时；挂载并恢复 AOP 日志事件。
- `GetByUuidAsync`：记录单条查询耗时；挂载并恢复 AOP 日志事件。

## 验证
- 编译通过。
- 触发两方法时在日志中看到：SQL文本、参数列表、各步骤耗时（中文描述）。

请确认后我将实施以上修改并验证。