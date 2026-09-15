# 批量货号销量提速：本地验收记录

日期：2026-09-15。代码基线 `a54013b853756b13ff48476ea02b16c5462afbdc`，工作分支 `codex/batch-sales-partial-dates-20260915`。保留同一隔离工作区此前的部分日期展示改动。

## 交付行为

- 首屏 `/query` 返回商品、coverage 和 overview；只读取两类销量事实聚合（日期＋商品、分店＋商品），不读折扣快照。
- 点击商品调用 `/detail`；点击分店调用 `/overview/branch`。总览及选中分店各通过一次 `/overview/discounts` 补齐分类，取消全量商品详情加载链及五秒轮询。
- 后续请求携带 `coverageVersion + readyDates`，前端核验商品集合、实际门店集合、查询日期及覆盖锁。409 保留结果；最终 401/403 清除查询和缓存。
- 同一读取期间发生变化的日期从摘要排除；分店汇总按稳定日期重新查询，再次变化返回409。共享后台严格状态检查保持原语义。
- 未统计日期保留日期轴与缺口；真实零与未统计分别显示0和不可用。首屏可靠销量不被折扣补齐覆盖。
- 导出每批最多20商品，批次立即折叠为日期＋分店合计。CSV临时文件写完后再次核验版本，控制器下发前重读权限，失败或取消清理本次文件。正文保留三个汇总区段、日期覆盖说明、空白待统计日及安全CSV转义。
- “导出当前详情”使用当前商品视图：全部商品视图导出结果中的全部商品，单商品视图只导出该商品；两者均保留已应用的实际授权门店和覆盖锁，不受草稿查询或分店钻取缩小范围。

## 性能方法和结果

本机隔离 SQL Server，随机临时数据库，638,000 条日统计、11,000 个日折扣快照；58店，2026-08-17至2026-09-07，共22天。每商品每店每天数量2、金额9。无应用结果缓存，数据库已预热。

计时范围为服务方法，**不含 HTTP、浏览器、实际部署硬件和公网耗时**。总览/分店/单品各20样本；折扣3样本，P95在该小样本中等于最大值。不能据此宣称生产P95已达标或相较旧发布版降低60%。

| 商品数 | 总览 P95 | 分店 P95 | 后台折扣 P95（3样本） | 总览响应大小 |
|---:|---:|---:|---:|---:|
| 10 | 101.8 ms | 31.2 ms | 307.9 ms | 22,908 B |
| 89 | 291.2 ms | 92.3 ms | 2513.8 ms | 36,582 B |
| 500 | 432.0 ms | 181.2 ms | 14066.0 ms | 106,708 B |

单商品详情 P95：63.3 ms。每次总览7条SQL，其中事实查询固定2条、快照查询0条；分店5条SQL，其中事实1条。折扣10条SQL，其中事实、快照、折扣状态各1条。

500商品后台折扣在去除逐商品重复扫描后，由同机首轮最大28,080.3 ms降至14,066.0 ms。仍需处理约330 MB快照JSON，这部分后台等待仍明显，但不阻塞首屏销量。JSON纯反序列化单次约795.3 ms，不能把剩余耗时全部归因于解析或数据库执行；SQL计时回调也不覆盖全部传输和物化成本。

## 正确性验证

- 后端相关回归共93项：首轮92项通过；剩余一项导出版本冲突测试原本在SQL结果物化后才改变版本，修正为最终核验SELECT之前改变版本后，与取消清理测试一起定向重跑，2项均通过。未跳过SQL集成测试。仅调整测试注入时序，没有放宽冲突断言。
- 单独性能测试1项通过，验证总览事实SQL固定2条、折扣快照/状态各批量读取1次，并核对销量和金额。
- 使用后端真实序列化响应，通过前端实际服务层解析总览、单品、分店、两种折扣和CSV，6项契约检查通过。
- 前端专项覆盖导入、聚合、缺口图表、服务字段及销售页面权限；类型检查和Web构建通过。
- 最后的导出范围修复追加ALL/单商品实际请求scope断言，前端专项和类型检查再次通过；最终Web构建通过，Vite阶段10.31秒。独立审查确认使用query响应中的已应用日期和门店范围。
- 浏览器使用真实页面组件和可控制延迟/错误的模拟接口，37项检查通过，覆盖10/89/500商品首屏请求、28/30天、全pending、真实零、退货、按需缓存、旧响应晚到、409、401/403、刷新保留选择和CSV取消。额外验证折扣失败保留可靠销量、取消后返回raw缓存继续补齐，以及重复点击在途请求去重。模拟接口验证交互，不作为生产耗时证据。
- 1512×864、1365×768、980×900三个窗口尺寸均无页面横向溢出或运行时错误。截图已人工核对提示条、日期缺口、图表、排行及贡献区域；宽表在各自容器内滚动。

本地模拟数据截图：[宽屏](../../../apps/web/tmp/batch-sales-performance/final-wide.png)、[原截图相近尺寸](../../../apps/web/tmp/batch-sales-performance/final-screenshot-size.png)、[窄窗口](../../../apps/web/tmp/batch-sales-performance/final-narrow.png)。

独立 code-reviewer 审查及修复复核已完成，最后两项分店折扣P2问题已关闭，未发现剩余阻断问题。没有可调用的DeepSeek第二审查通道，不将其记录为已完成审查。

## 90 天、100 商品的真实页面验证

本轮新增样本为 **2026-06-10 至 2026-09-07，100 商品、58 店**：522,000 条日统计事实和 9,000 个日商品折扣快照。每商品、每店、每天数量2、金额9；独立预期总数量1,044,000、金额4,698,000，页面结果一致。

页面使用正式 `BatchProductSalesAnalysisPage`、正式 API service 和 request 封装，经 Vite HTTP 代理访问本地 Kestrel、真实 Controller、Service 和 SQL Server。每个HTTP请求独立数据库连接。仅用户会话和授权源固定为测试用户及58店；不包含正式登录、完整App冷启动、生产权限查询耗时、生产网络或冷数据库。使用开发模式React，SQL buffer已热，无应用结果缓存；不能外推生产P95。

计时从按钮 click 开始，在接口完整返回、对应结果/图表出现后等待两次 `requestAnimationFrame`。首次空页面查询到可靠数据绘制为 **1,041 ms**，实际HTTP为741 ms。

之后连续20轮执行：选择全部商品 → 查询 → 可靠总览绘制后立即选未缓存商品 → 商品详情绘制后立即点分店。每轮查询重置详情缓存并选不同商品；全局折扣仍在途，不等待Network Idle。下表是这20轮点击到新结果绘制的统计，首次空页面结果另列，未把保留旧结果的刷新称为冷启动。

| 操作 | 样本 | 中位数 | P95 | 最大值 | 目标 |
|---|---:|---:|---:|---:|---:|
| 查询100商品新结果 | 20 | 492.5 ms | 764.7 ms | 931.0 ms | ≤5,000 ms |
| 未缓存单商品详情 | 20 | 989.2 ms | 1,301.4 ms | 1,620.0 ms | 补充指标 |
| 单商品点击分店 | 20 | 316.6 ms | 380.7 ms | 415.3 ms | ≤3,000 ms |

全部60次响应均核对90个readyDates、58家实际门店和覆盖版本；总览返回100商品，商品/分店读取返回1商品。20轮查询阶段均只有一次query和一次后台discounts，没有逐商品detail请求链。额外补测“100商品全局折扣仍在途时立即打开分店”：query显示515.5ms，分店显示410.0ms，branch实际请求100商品，后台并发状态已记录。

通过页面实际下载100商品90天详情CSV，HTTP200，8,443.3ms、279,313B。标准CSV解析器核对每日90行、分店58行、分店每日5,220行；三个区段各自的独立总量均为1,044,000、金额4,698,000，90天均为complete。读回发现门店范围说明逐店转义后拼接的格式错误，已改为整字段转义，真实SQL回归1项通过；修复后从页面再次下载，HTTP200、8,691.3ms、279,199B；58个门店代码逐个匹配，三个区段的行数及合计再次通过标准CSV解析核验。

总览响应53,462B，单商品详情1,421,996B，单品分店18,909B；总览请求没有携带所有商品的每日门店明细。服务层单独20样本基准：query P95 260.4ms/7 SQL；branch P95 148.8ms/5 SQL；这些SQL数字与浏览器墙钟分别记录。

全商品折扣补齐首次HTTP需12,274.6ms，连续操作并发期间观察到一次19,476.9ms的成功请求。这是仍然存在的独立等待；可靠数量、金额及排行先显示，5秒/3秒目标不包含分类指标全部补齐。

1512×864、1365×768及980×900尺寸无页面横向溢出、无运行时错误，两张90天趋势正常，宽表仅在自身容器内横向滚动。截图已人工查看：[宽屏](../../../apps/web/tmp/batch-sales-performance/90day-wide.png)、[窄窗口](../../../apps/web/tmp/batch-sales-performance/90day-narrow.png)。

原始证据位于 `apps/web/tmp/batch-sales-performance/`：`90day-100product-measurements.json`（SQL原始20样本）、`90day-browser-first.json`、`90day-browser-samples.json`、`90day-browser-summary.json`、`90day-browser-all-concurrent.json`、`90day-browser-request-timeline.json`及`90day-layout.json`。

新增可复现入口为 `BatchProductSales90DayHttpHarnessSqlServerTests.cs`，已显式加入测试项目。真实controller/SQL测试宿主仅监听loopback，禁止连接非本机SQL；随机测试库由fixture创建和清理。后续运行可用test-only `POST /shutdown` 正常结束并等待测试进程退出。独立code-reviewer已核对数据规模、每请求连接、覆盖锁、序列化、计时和清理边界。

## 取消与测试代理收尾

原始20轮浏览器操作经默认Vite代理，浏览器已取消的部分折扣请求未把断开及时传到Kestrel。带业务/响应写入分段计时的对照复现：默认代理请求业务执行11,299.3ms，随后响应写入阻塞125,211ms，直到宿主关闭才释放。直连相同请求在客户端0.3秒断开后，服务约1,233ms结束并记录RequestAborted=true、HTTP499。仅对本地测试代理提前转发res.close后，同类代理请求约1,118ms结束、写入0ms、HTTP499。原日志的500秒级请求总时长不能当作数据库查询耗时。

同时补齐查询代码中的SQL取消：两类Reader使用数据库驱动的取消参数，并在finally恢复原有ADO取消状态。新增本机SQL测试采用独立事务锁定目标表，待SELECT开始后再取消，分别验证统计和折扣Reader及时退出，以及正常、预取消和异常路径的状态恢复。

最终一次扩大后端回归执行104项，103项通过；唯一失败暴露SQL Server执行中取消抛出provider异常的问题。修复为仅在请求已取消时规范化为OperationCanceledException后，以最新代码重跑16项受影响测试（取消2项、MiddleTable 3项、PartialCoverage SQL 11项），全部通过。取消测试确认SELECT已开始后200ms取消，并要求3秒内退出；两项均约1秒完成。独立code-reviewer复核后关闭该问题，未放宽业务断言。证据：`batch-product-sales-final-regression.log`、`batch-product-sales-targeted-regression.log`。

最后对照计划补齐了每日CSV的未完成日期行。真实SQL新增场景在中间非Fresh日期放入大额事实，验证该日不计入总量、每日及分店每日保留5个空数值和pending、已完成无销量日仍为0，并核对各行列数与表头一致。修复初版的空列数并核对实际CSV断言后，新增用例与已有CSV转义/合计用例共2项通过（`csv-partial-axis-recheck-final.log`）。最终后端构建0错误。

### 最终代码页面复验

使用包含全部修复的后端重新启动隔离宿主，沿用正式页面和已修正取消转发的本地代理，再执行5轮100商品、90天查询 → 单商品 → 分店操作。所有响应日期/商品/门店覆盖锁一致，每轮query阶段均无逐商品detail请求。点击至绘制最大值分别为 **1,016.9ms、931.2ms、346.3ms**，5轮均达到5秒/3秒目标；该小样本用于最终回归，不替代上面的20轮P95。原始样本：`90day-browser-final-samples.json`。

页面选定P005及S01分店后点击“导出当前详情”，实际请求仅P005且仍含58家授权门店。CSV HTTP200、393.1ms、236,360B；标准CSV解析器独立验证每日90行、分店58行、分店每日5,220行，三个区段分别合计10,440件、金额46,980，与当前商品页面一致。商品及门店metadata逐项核对通过。证据：`90day-single-detail-export-final-http.json`、`90day-single-csv-final-validation.json`及同名CSV文件。

最终通过代理主动在0.3秒断开全量折扣请求，宿主业务307.7ms结束、响应写入0ms、RequestAborted=true，Kestrel总时长308.4ms并记录HTTP499。说明取消已传入实际后端执行。重启宿主期间options的连接失败提示经页面重试恢复200；最终页面无提示错误、无运行时错误、无横向溢出，商品/分店选择和90天范围保持，见`90day-final-page-state.json`。独立code-reviewer复核最后CSV修复后无未解决P1/P2。

本轮宿主通过`POST /shutdown`正常结束，HTTP harness测试1/1通过；精确核验`hb_batch_sales_test_catalog_bd772df4eca8`、`hb_batch_sales_test_posm_bd772df4eca8`、`hb_batch_sales_test_hbs_bd772df4eca8`均已不存在。此前baseline宿主`ac1e4be7b3ff`对应三库也已核验清理。未处理其他测试库。最终5189和5197均无监听，本任务ego浏览器空间187已关闭。原生子代理均已交付并结束执行；当前接口不提供关闭/归档原生子代理的操作，保留会话记录。

## 核验边界

已采用当前源码、调用点、专项测试和真实浏览器fixture复核。本轮已为当前隔离工作区重新建立codebase-memory索引；已有业务变更还通过精确源码、调用点和相关测试核验。

代码实现、本地验证、提交合并、生产部署和生产业务验收分开记录。本次未提交、合并或部署，不修改生产数据、数据库结构或索引。
已测500商品、58店、22天服务性能，以及100商品、58店、90天真实HTTP页面操作；366天上限组合的性能以及生产页面实际网络耗时尚未测量。

## 可复现入口

- 性能测试：`services/backend/BlazorApp.Api.Tests/BatchProductSalesOverviewPerformanceSqlServerTests.cs`。
- 明确设置本机 `BATCH_SALES_SQLSERVER_TEST_CONNECTION`、`BATCH_SALES_RUN_BENCHMARK=1` 和 `BATCH_SALES_BENCHMARK_OUTPUT_DIR` 后运行过滤器 `FullyQualifiedName~Overview_Performance_SQLServer`。
- 后端范围：BatchProductSalesAnalysisLogicTests、ControllerTests、MiddleTableTests、SqlTests、SqlServerIntegrationTests（基准单独运行）。
- 前端：`npm run typecheck`、`npm run test:batch-product-sales-analysis`、`npm run build -- --mode development`。
- 原始测量、后端日志、真实序列化跨语言契约fixture与浏览器步骤结果：本工作区 `apps/web/tmp/batch-sales-performance/`（临时验收产物，不加入发布包）。
