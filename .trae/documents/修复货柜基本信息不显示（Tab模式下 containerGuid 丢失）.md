## 原因分析
- 在 Tab 布局下，URL 固定为 `/home`，`useParams()` 读取不到 `:containerGuid`。
- 页面依赖 `props.containerGuid || paramsFromRoute.containerGuid`；若未从 Tab 打开或未传入 `params`，`containerGuid` 为空，`loadContainerInfo()` 不会执行，导致“货柜基本信息”不显示。
- 从货柜列表打开时已正确传 `params.containerGuid`（`src/pages/ContainerList/index.tsx:110-127`）；但从菜单或其他入口打开 `ContainerDetail` 时可能未带 `params`。

## 修复方案
- 页面容错：在 `ContainerDetail` 中增加第三个来源的容错解析：
  1. 优先 `props.containerGuid`
  2. 其次 `useParams().containerGuid`
  3. 最后从当前 Tab 的 `path` 解析：`/container/container-detail/{guid}`
- 实现步骤：
  - 在 `ContainerDetail/index.tsx` 读取 `window.g_tabModel`：取当前激活 Tab 的 `path`，用正则 `^/container/container-detail/(.+)$` 提取 GUID，作为最终 `containerGuid` 的 fallback。
  - 在 `loadContainerInfo()` 前输出一次调试日志，便于定位传参问题。
- 加强入口一致性：
  - 在 `src/app.ts` 的 `pathComponentMap` 中为 `'/container/container-detail'` 的点击处理补充 `params` 传递（当菜单或其他入口触发时，根据用户操作上下文传递 GUID）。

## 验证
- 从货柜列表打开明细：应显示基本信息（已有 `params`）。
- 从菜单或无路由参数入口打开：通过 Tab `path` fallback 自动解析 GUID，基本信息显示正常。
- 观察控制台日志，确认 GUID 来源与加载结果。