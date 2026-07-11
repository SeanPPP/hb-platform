# WPF UI 自动化测试实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用 FlaUI UIA3 + xUnit 建立 WPF POS 启动冒烟和真实现金销售端到端测试，真实写入仅允许门店 `1042`、`1005`，并只读确认销售订单与五类操作日志已到达后台。

**Architecture:** 新建独立的 `Hbpos.Client.UiTests` 项目，显式路径运行且不加入 `hbpos_win.slnx`。Preview 冒烟使用现有临时 SQLite；真实链路在启动 WPF 前读取本机设备绑定、校验全门店审计权限确认和后台只读快照，再驱动登录、扫码、改数量、现金支付，最后轮询后台订单与操作日志。测试程序集禁用并行，每次只拥有并清理自己启动的一个 WPF 进程。

**Tech Stack:** .NET 8 Windows、WPF、FlaUI.Core 5.0.0、FlaUI.UIA3 5.0.0、xUnit 2.5.3、Microsoft.NET.Test.Sdk 17.8.0、Microsoft.Data.Sqlite 10.0.8、System.Text.Json。

## Global Constraints

- 真实业务测试只允许 `HBPOS_E2E_STORE_CODE=1042` 或 `1005`；其他值必须在启动 WPF 前失败。
- 必须设置 `HBPOS_E2E_ENABLED=true`、`HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED=true`、`HBPOS_E2E_CASHIER_BARCODE`、`HBPOS_E2E_PRODUCT_BARCODE`、`HBPOS_API_BASE_URL`、`HBPOS_E2E_BACKEND_BASE_URL`、`HBPOS_E2E_BACKEND_BEARER_TOKEN`。
- `HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED=false` 时必须在启动 WPF 前失败；测试子进程显式设置为 `true`。
- 后台 bearer token 必须具备 `Permissions.PosTerminal.Audit.View` 和 Admin/SuperAdmin 全门店可见性；测试不输出 token、收银员条码或商品条码。
- 本机 `DeviceCache` 的最新 `StoreCode` 必须与目标门店完全一致，`DeviceCode` 必须非空；测试不得注册、切换、重绑或修改设备授权。
- 同一 DeviceCode 在现场必须由当前测试机器独占，禁止另一台机器并发复用；五类事件同 InstanceId 校验不能替代该机器级前提。
- Preview 固定使用 `--preview --screen=pos --culture=en-AU`，不得连接真实业务数据库。
- 只添加稳定英文 `AutomationProperties.AutomationId`；不改布局、样式、文案、Binding、Command 或业务行为。
- UI 等待使用 FlaUI `Retry` 和明确超时，不使用固定长时间 `Thread.Sleep`。
- 后台验证只使用 GET；不发送 DELETE、修改 SQL、测试数据重置或自动撤销销售。
- 失败证据写入 `%TEMP%\hbpos-ui-tests\{run-id}`，仓库不跟踪截图和运行日志。
- live 开始输入收银员条码或商品条码等任一受保护值后的失败不生成整窗截图，只报告未生成状态和客户端日志路径；支付成功页仍可截图。
- 固定窗口内其他门店的正常并发审计视为窗口污染并令测试失败；这不表示测试修改了该门店。
- 所有新增注释使用中文；提交信息使用中文并包含 `reasonix`。

---

### Task 1: 独立 FlaUI 项目与 Preview 启动冒烟

**Files:**
- Create: `apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj`
- Create: `apps/pos-wpf/tests/Hbpos.Client.UiTests/WpfAppFixture.cs`
- Create: `apps/pos-wpf/tests/Hbpos.Client.UiTests/StartupSmokeTests.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/MainWindow.xaml`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PosTerminalView.xaml`

**Interfaces:**
- Produces: `WpfAppFixture.Launch(string arguments, IReadOnlyDictionary<string,string?>? environment = null) : Window`
- Produces: `WpfAppFixture.WaitForAutomationId(string automationId, TimeSpan? timeout = null) : AutomationElement`
- Produces: `WpfAppFixture.CaptureFailure(string step) : string`
- Produces: `WpfAppFixture.CloseOwnedProcess() : bool`
- Produces AutomationIds: `PosMainWindow`, `PosTerminalScreen`

- [ ] **Step 1: 创建测试项目并锁定版本**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FlaUI.Core" Version="5.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="5.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Hbpos.Client.Wpf\Hbpos.Client.Wpf.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 先写 Preview 冒烟测试和未依赖 AutomationId 的启动夹具**

`WpfAppFixture.cs` 必须先包含程序集串行设置、collection fixture、延迟启动、FlaUI 会话、临时证据目录和进程所有权清理。WPF 可执行文件用 `typeof(Hbpos.Client.Wpf.App).Assembly.Location` 替换扩展名为 `.exe`，不硬编码仓库路径。

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Hbpos.Client.UiTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfUiCollection : ICollectionFixture<WpfAppFixture>
{
    public const string Name = "WPF UI";
}

public sealed class WpfAppFixture : IDisposable
{
    public FlaUI.Core.Application? App { get; private set; }
    public UIA3Automation? Automation { get; private set; }
    public Window? MainWindow { get; private set; }
    public string EvidenceDirectory { get; } = Path.Combine(
        Path.GetTempPath(), "hbpos-ui-tests", Guid.NewGuid().ToString("N"));

    public Window Launch(string arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (App is not null) throw new InvalidOperationException("测试夹具已经拥有一个 WPF 进程。");
        var assemblyPath = typeof(Hbpos.Client.Wpf.App).Assembly.Location;
        var executablePath = Path.ChangeExtension(assemblyPath, ".exe");
        if (!File.Exists(executablePath)) throw new FileNotFoundException("找不到 WPF 可执行文件。", executablePath);
        Directory.CreateDirectory(EvidenceDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false,
        };
        startInfo.Environment["HBPOS_CLIENT_LOG_FILE"] = Path.Combine(EvidenceDirectory, "hbpos-client.log");
        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null) startInfo.Environment.Remove(pair.Key);
                else startInfo.Environment[pair.Key] = pair.Value;
            }
        }
        App = FlaUI.Core.Application.Launch(startInfo);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(30))
            ?? throw new TimeoutException("30 秒内未找到 WPF 主窗口。");
        return MainWindow;
    }

    public AutomationElement WaitForAutomationId(string automationId, TimeSpan? timeout = null) =>
        Retry.WhileNull(
            () => MainWindow?.FindFirstDescendant(automationId),
            timeout: timeout ?? TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"未找到 AutomationId={automationId}。")
        .Result!;

    public string CaptureFailure(string step)
    {
        Directory.CreateDirectory(EvidenceDirectory);
        var path = Path.Combine(EvidenceDirectory, $"{step}.png");
        if (MainWindow is not null)
        {
            using var image = Capture.Element(MainWindow);
            image.ToFile(path);
        }
        return path;
    }

    public bool CloseOwnedProcess()
    {
        if (App is null) return true;
        var exited = App.HasExited || App.Close(killIfCloseFails: false);
        if (!exited) App.Kill();
        Automation?.Dispose();
        App.Dispose();
        Automation = null;
        App = null;
        MainWindow = null;
        return exited;
    }

    public void Dispose() => CloseOwnedProcess();
}
```

`StartupSmokeTests.cs`：

```csharp
namespace Hbpos.Client.UiTests;

[Collection(WpfUiCollection.Name)]
public sealed class StartupSmokeTests
{
    private readonly WpfAppFixture _app;

    public StartupSmokeTests(WpfAppFixture app) => _app = app;

    [Fact]
    public void Preview_mode_shows_pos_screen_and_exits_cleanly()
    {
        try
        {
            var window = _app.Launch("--preview --screen=pos --culture=en-AU");
            Assert.Equal("PosMainWindow", window.AutomationId);
            var pos = _app.WaitForAutomationId("PosTerminalScreen", TimeSpan.FromSeconds(30));
            Assert.False(pos.IsOffscreen);
            Assert.True(_app.CloseOwnedProcess());
        }
        catch
        {
            _app.CaptureFailure(nameof(Preview_mode_shows_pos_screen_and_exits_cleanly));
            throw;
        }
    }
}
```

- [ ] **Step 3: 运行测试，确认因缺少两个 AutomationId 而失败**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter FullyQualifiedName~StartupSmokeTests --logger "console;verbosity=normal"
```

Expected: FAIL，失败信息包含 `PosMainWindow` 或 `PosTerminalScreen`，WPF 进程被夹具清理。

- [ ] **Step 4: 只添加两个无视觉影响的 AutomationId**

```xml
<Window ... AutomationProperties.AutomationId="PosMainWindow">
```

```xml
<UserControl ... AutomationProperties.AutomationId="PosTerminalScreen">
```

- [ ] **Step 5: 连续三次运行 Preview 冒烟**

Run three times:

```powershell
1..3 | ForEach-Object {
  dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --no-restore --filter FullyQualifiedName~StartupSmokeTests --logger "console;verbosity=minimal"
  if ($LASTEXITCODE -ne 0) { throw "Preview 冒烟第 $_ 次失败" }
}
```

Expected: 3 次均 PASS，每次 1 个测试、0 失败、0 跳过；任务管理器中没有遗留 `Hbpos.Client.Wpf` 测试进程。

- [ ] **Step 6: 检查并提交**

```powershell
git diff --check
git add apps/pos-wpf/tests/Hbpos.Client.UiTests apps/pos-wpf/src/Hbpos.Client.Wpf/MainWindow.xaml apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PosTerminalView.xaml
git commit -m "测试：添加 WPF Preview UI 冒烟 reasonix"
```

---

### Task 2: 真实门店安全门禁与现金销售 UI 链路

**Files:**
- Modify: `apps/pos-wpf/tests/Hbpos.Client.UiTests/WpfAppFixture.cs`
- Create: `apps/pos-wpf/tests/Hbpos.Client.UiTests/SaleFlowTests.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/MainWindow.xaml`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PosTerminalView.xaml`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PaymentView.xaml`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PaymentSuccessView.xaml`
- Modify: `docs/superpowers/specs/2026-07-11-wpf-ui-automation-design.md`

**Interfaces:**
- Produces: `LiveE2eConfiguration.FromEnvironment(Func<string,string?>? read = null) : LiveE2eConfiguration`
- Produces: `LiveDeviceBinding.ReadLatestAsync(string? databasePath = null, CancellationToken cancellationToken = default) : Task<LiveDeviceBinding>`
- Produces: `LiveDeviceBinding.EnsureMatches(string targetStoreCode) : void`
- Consumes: Task 1 `WpfAppFixture.Launch`, `WaitForAutomationId`, `CaptureFailure`, `CloseOwnedProcess`
- Produces AutomationIds: `CurrentStoreInfo`, `CurrentCashierInfo`, `CashierLoginOverlay`, `CashierLoginInput`, `CashierLoginButton`, `ProductBarcodeInput`, `CartItemsGrid`, `CartLineQuantity`, `CartLineIncreaseButton`, `OpenPaymentButton`, `PaymentScreen`, `RemainingAmount`, `ConfirmPaymentButton`, `AddCashTenderButton`, `AppliedTendersList`, `PaymentSuccessScreen`, `CompletedTransactionId`, `CompletedTransactionContext`

- [ ] **Step 1: 先写环境变量白名单和密钥脱敏测试**

在 `SaleFlowTests.cs` 增加 `LiveE2eConfigurationTests`，使用字典读取器，不读取真实进程环境：

```csharp
[Theory]
[InlineData("1002")]
[InlineData("10420")]
[InlineData("")]
public void Rejects_store_outside_exact_allowlist(string storeCode)
{
    var values = ValidValues();
    values["HBPOS_E2E_STORE_CODE"] = storeCode;
    var error = Assert.Throws<InvalidOperationException>(() =>
        LiveE2eConfiguration.FromEnvironment(name => values.GetValueOrDefault(name)));
    Assert.Contains("HBPOS_E2E_STORE_CODE", error.Message);
    Assert.DoesNotContain(values["HBPOS_E2E_CASHIER_BARCODE"], error.Message);
    Assert.DoesNotContain(values["HBPOS_E2E_BACKEND_BEARER_TOKEN"], error.Message);
}

[Fact]
public void Accepts_only_complete_1042_configuration()
{
    var values = ValidValues();
    var result = LiveE2eConfiguration.FromEnvironment(name => values.GetValueOrDefault(name));
    Assert.Equal("1042", result.StoreCode);
    Assert.Equal(new Uri("http://localhost:5159/"), result.PosApiBaseUrl);
}
```

`ValidValues()` 必须包含八个必需变量和 `HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED=true`，令牌与条码使用测试占位字符串但不得出现在失败消息。

- [ ] **Step 2: 运行配置测试，确认缺少实现时失败**

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter FullyQualifiedName~LiveE2eConfigurationTests --logger "console;verbosity=normal"
```

Expected: 首次为编译失败；增加仅抛 `NotSupportedException` 的类型骨架后再次运行，测试以运行时 FAIL 结束。

- [ ] **Step 3: 实现最小配置解析**

```csharp
internal sealed record LiveE2eConfiguration(
    string StoreCode,
    string CashierBarcode,
    string ProductBarcode,
    Uri PosApiBaseUrl,
    Uri BackendBaseUrl,
    string BackendBearerToken)
{
    private static readonly HashSet<string> AllowedStores = ["1042", "1005"];

    public static LiveE2eConfiguration FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        if (!string.Equals(read("HBPOS_E2E_ENABLED"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("必须显式设置 HBPOS_E2E_ENABLED=true。");
        if (!string.Equals(read("HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("必须确认 HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED=true。");
        if (string.Equals(read("HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED 不能为 false。");

        var store = Required("HBPOS_E2E_STORE_CODE", read);
        if (!AllowedStores.Contains(store))
            throw new InvalidOperationException("HBPOS_E2E_STORE_CODE 只允许 1042 或 1005。");

        return new LiveE2eConfiguration(
            store,
            Required("HBPOS_E2E_CASHIER_BARCODE", read),
            Required("HBPOS_E2E_PRODUCT_BARCODE", read),
            RequiredUri("HBPOS_API_BASE_URL", read),
            RequiredUri("HBPOS_E2E_BACKEND_BASE_URL", read),
            Required("HBPOS_E2E_BACKEND_BEARER_TOKEN", read));
    }

    private static string Required(string name, Func<string, string?> read) =>
        string.IsNullOrWhiteSpace(read(name))
            ? throw new InvalidOperationException($"缺少环境变量 {name}。")
            : read(name)!.Trim();

    private static Uri RequiredUri(string name, Func<string, string?> read)
    {
        var value = Required(name, read);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"环境变量 {name} 必须是绝对 HTTP/HTTPS URI。");
        return uri;
    }
}
```

- [ ] **Step 4: 先写本机 DeviceCache 只读门禁测试**

测试在 `%TEMP%` 建立独立 SQLite 文件和 `DeviceCache` 表，插入 `1042/DEV-1042`，然后断言读取成功、`EnsureMatches("1042")` 通过、`EnsureMatches("1005")` 失败。失败消息只包含门店和设备标识，不包含授权码。

Run:

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter FullyQualifiedName~LiveDeviceBindingTests --logger "console;verbosity=normal"
```

Expected: RED，因为 `LiveDeviceBinding.ReadLatestAsync` 尚不存在。

- [ ] **Step 5: 用 SQLite ReadOnly 模式实现设备预检**

```csharp
internal sealed record LiveDeviceBinding(string StoreCode, string DeviceCode)
{
    public static async Task<LiveDeviceBinding> ReadLatestAsync(
        string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hbpos.Client", "hbpos_client.db");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StoreCode, DeviceCode FROM DeviceCache ORDER BY UpdatedAt DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("本机没有已注册的 WPF 设备绑定。");
        return new LiveDeviceBinding(reader.GetString(0).Trim(), reader.GetString(1).Trim());
    }

    public void EnsureMatches(string targetStoreCode)
    {
        if (!string.Equals(StoreCode, targetStoreCode, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(DeviceCode))
            throw new InvalidOperationException("本机设备门店与 HBPOS_E2E_STORE_CODE 不一致，已在业务操作前中止。");
    }
}
```

- [ ] **Step 6: 添加真实链路使用的 AutomationId**

按 Interfaces 列表逐项添加。行模板中的 `CartLineQuantity` 和 `CartLineIncreaseButton` 允许每行重复，但测试必须先定位目标 `DataGridRow`，再在该行的子树中查找，禁止从窗口根节点取第一个重复 ID。

在已批准设计文档的必需环境变量列表加入：

```text
HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED=true
```

说明该值只表示运行者已人工确认 token 为全门店 Admin/SuperAdmin，可程序化阻止使用范围不明的 token 开始真实写入。

- [ ] **Step 7: 实现从登录到成功页的 FlaUI 测试**

`SaleFlowTests.Store_cash_sale_reaches_payment_success` 的顺序必须固定：

```csharp
var config = LiveE2eConfiguration.FromEnvironment();
var device = await LiveDeviceBinding.ReadLatestAsync();
device.EnsureMatches(config.StoreCode);
var window = _app.Launch("--culture=en-AU", new Dictionary<string, string?>
{
    ["HBPOS_API_BASE_URL"] = config.PosApiBaseUrl.ToString(),
    ["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"] = "true",
});
Assert.Equal(config.StoreCode, ExtractAllowedStore(_app.WaitForAutomationId("CurrentStoreInfo").Name));
var login = _app.WaitForAutomationId("CashierLoginInput");
login.Focus();
Keyboard.Type(config.CashierBarcode);
Keyboard.Type(VirtualKeyShort.RETURN);
WaitUntilHidden("CashierLoginOverlay");
var scan = _app.WaitForAutomationId("ProductBarcodeInput");
scan.Focus();
Keyboard.Type(config.ProductBarcode);
Keyboard.Type(VirtualKeyShort.RETURN);
var row = WaitForSingleCartRow("CartItemsGrid");
var quantity = ReadQuantity(row, "CartLineQuantity");
row.FindFirstDescendant("CartLineIncreaseButton")!.AsButton().Invoke();
WaitForQuantity(row, quantity + 1m);
_app.WaitForAutomationId("OpenPaymentButton").AsButton().Invoke();
_app.WaitForAutomationId("PaymentScreen", TimeSpan.FromSeconds(30));
_app.WaitForAutomationId("AddCashTenderButton").AsButton().Invoke();
WaitForTenderAndZeroRemaining();
var confirm = _app.WaitForAutomationId("ConfirmPaymentButton").AsButton();
Assert.True(confirm.IsEnabled);
confirm.Invoke();
_app.WaitForAutomationId("PaymentSuccessScreen", TimeSpan.FromSeconds(60));
Assert.NotEqual("-", _app.WaitForAutomationId("CompletedTransactionId").Name);
```

所有等待辅助方法使用 `Retry.WhileNull` / `Retry.WhileFalse`，超时信息包含当前步骤和 AutomationId。`catch` 先调用 `CaptureFailure`，`finally` 只调用 `CloseOwnedProcess`，不点击新交易、不删除或撤销销售。

- [ ] **Step 8: 运行纯门禁测试并编译真实流程**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter "FullyQualifiedName~LiveE2eConfigurationTests|FullyQualifiedName~LiveDeviceBindingTests" --logger "console;verbosity=minimal"
dotnet build apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --no-restore
```

Expected: 门禁测试全部 PASS；项目 0 error。此步骤不得设置真实 E2E 环境变量，不产生业务写入。

- [ ] **Step 9: 检查并提交**

```powershell
git diff --check
git add apps/pos-wpf/tests/Hbpos.Client.UiTests apps/pos-wpf/src/Hbpos.Client.Wpf/MainWindow.xaml apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PosTerminalView.xaml apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PaymentView.xaml apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/PaymentSuccessView.xaml docs/superpowers/specs/2026-07-11-wpf-ui-automation-design.md
git commit -m "测试：添加双门店真实现金销售 UI 链路 reasonix"
```

---

### Task 3: 后台订单、五类审计事件与其他门店只读校验

**Files:**
- Modify: `apps/pos-wpf/tests/Hbpos.Client.UiTests/SaleFlowTests.cs`

**Interfaces:**
- Produces: `OperationAuditBackendClient.FetchAllAsync(AuditQuery query, CancellationToken cancellationToken) : Task<IReadOnlyList<AuditRow>>`
- Produces: `OperationAuditBackendClient.PollRequiredAsync(...) : Task<IReadOnlyList<AuditRow>>`
- Produces: `OperationAuditBackendClient.GetDetailAsync(Guid eventId, CancellationToken cancellationToken) : Task<JsonDocument>`
- Produces: `OperationAuditBackendClient.PollOrderAsync(string orderGuid, string storeCode, CancellationToken cancellationToken) : Task`
- Produces: `AuditSnapshot.Create(IEnumerable<AuditRow> rows) : AuditSnapshot`
- Produces: `AuditSnapshot.AssertOtherStoresUnchanged(AuditSnapshot before, AuditSnapshot after, ISet<string> allowedStores) : void`
- Consumes: Task 2 `LiveE2eConfiguration`, `LiveDeviceBinding`, `Store_cash_sale_reaches_payment_success`

- [ ] **Step 1: 先写 JSON 分页和快照差异测试**

使用自定义 `HttpMessageHandler` 返回 camelCase JSON，覆盖：

```csharp
[Fact]
public async Task FetchAllAsync_reads_all_pages_without_writes()
{
    // 第 1 页 200 项、第 2 页 1 项，断言客户端只发两个 GET 且返回 201 项。
}

[Fact]
public void Snapshot_rejects_new_event_in_other_store()
{
    var before = AuditSnapshot.Create([
        Row("old-1042", "1042", "DEV-1", "CASHIER_LOGIN", "Succeeded", "2026-07-11T10:00:00Z")
    ]);
    var after = AuditSnapshot.Create([
        Row("old-1042", "1042", "DEV-1", "CASHIER_LOGIN", "Succeeded", "2026-07-11T10:00:00Z"),
        Row("new-1009", "1009", "DEV-X", "SALE_COMPLETE", "Succeeded", "2026-07-11T10:01:00Z")
    ]);
    Assert.Throws<InvalidOperationException>(() =>
        AuditSnapshot.AssertOtherStoresUnchanged(before, after, new HashSet<string> { "1042", "1005" }));
}

[Fact]
public void Required_events_must_be_succeeded_for_same_store_device_and_cashier()
{
    // 五类事件齐全时通过；任一事件 Outcome=Failed、门店/设备/收银员不同或 SALE_COMPLETE 无 orderGuid 时失败。
}
```

- [ ] **Step 2: 运行测试确认 RED**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter "FullyQualifiedName~OperationAuditBackendClientTests|FullyQualifiedName~AuditSnapshotTests" --logger "console;verbosity=normal"
```

Expected: FAIL，因为分页客户端和快照断言尚不存在。

- [ ] **Step 3: 实现只读分页客户端和固定窗口快照**

使用以下记录类型和固定约束：

```csharp
internal sealed record AuditRow(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset ReceivedAtUtc,
    string OperationType,
    string Outcome,
    string StoreCode,
    string DeviceCode,
    string? CashierId,
    string? CashierName,
    string? InstanceId,
    string? OrderGuid,
    string? PaymentMethod,
    string? ReasonCode);

internal sealed record AuditQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? StoreCode = null,
    string? DeviceCode = null,
    string? CashierKeyword = null);
```

`FetchAllAsync` 必须从 `pageNumber=1&pageSize=200` 开始，解析 `success/data/items/total`，直到累计数量达到 total 或当前页为空。URI 只包含门店、设备、收银员和时间，不包含收银员条码、商品条码或 bearer token。每个请求必须验证 HTTP 成功和 JSON `success=true`。

快照按 `StoreCode` 保存：

```csharp
internal sealed record StoreAuditSnapshot(int Count, DateTimeOffset? MaxReceivedAtUtc, ISet<Guid> EventIds);
```

`AssertOtherStoresUnchanged` 对不在 `{1042,1005}` 的每个可见门店比较 `Count`、`MaxReceivedAtUtc`、`EventIds`；任何差异都失败。错误信息只列门店和事件 ID，不列敏感字段。

- [ ] **Step 4: 实现五类事件有界轮询和详情验证**

固定必需集合：

```csharp
private static readonly HashSet<string> RequiredOperationTypes =
[
    "CASHIER_LOGIN",
    "CART_ITEM_ADD",
    "CART_ITEM_QUANTITY_CHANGE",
    "PAYMENT_TENDER_ADD",
    "SALE_COMPLETE",
];
```

先按 `storeCode + deviceCode + fromUtc/toUtc` 找到本次具有非空 `InstanceId` 的 `CASHIER_LOGIN`，读取其 `cashierId + InstanceId`；再以 `cashierId` 作为 `cashierKeyword` 轮询，并在客户端按该 `InstanceId` 精确过滤。客户端必须再次验证所有五类事件的 StoreCode、DeviceCode、CashierId、InstanceId 和 `Outcome=Succeeded`。`PAYMENT_TENDER_ADD` 必须 `PaymentMethod=Cash`；`SALE_COMPLETE` 必须 `ReasonCode=SALE` 且 `OrderGuid` 为 GUID。

对本次新增的 `CART_ITEM_ADD`、`CART_ITEM_QUANTITY_CHANGE`、`PAYMENT_TENDER_ADD`、`SALE_COMPLETE` 调用详情 GET，断言至少一个 item 的 `productCode/itemNumber/referenceCode/lookupCode` 与测试商品条码完全匹配。轮询使用 120 秒硬截止，间隔 2 秒；超时时只输出已观察到的 OperationType 和最后一个固定脱敏验证原因，不输出条码或 token。

- [ ] **Step 5: 轮询后台 POSM 订单详情**

从 `SALE_COMPLETE.orderGuid` 调用：

```text
GET /api/react/v1/posm-sales-orders/detail/{orderGuid}
```

最多 120 秒，直到 HTTP 200、`success=true`、`data.order` 非空；若 `data.order.branchCode` 有值，必须等于目标门店。该步骤证明纯现金成功页后的本地 Pending 订单已由后台同步处理，而不是只停留在 WPF 本地库。

- [ ] **Step 6: 把只读前后快照接入真实 UI 测试**

在任何 WPF 启动、收银员输入或商品扫码前：

```csharp
var runStartedUtc = DateTimeOffset.UtcNow;
var windowFrom = runStartedUtc.AddMinutes(-2);
var windowTo = runStartedUtc.AddMinutes(10);
using var backend = new OperationAuditBackendClient(config.BackendBaseUrl, config.BackendBearerToken);
var beforeRows = await backend.FetchAllAsync(new AuditQuery(windowFrom, windowTo), CancellationToken.None);
var before = AuditSnapshot.Create(beforeRows);
```

UI 成功后先保存成功页截图，再关闭自有 WPF 进程触发最后一次 outbox flush。随后：

```csharp
var required = await backend.PollRequiredAsync(
    new AuditQuery(windowFrom, windowTo, config.StoreCode, device.DeviceCode),
    TimeSpan.FromSeconds(120),
    CancellationToken.None);
var sale = RequiredEventValidator.Validate(required, config.StoreCode, device.DeviceCode);
await backend.PollOrderAsync(sale.OrderGuid!, config.StoreCode, CancellationToken.None);
var afterRows = await backend.FetchAllAsync(new AuditQuery(windowFrom, windowTo), CancellationToken.None);
var after = AuditSnapshot.Create(afterRows);
AuditSnapshot.AssertOtherStoresUnchanged(before, after, new HashSet<string> { "1042", "1005" });
```

同时计算 `newIds = after.EventIds - before.EventIds`，只对新增 EventId 查详情；断言目标 DeviceCode 的所有新增事件都属于当前目标 StoreCode。

- [ ] **Step 7: 运行纯逻辑测试和 Preview 回归**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter "FullyQualifiedName~OperationAuditBackendClientTests|FullyQualifiedName~AuditSnapshotTests|FullyQualifiedName~LiveE2eConfigurationTests|FullyQualifiedName~LiveDeviceBindingTests" --logger "console;verbosity=minimal"
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --no-restore --filter FullyQualifiedName~StartupSmokeTests --logger "console;verbosity=minimal"
```

Expected: 所有纯逻辑测试 PASS；Preview 冒烟 PASS；没有真实业务写入。

- [ ] **Step 8: 在凭据齐全时分别运行两个门店**

门店 `1042`：

```powershell
$env:HBPOS_E2E_ENABLED='true'
$env:HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED='true'
$env:HBPOS_E2E_STORE_CODE='1042'
$env:HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED='true'
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --no-restore --filter FullyQualifiedName~SaleFlowTests --logger "console;verbosity=normal"
```

门店 `1005` 使用相同命令，仅替换 `HBPOS_E2E_STORE_CODE`；两个门店必须在各自已注册的 Windows 配置上串行运行。其他必需变量由受保护的运行环境提供，命令和测试输出不得回显其值。

Expected per store: 1 条真实现金销售完成；支付成功页出现；后台订单详情可读；五类 Succeeded 操作日志齐全；其他门店快照不变。若当前会话未提供真实变量，该步骤必须在首次业务操作前以缺少变量名失败，不能猜测或读取源码内秘密。

- [ ] **Step 9: 检查并提交**

```powershell
git diff --check
git add apps/pos-wpf/tests/Hbpos.Client.UiTests/SaleFlowTests.cs
git commit -m "测试：验证销售订单与后台操作日志全链路 reasonix"
```

---

### Task 4: 全量验证、影响检测与交付审查

**Files:**
- Verify only: all files changed by Tasks 1-3

**Interfaces:**
- Consumes: all previous tasks
- Produces: clean review package and verification evidence

- [ ] **Step 1: 运行现有 WPF 相关回归**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --no-restore --filter "FullyQualifiedName~AppStartupOptionsTests|FullyQualifiedName~OperationAuditViewModelTests|FullyQualifiedName~MainViewModelScannerTests" --logger "console;verbosity=minimal"
```

Expected: 157 tests PASS，0 failed。

- [ ] **Step 2: 运行新项目非真实写入测试**

```powershell
dotnet test apps/pos-wpf/tests/Hbpos.Client.UiTests/Hbpos.Client.UiTests.csproj --filter "FullyQualifiedName!~SaleFlowTests.Store_cash_sale_reaches_payment_success" --logger "console;verbosity=minimal"
```

Expected: 配置、SQLite、JSON、快照测试全部 PASS；Preview 冒烟 PASS。命令不触发真实销售。

- [ ] **Step 3: 检查改动范围**

```powershell
git status --short
git diff --check HEAD~3..HEAD
git log --oneline -3
```

Expected: 只包含 UI 测试项目、四个 WPF XAML 文件和两份设计/计划文档；没有截图、日志、数据库或密钥文件。

- [ ] **Step 4: 运行 GitNexus 变更检测**

对隔离 worktree `D:\DevRepos\hb-platform\.worktrees\wpf-ui-e2e` 运行：

```text
detect_changes(scope="compare", base_ref="5a9b1d5443890b5ada310d4934aed7e79cfd2438", worktree="D:\DevRepos\hb-platform\.worktrees\wpf-ui-e2e")
```

Expected: 生产改动仅为 XAML AutomationId 元数据，无 ViewModel、支付、落库或后台写入执行流变化。

- [ ] **Step 5: 独立代码审查**

审查必须验证两项独立结论：

1. Spec compliance：1042/1005 白名单、全门店 token 确认、本机 DeviceCache 预检、只读后台 GET、无自动清理、两门店串行是否完整。
2. Code quality：FlaUI 进程所有权、UIA 对象释放、Retry 超时、密码脱敏、分页终止条件、快照差异、截图路径是否可靠。

Critical/Important 问题必须由实现代理修复并再次审查；全部通过后才可交付。
