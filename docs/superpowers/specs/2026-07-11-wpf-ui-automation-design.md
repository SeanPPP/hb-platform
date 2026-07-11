# WPF UI 自动化测试设计

## 目标

为 `apps/pos-wpf` 增加可重复运行的原生 Windows UI 自动化测试，使用 `FlaUI UIA3 + xUnit` 驱动真实 WPF 窗口。第一阶段交付无后台写入的启动冒烟测试，随后用同一测试入口验证门店 `1042`、`1005` 从用户操作到后台操作日志落库的完整链路。

## 已确认决策

- 使用 `FlaUI.Core 5.0.0`、`FlaUI.UIA3 5.0.0` 和仓库现有 xUnit 2 测试栈。
- 不使用 Maestro、Jest 或 Appium。Maestro 不支持原生 WPF，Jest 会增加无价值的 JavaScript 桥接层。
- UI 测试建立独立项目，不并入现有 `Hbpos.Client.Tests`，避免桌面测试影响快速单元测试。
- 第一阶段不把 UI 测试项目加入 `hbpos_win.slnx`，只通过显式项目路径运行，普通解决方案测试不会启动桌面进程。
- UI 测试串行执行，同一时间只允许一个 WPF 进程和一个 UIA 会话。
- 定位器优先使用稳定的 `AutomationProperties.AutomationId`，不依赖中文或英文显示文本。
- 测试不得自动注册、切换或重绑定门店设备。
- 真实业务测试只允许门店 `1042`、`1005`；任何其他门店立即中止，不进入用户操作阶段。

## 范围

### 第一阶段：隔离启动冒烟

启动参数固定为：

```text
--preview --screen=pos --culture=en-AU
```

现有 PreviewMode 会使用临时本地 SQLite 数据库、跳过设备注册和启动后的远程同步，因此该测试不会连接真实业务数据。测试只验证：

1. WPF 进程成功启动。
2. 主窗口在超时内可由 UIA3 获取。
3. POS 主界面可见且没有启动错误覆盖层。
4. 关闭窗口后进程正常退出。

### 第二阶段：门店完整业务链

每次测试运行只绑定一个门店，并由操作系统用户配置中现有的设备授权决定门店。测试入口必须同时满足：

- `HBPOS_E2E_ENABLED=true`
- `HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED=true`
- `HBPOS_E2E_STORE_CODE` 为 `1042` 或 `1005`
- `HBPOS_E2E_CASHIER_BARCODE` 提供专用测试收银员条码
- `HBPOS_E2E_PRODUCT_BARCODE` 提供当前门店可销售的专用测试商品条码
- `HBPOS_API_BASE_URL` 显式指向本次测试使用的 POS API
- `HBPOS_E2E_BACKEND_BASE_URL` 指向后台查询 API
- `HBPOS_E2E_BACKEND_BEARER_TOKEN` 提供全门店 Admin/SuperAdmin token，测试只用于只读查询操作日志
- WPF 界面实际显示的门店代码与 `HBPOS_E2E_STORE_CODE` 完全一致

`HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED=true` 只表示运行者已人工确认 token 属于全门店 Admin/SuperAdmin。该门禁用于在真实写入开始前阻止范围不明的 token，不替代后台的授权校验。

首条完整业务场景为：

```text
启动 WPF
→ 收银员登录
→ 扫码加入指定测试商品
→ 修改数量
→ 进入付款
→ 完成现金销售
→ 显示支付成功
→ 后台操作日志出现对应门店、设备和收银员事件
```

后台完成判据至少包含以下事件：

- `CASHIER_LOGIN`
- `CART_ITEM_ADD`
- `CART_ITEM_QUANTITY_CHANGE`
- `PAYMENT_TENDER_ADD`
- `SALE_COMPLETE`

后续场景只有在首条链路稳定后再增加挂单/取单、退款、收据重打、分期和日结，不预先搭建通用页面对象框架。

## 文件结构

```text
apps/pos-wpf/tests/Hbpos.Client.UiTests/
  Hbpos.Client.UiTests.csproj
  WpfAppFixture.cs
  StartupSmokeTests.cs
  SaleFlowTests.cs
```

- `Hbpos.Client.UiTests.csproj`：固定 NuGet 版本并引用 WPF 客户端项目。
- `WpfAppFixture.cs`：启动/关闭 WPF、创建 UIA3 会话、执行门店白名单检查、在失败时保存截图。
- `StartupSmokeTests.cs`：只包含 PreviewMode 启动冒烟。
- `SaleFlowTests.cs`：只包含 `1042`、`1005` 的真实完整销售链路和后台完成轮询。

暂不创建页面对象、驱动接口、工厂或通用 DSL。重复定位逻辑达到三个调用点后再提取小型辅助方法。

## WPF 可测试性

只给首条场景实际使用的控件添加显式 `AutomationProperties.AutomationId`：

- 主窗口根节点和 POS 主界面
- 收银员登录输入框及确认按钮
- 商品搜索/扫码输入入口
- 数量修改入口
- 付款入口、现金付款与确认按钮
- 支付成功界面
- 当前门店代码显示元素

AutomationId 使用稳定英文标识，不包含本地化文本、商品名称或动态金额。除这些属性外不改变布局、样式、绑定和业务行为。

## 数据隔离

- PreviewMode 冒烟测试始终先运行，验证框架而不写后台。
- 真实 E2E 由显式环境变量启用，测试项目不会被普通单元测试命令自动执行。
- 测试开始前记录门店代码、设备代码、收银员、开始 UTC 时间和测试商品条码。
- 后台验证按门店、设备、收银员和时间窗联合过滤，不使用全库最近一条记录。
- 其他门店只对 `PosOperationAudit` 按门店执行前后只读计数与最大 `ReceivedAtUtc` 快照；其子表通过新增事件 ID 反查。发现非 `1042/1005` 的新增审计事件时整次测试失败。
- 不自动删除销售、退款、分期或日结业务记录。真实环境是否允许写入仍由运行前人工授权决定。
- 两个门店分别运行，不在同一 Windows 配置中自动重新注册设备。

## 等待、失败与证据

- 使用 FlaUI 的重试/等待能力等待窗口和控件状态，不使用固定长时间 `Thread.Sleep`。
- 单个 UI 操作设置明确超时；超时信息包含当前步骤和 AutomationId。
- 失败时将窗口截图保存到 `%TEMP%\hbpos-ui-tests\{run-id}` 并报告客户端日志路径，不将截图提交到仓库。
- 后台完成采用有上限的轮询，达到超时后报告已观察到的事件集合。
- 清理阶段只关闭由测试启动的 WPF 进程，不终止用户已打开的其他实例。

## 执行方式

PreviewMode 冒烟：

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter FullyQualifiedName~StartupSmokeTests
```

门店完整链路：

```powershell
$env:HBPOS_E2E_ENABLED='true'
$env:HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED='true'
$env:HBPOS_E2E_STORE_CODE='1042'
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter FullyQualifiedName~SaleFlowTests
```

门店 `1005` 使用相同命令，只替换 `HBPOS_E2E_STORE_CODE`。收银员凭据和测试商品通过运行机器的受保护配置提供，不写入源码、测试参数或测试日志。

## 验收标准

- PreviewMode 启动冒烟在本机连续运行三次通过，且没有后台业务写入。
- UI 测试程序集无并行 WPF 进程。
- 缺少显式 E2E 开关、门店不在白名单或设备门店不匹配时，测试在首次业务操作前失败。
- `1042`、`1005` 分别完成一次销售链路，并在后台观察到五类必需事件。
- 其他门店的前后只读快照无新增记录。
- 失败证据包含步骤、截图和日志位置，且不泄露收银员凭据、设备授权码或支付信息。

## 非目标

- 不用 UI 自动化替代现有 ViewModel、服务和 API 单元测试。
- 不覆盖真实银行卡、Linkly、钱箱或打印机硬件认证。
- 不在第一阶段覆盖所有 24 类操作日志事件。
- 不增加 Maestro、Jest、Appium、WinAppDriver 或自定义 UI 自动化协议。
