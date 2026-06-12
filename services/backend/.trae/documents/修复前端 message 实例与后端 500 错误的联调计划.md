## 问题定位
- 前端：`message.error is not a function`，因在同一组件内包裹 `<App>` 后仍使用 `App.useApp()`，该钩子在当前树未提供上下文。
- 后端：`POST /api/react/v1/product-set-codes/grid` 返回 500；日志包含 `System.InvalidOperationException: Unable to resolve service for type ...`，推测未注册 `IProductSetCodeReactService` 到 DI 容器。

## 前端改动
1. 消除 message 实例错误
- 在 `ReactUmi/my-app/src/pages/MultiCodeSets/index.tsx`：改用 `message.useMessage()` 生成实例：
```tsx
import { message } from 'antd';
const [messageApi, contextHolder] = message.useMessage();
```
- 将所有 `message.*` 替换为 `messageApi.*`，并在 JSX 顶部渲染 `{contextHolder}`。
- 移除本页面中的 `<App>` 包裹或改为在更高层（如 `KeepAliveTabLayout`）统一包裹。

2. 其他前端提醒
- 搜索区使用 `Space.Compact`（已应用），避免 `addonAfter` 废弃警告。
- Handsontable 使用 `className="ht-theme-default"`（已应用），避免 classic 主题废弃警告。
- 排序映射：确认 `afterColumnSort` 将列索引映射到 `columns[index].data`，形成 `{ colId, sort }`（已应用）。

## 后端改动
1. 注册 React 服务到 DI
- 在 `BlazorApp.Api/Program.cs` 或 `Startup.cs` 中，添加：
```csharp
services.AddScoped<IProductSetCodeReactService, ProductSetCodeReactService>();
```
- 确保控制器命名空间与服务命名空间导入正确。

2. 验证路由与授权
- 控制器：`ReactProductSetCodesController` 路由 `api/react/v1/product-set-codes` 已存在。
- 授权：保证前端传递的 Token 有效与角色权限包含 `Admin`/`WarehouseManager`。

## 联调验证
- 启动后端与前端，进入 `/pos-admin/multi-code-sets`。
- 检查：前端不再出现 message 实例错误；请求返回 200，表格正常显示；排序/过滤/分页与批量操作功能可用。

## 范围与安全
- 仅修改上述页面与 DI 注册；不影响其它模块。