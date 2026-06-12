**问题分析**
- 后端接收入口使用 `POST` + `[FromBody]`，签名正确：`BlazorApp.Api/Controllers/React/ReactStoreRetailPricesController.cs:22-28`。
- 服务层已按 `request.FilterModel` 遍历应用筛选：`BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs:59-181`。
- 前端构造请求体 `GridRequestDto` 并通过 `POST` 发送：`ReactUmi/my-app/src/services/storeRetailPriceService.ts:15-19`；类型字段与后端 DTO 一致：`ReactUmi/my-app/src/types/storeRetailPrice.ts:1-16`。
- 触发筛选的事件：`afterFilter(conditionsStack)` 中设置了 `headerFilters` 后立即调用 `loadData()`：`ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx:216-221`。
- React 状态更新是异步的，`setHeaderFilters(...)` 后立刻调用 `loadData()` 会拿到旧的 `headerFilters`，导致 `buildGridRequest()` 中 `FilterModel` 为空：`index.tsx:223-241`。
- 同理，排序事件 `afterColumnSort` 也有相同问题：`index.tsx:164-179`。

**修复方案**
1. 移除事件中对 `loadData()` 的立即调用，改为用 `useEffect` 监听状态变更后拉取数据：
   - 在 `afterFilter` 中仅计算 `model` 并 `setHeaderFilters(model)`；不直接调用 `loadData()`。
   - 在 `afterColumnSort` 中仅 `setHeaderSort(...)`；不直接调用 `loadData()`。
   - 新增 `useEffect(() => { setPage(1); loadData(); }, [headerFilters, headerSort])`，确保最新状态参与构造请求体。
2. 为彻底消除竞态，`buildGridRequest()` 支持可选覆盖参数：`buildGridRequest(overrideFilters?, overrideSort?)`，事件中传递最新 `model`/`sort` 时直接构造请求体（可选方案，若不想新增 `useEffect`）。
3. 校验字段映射：
   - 过滤键使用后端支持的列名（如 `storeCode/supplierCode/itemNumber/...`），当前实现已匹配：`StoreRetailPriceReactService.cs:74-105, 145-179`。
   - 过滤值类型保持字符串，数值过滤后端再解析为 `decimal`：`StoreRetailPriceReactService.cs:107-143`。
4. 加入最小化调试日志（可选）：在前端 `loadData()` 中 `console.log('GridRequest', req)`；后端在进入服务层时记录 `FilterModel?.Count` 与键列表，便于验证。

**预期结果**
- 应用列头筛选或排序后，请求体 `FilterModel`/`SortModel` 不再为空；后端 `request.FilterModel.Any()` 为真，过滤生效。
- 解决同类状态更新时序问题（筛选与排序）。

如确认方案，我将按上述步骤修改 `index.tsx` 并验证请求体与后端筛选效果。