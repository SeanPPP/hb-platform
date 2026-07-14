# WPF 客户端服务器地址设置 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在设备注册页和现有设置页安全修改 Hbpos.Api 基址，并让 Release 客户端默认连接 `https://hotbargain.vip/pos-api/`。

**Architecture:** 保留 `HBPOS_API_BASE_URL` 作为唯一运行时来源。新增可测试的服务器地址服务、共享 ViewModel 和共享 WPF 面板，保存到 Windows 用户级环境变量；所有已有 HTTP 客户端只在下次启动时读取新值。

**Tech Stack:** .NET 8、WPF、CommunityToolkit.Mvvm、MaterialDesignInXaml、xUnit

## Global Constraints

- Release 默认地址必须是 `https://hotbargain.vip/pos-api/`；Debug 默认地址保持 `http://localhost:5159/`。
- `HBPOS_API_BASE_URL` 覆盖优先级保持不变；不得运行中热切换已有 HTTP 客户端。
- 远程地址必须使用 HTTPS；loopback 可使用 HTTP；拒绝相对 URI、非 HTTP(S)、userinfo、query 和 fragment。
- 保存前必须通过 `GET api/v1/health`；失败不得覆盖用户级环境变量。
- 两个入口复用同一 ViewModel 和控件；设置页复用设备注册权限，不新增权限或数据库表。
- 所有新增用户文案提供中英文资源，关键逻辑使用清晰中文注释，不添加第三方依赖。

---

### Task 1: 服务器地址服务、默认值和共享 ViewModel

**Files:**
- Create: `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/ApiServerSettingsService.cs`
- Create: `apps/pos-wpf/src/Hbpos.Client.Wpf/ViewModels/ApiServerSettingsViewModel.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/ServiceRegistration.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Resources/SettingsStrings.resx`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Resources/SettingsStrings.zh-CN.resx`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/ApiServerSettingsServiceTests.cs`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/ApiServerSettingsViewModelTests.cs`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/ServiceRegistrationSquareClientTests.cs`

**Interfaces:**
- Produce `ApiServerSettingsService.GetCurrentAddress()`、`NormalizeAddress(string)`、`TestConnectionAsync(string, CancellationToken)`、`SaveUserAddress(string)`。
- Produce `ApiServerSettingsViewModel` with `ServerAddressText`、`StatusMessage`、`IsBusy`、`RestartRequired`、`TestConnectionCommand`、`SaveCommand` and `Load()`。

- [ ] **Step 1: 写失败测试**

  增加测试覆盖：Release 常量、公网 HTTPS 规范化、loopback HTTP、非法协议/相对地址/query/fragment/userinfo 拒绝、健康检查 URL 与响应、保存前健康检查、保存新值后 `RestartRequired=true`、失败不写入。

- [ ] **Step 2: 验证 RED**

  Run: `dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~ApiServerSettings|FullyQualifiedName~ServiceRegistrationSquareClientTests" --artifacts-path .artifacts/server-address-task1-red`

  Expected: FAIL，原因是新服务/ViewModel/Release 默认常量尚不存在。

- [ ] **Step 3: 最小实现**

  实现集中 URI 规范化和 5 秒健康检查；生产保存使用 `Environment.SetEnvironmentVariable("HBPOS_API_BASE_URL", normalized, EnvironmentVariableTarget.User)`。`SaveCommand` 先测试再保存；只有规范化地址与当前进程地址不同时标记重启。`GetApiBaseAddress()` 只调整编译配置默认值，不改变环境变量解析和尾斜杠行为。

- [ ] **Step 4: 验证 GREEN**

  Run: `dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~ApiServerSettings|FullyQualifiedName~ServiceRegistrationSquareClientTests" --artifacts-path .artifacts/server-address-task1-green`

  Expected: PASS，0 failed。

- [ ] **Step 5: 提交**

  `git commit -am "实现客户端服务器地址配置服务 reasonix"`，并显式添加新文件。

### Task 2: 接入设备注册页和现有设置页

**Files:**
- Create: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Controls/ApiServerSettingsPanel.xaml`
- Create: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Controls/ApiServerSettingsPanel.xaml.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/ViewModels/DeviceRegistrationViewModel.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/ViewModels/SettingsViewModel.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/ViewModels/Main/MainChildViewModelFactory.cs`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/DeviceRegistrationView.xaml`
- Modify: `apps/pos-wpf/src/Hbpos.Client.Wpf/Views/Screens/SettingsView.xaml`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/DeviceRegistrationViewModelTests.cs`
- Test: `apps/pos-wpf/tests/Hbpos.Client.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Consume `ApiServerSettingsViewModel` from Task 1.
- Parents expose non-null `ApiServerSettings` and factories inject the shared service-backed instances.

- [ ] **Step 1: 写失败测试**

  增加测试覆盖：注册 ViewModel 保存新地址后禁用注册/验证；设置 ViewModel 加载地址；两份 XAML 都引用共享面板；设置入口仍受设备注册权限控制。

- [ ] **Step 2: 验证 RED**

  Run: `dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~DeviceRegistrationViewModelTests|FullyQualifiedName~SettingsViewModelTests" --artifacts-path .artifacts/server-address-task2-red`

  Expected: FAIL，原因是父 ViewModel 和共享面板尚未接入。

- [ ] **Step 3: 最小实现**

  在注册卡片门店选择器前加入共享面板；在设置页“更换分店注册”内容顶部加入同一面板。父 ViewModel 监听 `RestartRequired`，注册流程立即刷新命令状态并阻止继续注册；设置页只显示重启提示。沿用现有样式和状态颜色，不创建新设置分类。

- [ ] **Step 4: 验证 GREEN 和集成构建**

  Run: `dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~ApiServerSettings|FullyQualifiedName~DeviceRegistrationViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~ServiceRegistrationSquareClientTests" --artifacts-path .artifacts/server-address-task2-green`

  Run: `dotnet build apps/pos-wpf/src/Hbpos.Client.Wpf/Hbpos.Client.Wpf.csproj --artifacts-path .artifacts/server-address-build`

  Expected: tests PASS，build exit 0。

- [ ] **Step 5: 提交**

  `git commit -am "添加客户端服务器地址设置入口 reasonix"`，并显式添加新 XAML 文件。

### Task 3: 最终验证与审查修复

**Files:**
- Review all files changed by Tasks 1-2.

**Interfaces:**
- Consume the complete feature; produce review-clean and verified branch state.

- [ ] **Step 1: 独立代码审查**

  审查信任边界、URI 规范化、环境变量持久化、注册命令阻断、XAML 可访问性和中英文文案，修复所有 Critical/Important findings。

- [ ] **Step 2: 完整定向验证**

  Run: `dotnet test apps/pos-wpf/tests/Hbpos.Client.Tests/Hbpos.Client.Tests.csproj --filter "FullyQualifiedName~ApiServerSettings|FullyQualifiedName~DeviceRegistrationViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~ServiceRegistrationSquareClientTests" --artifacts-path .artifacts/server-address-final`

  Run: `dotnet build apps/pos-wpf/src/Hbpos.Client.Wpf/Hbpos.Client.Wpf.csproj --artifacts-path .artifacts/server-address-final-build`

  Run: `git diff --check main...HEAD`

  Expected: tests/build PASS，diff check 无输出。

- [ ] **Step 3: GitNexus 变更检测**

  Run: `node .gitnexus/run.cjs detect-changes -r hb-platform --scope compare --base-ref main`

  Expected: 变更仅覆盖服务器地址配置、设备注册和设置页面；如风险升至 HIGH/CRITICAL，回查调用链并在交付说明中明确。
