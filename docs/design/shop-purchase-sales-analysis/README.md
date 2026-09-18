# 进货销量分析：订货前台页面 + 后台页面改版并挪到销售看板

日期：2026-09-18。两件事一起落地：订货前台（`/shop`）新增「进货销量分析」；后台「分店供应商进货销量分析」做同样的图表化改版，并从「收银管理」挪到「销售看板」。

## 页面设计

两个页面共用同一套表格列与图表组件（`apps/web/src/components/PurchaseSalesTrend/`）：

- **表格列**：图片、货号/名称、上次进货、最近进货、间隔销量、**日销量与进货**、**售出比**、供应商（后台另有统计更新时间）。30/60/90 天三列与「间隔天数」列已下线，进货数量统一按整数显示。
- **按总销量排序**：「日销量与进货」列可排序，排的是最近进货后的累计净销量，与图表、售出比同一口径。**页面默认即按总销量降序**，点击列头可切换升序。排序在服务端完成，跨分页有效。
- **行内迷你图**：蓝色柱为逐日净销量（浅蓝为周末，退货为向下的红色柱），橙色虚线与圆点标出每次进货的日期，顶部标注进货数量。
- **展开大图**（点击行）：左轴日销量柱，最近进货之前的日子用淡色；橙色标记「上次进货 / 最近进货」及数量；绿色线为最近进货后的累计销量（右轴），橙色虚线为进货量参考线，两线相交处打红圈并提示「约 MM-DD 卖完」，未卖完则显示按进货量估算的剩余。
- **售出比** = 最近进货后累计销量 ÷ 最近进货量，≥100% 标红、≥70% 标橙。
- **前台页面**：分店跟随顶部「当前分店」，切店即重载供应商并清空结果；工具栏只有供应商、订单日期、关键字。
- **后台页面**：保留分店下拉与列拖拽排序；默认列顺序把「供应商」挪到图表之后，1440 宽度下图表列无需横向滚动即可见；列顺序存储键升级为 `v2`，旧的含 30/60/90 的自定义顺序自动作废。

## 后端

- 分析行 DTO 追加 `TotalSalesSinceLatestPurchase`（最近进货当天起至今的累计净销量），由销量聚合子查询产出并加入排序白名单；该子查询的窗口末端改为「最近进货后 90 天」与「今天」取较晚者，久未进货的商品不会被 90 天上界截断，同时 30/60/90 天三个口径保持不变。「间隔天数」排序键后端仍保留以兼容旧请求。
- 分析行 DTO 追加 `DailySales`（上次进货日起至布里斯班业务日今天的逐日净销量，无上次进货则取最近进货前 30 天；缺失日期补 0，退货负数保留）与 `Purchases`（窗口内的进货事件，数量取整）。分页 SQL 与排序不变，逐日数据按页一次查询 `ProductStoreDailySalesStatistic` 后在内存分组；查询失败时降级为空序列，不影响主查询。SalesQty30/60/90 字段保留以兼容旧客户端。
- 新增前台只读接口（路由前缀 `api/react/v1/local-supplier-invoices`）：
  - `GET shop/purchase-sales-analysis`
  - `GET shop/purchase-sales-analysis/supplier-options?storeCode=`
  只认订货前台权限（`OrderFront.View`，或纯仓库员工 + `Orders.Create`），门店限定为本人名下门店，与前台进货单口径一致。
- 后台三个分析接口改为「`SalesDashboard.LocalSupplierPurchaseSales.View` 或 `LocalPurchase.View` 任一即可」，接口层对旧权限保持兼容。

## 性能：已优化到 286 毫秒

页面原先在生产库上主查询要 2.4~6.6 秒。定位与处理过程：

| 环节 | 优化前 | 优化后 |
| --- | --- | --- |
| 明细表逻辑读 | 66,986 次（并行全表扫描） | 1,065 次（94 次 seek） |
| 服务端 CPU | 3,625 毫秒 | 500~564 毫秒 |
| 服务端占用时间 | 2,433~6,611 毫秒 | **286 毫秒** |
| 逐日销量补充（当前页 100 个商品） | 126 毫秒 | 不变，可忽略 |

根因：供应商信息不在进货明细表上，必须先扫描明细并 JOIN 商品/零售价才能按供应商过滤，
导致为拿到 7,786 行明细而扫描了 65 万行整表。

做了三件事：

1. **加覆盖索引（主要收益）**：`IX_LSPSA_InvoiceDetails_Invoice_Covering`，
   已于 2026-09-18 用 `ONLINE = ON` 在生产库在线创建，耗时约 25 秒未阻塞业务，占用 265.3 MB。
   脚本与实测数据见 [`SqlScripts/LocalSupplierPurchaseSalesAnalysisIndexes.sql`](../../../services/backend/SqlScripts/LocalSupplierPurchaseSalesAnalysisIndexes.sql)。
2. **JOIN 改写**：先算出最终 ProductCode 再 JOIN Product，把跨表 COALESCE 的连接条件变成单列等值，
   执行计划更稳定。结果经全量双向 EXCEPT 比对与改写前完全一致（675 行无差异）。
3. **分段耗时日志**：后端输出 `分店供应商进货销量分析耗时 PagedMs=... DailyMs=...`，便于持续观察。

试过但**放弃**的方向（实测更慢，已回退）：供应商商品集合 EXISTS 预过滤（48~51 秒）、
强制嵌套循环连接（53~64 秒，596 万次逻辑读）。执行计划还建议过一个 `StoreRetailPrice` 索引，
经 IO 统计判断该表是正常 seek 而非整表扫描，收益不成立，**未采纳**，理由记在 SQL 脚本注释里。

## 权限与菜单迁移（需要管理员动作）

销售看板沿用「逐页独立权限」的既有约定（测试明确要求本地进货权限不得连带点亮销售看板），因此新增权限码 **`SalesDashboard.LocalSupplierPurchaseSales.View`（查看分店进货销量分析）**：

- 新路径 `/executive-sales-intelligence/local-supplier-purchase-sales-analysis`，旧路径 `/pos-admin/local-supplier-purchase-sales-analysis` 保留为隐藏重定向。
- Web 路由、菜单预览、后台入口规则、后端 `NavigationService`（含后台准入集合）、移动端角色菜单目录与中英文权限文案均已同步。
- **管理员天然可见；非管理员若要继续使用后台页面，需要在角色管理里授予新权限**。`LocalPurchase.View` 不再显示该菜单（与 #188 拆分 Web 用户管理权限的做法一致）。
- 前台页面不需要新权限，能进订货前台的账号都能看本店数据。

## 国际化与首屏体积

- 图表、前台页面与计算说明的中英文文案放在 `components/PurchaseSalesTrend/messages.{zh,en}.json`，通过新增的 `i18n/registerPageMessages` 随页面代码块懒注册；前台导航增量文案随 `ShopLayout` 懒注册（`layout/shopNavMessages.ts`）。计算说明改用前端译文，不再直接显示后端返回的中文。
- 「批量货号销量」页面原先整套文案被静态打进首屏 i18n 包，本次一并改为懒注册。按 CI 同环境构建，首屏 gzip 从 458,319 降到约 453,650 字节，体积门禁余量从几十字节恢复到约 4.7 KB。
- 主文案包移除了已下线的 30/60/90 天列文案。

## 验收截图（演示数据）

- 前台：[桌面中文](shop-desktop-zh.png)、[桌面英文](shop-desktop-en.png)、[手机 390px](shop-mobile-zh.png)
- 后台（销售看板下）：[中文](admin-desktop-zh.png)、[英文](admin-desktop-en.png)

本地验收入口 `apps/web/dev/shop-purchase-sales-analysis-preview/`：默认挂载真实 `ShopLayout` + 前台页面，`?mode=admin` 挂载 `AdminLayout` + 后台页面，`?auto=1` 自动选供应商并搜索；仅入口内模拟 fetch。在 `apps/web` 执行 `npm run dev -- --port 5196` 后打开 `http://localhost:5196/dev/shop-purchase-sales-analysis-preview/?lang=zh`。

## 已执行的验证

- 后端：`dotnet test` 过滤导航、权限种子、授权元数据、分析服务与新接口相关测试，675 通过、3 跳过（需要真实 SQL Server 的只读验证）、0 失败；`dotnet build` 无错误。
- Web：`npm run typecheck`；`test:access`、`test:local-supplier-purchase-sales-analysis`（含新增的图表指标与服务归一化用例）、`test:batch-product-sales-analysis`；`shopMobileNavigation`、`shopRedesignUiContract`、`shopBannerCopy`、前台进货单 `sourceContract` 契约测试；`build:ci` + `verify:bundle` 体积门禁。
- 移动端：权限文案测试、角色菜单目录测试、`check-locale-parity.js`。
- 排序链路：前台与后台均验证「首次点击列头为降序 → 请求参数 `sortBy=totalSalesSinceLatestPurchase&sortOrder=desc` → 返回结果严格降序」，再次点击转升序同样正确。
- 浏览器：前台 1440 / 390 视口与后台 1440 视口，中英文各自完成「选供应商 → 搜索 → 展开大图」；「货号销量」页面英文界面回归正常（懒注册文案生效）。

## 未覆盖

- 逐日销量查询未在真实 SQL Server 上实测；每页最多 200 个商品、窗口最长约一年，预期数据量可接受，上线后建议观察接口耗时。
- 未连接生产数据库验收；生产需管理员为相关角色授予新权限后，非管理员才能在销售看板看到该页面。
