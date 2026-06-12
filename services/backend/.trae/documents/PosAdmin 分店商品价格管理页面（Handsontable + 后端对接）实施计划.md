## 页面目标
- 在 `src/pages/PosAdmin` 下新增“分店商品价格管理”页面，使用 Handsontable 展示并编辑分店商品价格数据。
- 对接后端 `/api/react/v1/store-retail-prices`，支持读取与批量保存（事务）。
- 可编辑列：`IsAutoPricing`、`DiscountRate`、`IsActive`、`StoreRetailPriceValue`、`PurchasePrice`。
- 支持按分店/供应商/商品进行服务端过滤与排序（通过 `GridRequestDto`）。

## 技术与依赖
- 现有依赖：`handsontable`、`@handsontable/react` 已在 `package.json` 中引入。
- 前端服务：已存在 `src/services/storeRetailPriceService.ts` 与类型 `src/types/storeRetailPrice.ts`。
- 认证：统一通过 `src/utils/request.ts` 携带 Token；401 自动刷新已封装。

## 路由与文件
- 新建目录：`src/pages/PosAdmin/StoreRetailPrices/`
- 新建页面：`index.tsx`
-（如需要）在 `src/pages/PosAdmin/index.tsx` 或上层菜单集成入口，但本次仅实现页面本身。

## 数据模型与接口
- 列表类型：`StoreRetailPriceListDto`（显示用）
- 批量保存：`StoreRetailPriceUpsertItemDto[]` → `POST /batch-upsert`
- 服务端分页/过滤/排序：`GridRequestDto` → `POST /grid`

## 页面结构
- 顶部工具栏：
  - 过滤输入：`StoreCode`、`SupplierCode`、`ProductCode`（文本）；可扩展为下拉选择（复用现有供应商/分店服务）。
  - 排序选择：字段与方向（简化为单字段）。
  - 操作按钮：`查询`、`保存修改`、`重置`。
- Handsontable 表格：
  - 列定义：
    - 只读字段：`StoreCode`、`StoreName`、`SupplierCode`、`SupplierName`、`ProductCode`、`ProductName`、`UpdatedAt`
    - 可编辑字段：`IsAutoPricing`（checkbox）、`IsActive`（checkbox）、`DiscountRate`（numeric，保留 4 位小数，范围 0~1）、`StoreRetailPriceValue`（numeric，2 位小数，非负）、`PurchasePrice`（numeric，2 位小数，非负）
  - 单元格编辑校验：
    - 数值字段非负校验、精度控制（Handsontable `numeric` 类型 + 自定义 validator）。
    - 布尔字段用 `checkbox` 渲染器。
- 状态管理：
  - 本地维护 `editedMap`（以 `UUID` 为键），记录用户修改的字段和值。
  - 点击“保存修改”时，将 `editedMap` 转换为 `StoreRetailPriceUpsertItemDto[]`，调用 `batchUpsert`。

## 交互与数据流
1. 加载：页面初始化时发送 `getGrid({ StartRow: 0, EndRow: PageSize-1, PageSize, GlobalSearch, FilterModel, SortModel })`。
2. 展示：将返回的 `Items` 映射到 Handsontable `data`。
3. 编辑：表格 `afterChange` 钩子中更新 `editedMap`。
4. 保存：点击“保存修改”按钮 → `batchUpsert(editedItems)` → 成功后提示、清空 `editedMap`、刷新列表。
5. 过滤与排序：表单提交后重发 `getGrid` 请求，后端按筛选与排序返回结果。

## 错误处理
- 接口失败：使用 `antd` 的 `message.error`。批量保存返回 `Errors` 时逐条提示或汇总显示。
- 校验失败：在本地阻止保存并高亮错误单元格（Handsontable `setCellMeta` + `invalid` 类）。

## 性能与分页
- 初始实现：固定 `PageSize`（如 100）分页加载。
- 后续可拓展：滚动加载/下一页加载；或通过顶部分页控件切换页码。

## 实现步骤
1. 新建 `src/pages/PosAdmin/StoreRetailPrices/index.tsx`，引入 `@handsontable/react`、`antd`、`storeRetailPriceService` 与类型。
2. 编写过滤表单与工具栏（`Form` + `Input` + `Select` + `Button`）。
3. 定义 Handsontable 列配置与渲染器/编辑器；实现 `afterChange` 捕获变更并更新 `editedMap`。
4. 接入 `getGrid` 获取数据；支持将表单转为 `GridRequestDto` 的 `FilterModel`、`SortModel`。
5. 实现“保存修改”按钮，构造 `StoreRetailPriceUpsertItemDto[]` 调用 `batchUpsert` 并处理返回。
6. 成功后刷新数据并清理本地编辑状态。

## 注意事项
- 现有 TS 报“找不到模块 '@/types/storeRetailPrice'”：文件已存在，若仍报错是 TS 索引缓存问题；页面创建后将重启前端 dev 服务以恢复索引。
- 价格字段精度：`StoreRetailPriceValue/PurchasePrice` 两位小数，`DiscountRate` 四位小数，范围校验。

## 交付与验证
- 页面渲染并加载数据，编辑上述 5 个字段并批量保存成功。
- 通过筛选/排序进行服务端验证，检查响应数据的变更与总数是否匹配。
- 提供示例过滤：按 `StoreCode=ST01`、`SupplierName contains "义乌"`、价格区间过滤。
