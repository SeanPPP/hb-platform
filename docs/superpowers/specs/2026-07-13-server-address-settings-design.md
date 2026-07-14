# WPF 客户端服务器地址设置设计

## 目标

让门店管理员在设备注册页和现有设置页修改 Hbpos.Api 基址，不再依赖手工 PowerShell。Release 客户端默认连接 `https://hotbargain.vip/pos-api/`，Debug 继续使用 `http://localhost:5159/`。

## 设计

- 新增一套共享的服务器地址设置 ViewModel 和 WPF 控件，分别嵌入设备注册页、设置页“更换分店注册”区域。
- 服务端地址仍由 `HBPOS_API_BASE_URL` 统一驱动。保存时写入 Windows 当前用户级环境变量，客户端重启后由现有 `GetApiBaseAddress()` 统一创建所有 HTTP 客户端。
- 不做运行时热切换。当前进程已有多组长生命周期 `HttpClient`，热切换会产生新旧服务器混用。
- 输入只接受绝对 HTTP/HTTPS URI。远程地址必须使用 HTTPS；localhost/loopback 可使用 HTTP；拒绝用户名密码、查询串和片段；规范化为尾部 `/`。
- “测试连接”调用目标地址的 `GET api/v1/health`，要求 HTTP 200 且返回在线状态。“保存”会再次测试，成功后才持久化。
- 保存值与当前进程地址不同时展示“重启后生效”。设备注册页同时禁用注册/验证操作，避免继续使用旧地址注册。
- 设置页沿用 `Permissions.PosTerminal.Settings.DeviceRegistration`；首次设备注册页面不要求收银员权限，以保证错误地址可恢复。

## 交互与视觉

- 沿用现有 `PosSearchTextBoxStyle`、主次按钮、MaterialDesign 图标、8px 圆角和状态栏语言，不引入依赖。
- 字段标签始终显示，错误信息紧邻字段；按钮包含正常、忙碌、连接失败、保存成功和等待重启状态。
- 中英文资源同步提供，所有新增代码关键逻辑使用中文注释。

## 验收

- Release 默认地址为公网 HTTPS，Debug 默认地址不变，环境变量覆盖优先级不变。
- 两个页面均能显示、测试并保存同一格式的服务器地址。
- 非法地址或健康检查失败不会覆盖已保存地址。
- 保存新地址后明确提示重启，注册按钮在重启前不可执行。
- 定向单元测试、WPF 构建、`git diff --check` 和 GitNexus `detect-changes` 通过。
