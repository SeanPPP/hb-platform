# 折扣日快照来源查询索引

批量货号销量的折扣回填读取 `HOT_POS_CLOUD` 历史成交明细。2026-09-15 的只读核验发现，约 1,166 万行明细只有 ID 索引；按日取明细后关联销售主表缺少对应访问路径，部分日期超过三分钟来源读取预算。

本目录六个 SQL 脚本为这次修复保留可审查、可回退的运维记录，不由应用启动或迁移自动执行：

- `BatchProductSalesDiscountSourceIndexes.Preflight.sql`：只读核验实例、数据库 GUID、列、既有索引及数据文件、日志和物理卷余量。
- `BatchProductSalesDiscountSourceIndexes.sql`：先建明细的 `(B结账日期, B销售单号)` 索引，再建主表的 `(B销售单号, B结账日期)` 索引。
- `BatchProductSalesDiscountSourceIndexes.ResumeMain.sql`：明细已提交、主表尚未创建时专用，先精确验证明细定义与归属，再仅创建主表索引并输出容量检查值。
- `BatchProductSalesDiscountSourceIndexes.Rollback.sql`：先验证所有仍存在的候选索引定义及专属归属标记，再删除本次创建的索引。
- `BatchProductSalesDiscountSourceIndexes.CoverProductName.sql`：将既有明细索引由 v1 原位升级为覆盖 `B商品名` 的 v2。
- `BatchProductSalesDiscountSourceIndexes.CoverProductName.Rollback.sql`：仅从精确 v2 回退至 v1。

创建脚本严格限定已核验实例与数据库 GUID。两组“创建索引＋归属标记”各自为一个事务，首组失败后不继续第二组。使用 ONLINE、MAXDOP 1 和低优先级锁等待；无法获取锁时自行放弃，不终止业务会话。每组创建前重新核验数据、日志与物理卷余量。

执行前保留只读预检输出和精确脚本 SHA-256，执行器必须遇错停止。开始前须再次确认没有活动的长请求或阻塞链，再进入维护窗口；低优先级锁等待只能降低锁影响，不能替代该门禁或 I/O 余量判断。不要使用短客户端命令超时取消大型索引构建；执行过程中独立观察请求、阻塞、日志增长和物理卷余量。FULL 恢复模型下完成重建后，必须在同一维护窗口执行日志备份，并重新只读核验 `log_reuse_wait`、日志/共享卷余量以及 v2 索引定义和 owner。若中断，先查索引与归属元数据，禁止直接重复整份脚本。

本次索引主要改善按日期取明细和主表关联。退货原单证据查询使用规范化单号且没有日期谓词，其耗时仍需通过真实任务验证；索引创建成功本身不能证明所有来源查询已经加速。

回填验收以日期状态 Fresh、实际快照行数和发布来源版本一致为准；页面可访问、队列处于 Running 或任务日志已开始均不代表某日期已完成。

## 商品名覆盖版本（v1 → v2）

`BatchProductSalesDiscountSourceIndexes.CoverProductName.sql` 仅以 `DROP_EXISTING` 原位重建既有明细索引，并新增 `B商品名` include，归属标记从 v1 更新为 `hb-discount-source-index-repair-20260915:v2-productname`。其 rollback 脚本只接受精确 v2 前态，恢复原 14 个 include 和 v1 标记。两个脚本均按真实 heap/clustered 行数、现有目标索引 reserved space、`nvarchar(500)` 的最大 1000 bytes、每行 16 bytes 和 1.25 安全系数计算本次 build 所需容量；分别核验 data/log 逻辑容量；FULL 恢复模型下日志按两倍 build 预留；data/log 共用物理卷时按实际 data 增长和双倍 build 的日志增长之和，再保留 8192MB 运营余量。生产已核验 data 文件 `max_size=-1`、log 文件 `max_size=268435456`，两者均为固定 8192 页（64MB）增长；任一值变化即拒绝执行。旧的跨所有索引 `SUM(row_count)` 不可用于该估算。

来源阶段预算为 4 分钟，包含首次来源签名 capture、事实聚合和发布围栏的第二次 capture；它不是单条 HBSales SQL 的预算。单日处理上限为 6 分钟，严格小于 10 分钟日租约，也小于 15 分钟全局租约。新增的来源签名和事实聚合 elapsed 日志用于验收各阶段，缓存中约 28 秒的单条 HBSales 查询不能代表整个来源阶段。
