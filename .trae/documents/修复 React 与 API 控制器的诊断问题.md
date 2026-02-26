## 概览
- 目标：清理当前 TypeScript 与 C# 诊断/Lint 问题，保证类型一致、风格统一与空引用安全。
- 影响范围：`ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx`、`ReactUmi/my-app/src/types/storeRetailPrice.ts`、`ReactUmi/my-app/src/services/storeRetailPriceService.ts`（只读确认）、`BlazorApp.Api/Controllers/CategoriesController.cs`、`BlazorApp.Api/Controllers/DebugController.cs`、`BlazorApp.Api/Controllers/DomesticSupplierController.cs`。

## 问题与原因
1. StoreRetailPrices 页面
- 使用 `GridResponse` 时读取了小写属性 `items/total`，但类型声明为大写 `Items/Total`，导致属性不存在错误。
- `batchUpsert` 入参为 `StoreRetailPriceUpsertItemDto[]`（必填 `StoreCode/ProductCode/SupplierCode`），但当前从编辑收集的是 `Partial<...>`，且仅有 `UUID` 与变更字段，触发类型不兼容（`string | undefined` → `string`）。
- `sort` 回调参数未显式标注类型，在 `noImplicitAny` 下触发隐式 `any` 报错。

2. CategoriesController 风格警告
- Lint 提示若干使用指令顺序、对象初始化与逗号/换行缩进的风格一致性问题，需要统一代码风格以消除警告（功能正确但不符合当前规则）。

3. 空引用风险
- `DebugController.cs:405` 二级导航 `Includes(wp => wp.Product, p => p.WarehouseCategory)` 被分析器标记为可能空引用链。
- `DomesticSupplierController.cs:125/166` 对 `ModelState` 的 `Value.Errors` 未做空值保护，存在潜在空引用。

## 修复方案
1. 修正 GridResponse 读取属性
- 在 `StoreRetailPrices/index.tsx` 中将 `res.data?.data?.items`/`total` 改为 `res.data?.data?.Items`/`Total`。
- 保持与 `types/storeRetailPrice.ts:23-28` 的类型一致。

2. 解决 `batchUpsert` 类型不兼容
- 方案A（推荐）：调整 `StoreRetailPriceUpsertItemDto` 使 `StoreCode/ProductCode/SupplierCode` 改为可选，以反映“更新场景仅凭 `UUID` 即可”的后端约定；`Create` 场景仍使用 `CreateStoreRetailPriceDto` 保持必填。
- 方案B（备选）：保留类型不变，在提交前将 `editedMap` 中的条目映射为完整对象：通过当前行的 `productCode` 以及后端查询或上下文补齐 `StoreCode/SupplierCode`；成本与耦合更高。
- 本次采用方案A：修改 `types/storeRetailPrice.ts:91-101` 三个字段为可选，并保留 `UUID?: string`。

3. 为 sort 回调添加类型标注
- 在 `StoreRetailPrices/index.tsx` 的 `so.sort(...)` 与 `lo.sort(...)` 显式标注 `(a: {label: string; value: string}, b: {label: string; value: string})`，消除隐式 any。

4. 统一 CategoriesController 代码风格
- 按当前 Lint 建议：
  - 使用指令顺序统一为 `System* → Microsoft* → 项目命名空间`。
  - 简单对象初始化可压缩为单行或在多行结尾添加逗号、统一缩进（根据项目既有风格）。
  - 将若干返回体调整为一致风格，消除“Insert/Replace/Delete”类风格警告。
- 不改动业务逻辑。

5. 修复空引用警告
- `DebugController.cs`：将二级导航 includes 改为链式并使用空值断言避免分析器误报，例如：
  - `.Includes(wp => wp.Product).Includes(wp => wp.Product!.WarehouseCategory)`。
- `DomesticSupplierController.cs`：对 `ModelState` 空安全处理：
  - `var errors = ModelState.SelectMany(x => (x.Value?.Errors ?? Enumerable.Empty<ModelError>()).Select(e => e.ErrorMessage));`

## 预计修改的文件
- `src/pages/PosAdmin/StoreRetailPrices/index.tsx`：属性大小写修正；sort 回调类型标注。
- `src/types/storeRetailPrice.ts`：`StoreRetailPriceUpsertItemDto` 三字段改为可选。
- `BlazorApp.Api/Controllers/CategoriesController.cs`：使用指令顺序与返回体风格统一（按 Lint 要求）。
- `BlazorApp.Api/Controllers/DebugController.cs`：调整 `Includes` 写法避免空引用警告。
- `BlazorApp.Api/Controllers/DomesticSupplierController.cs`：`ModelState` 错误收集空值保护。

## 验证
- 前端：`StoreRetailPrices` 页面加载与保存无类型错误；批量保存返回统计正常展示。
- 后端：编译通过且无新的警告；接口返回格式维持不变；增加两处空安全后功能不变。

请确认以上方案，确认后我将一次性提交对应修改并自检。