## 问题定位
- OnLogExecuting/OnLogExecuted 为事件（event），不可读取当前委托或直接赋值；我们尝试读取与赋值导致 “缺少 get 访问器” 编译错误。
- 多处日志长行触发代码风格告警，建议按规则换行缩进。
- `using System.Diagnostics` 必需（Stopwatch 使用），保留；若代码风格误报，将在文件顶部统一整理 using。

## 修复方案
- 事件订阅/取消：
  - 使用局部委托 `logExec`、`logExecuted`，通过 `db.Aop.OnLogExecuting += logExec; db.Aop.OnLogExecuted += logExecuted;` 订阅；
  - 在所有返回路径和异常路径中用 `-= logExec` / `-= logExecuted` 取消订阅（try/finally 保障释放）。
  - 不读取或覆盖已有事件处理器，避免 get/set 报错。
- 日志格式优化：
  - 将长字符串参数换行：
    - 参数日志 `string.Join(... pars.Select(...))` 按规则拆分多行并缩进。
    - 分页/计数/列表耗时日志同样拆分。
- 代码范围：仅修改 `StoreRetailPriceReactService.cs`，不触碰其他告警文件。

## 具体改动点
- `GetGridDataAsync`：
  - 在方法开始创建 `reqId` 和 `Stopwatch`；订阅 AOP 事件；在 `requiresJoinCount` 分支与 else 分支的返回前以及异常 catch 前，均取消订阅；
  - 保留现有分段耗时记录（基础查询/过滤/排序/分页或计数/列表/总耗时），调整为多行日志。
- `GetByUuidAsync`：
  - 同样订阅与取消 AOP 事件；记录单次详情查询耗时；格式化日志。

## 验证
- 重新编译应不再出现 “缺少 get 访问器” 错误；
- 运行查询时，在日志中看到：SQL 文本、参数、分段耗时、总耗时；
- 代码风格告警消失或显著减少。

确认后我将执行以上修改并进行编译与运行验证。