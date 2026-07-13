# 仓库货位批量解绑商品 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在仓库标签管理页允许选择已绑定商品的货位，并一次解绑所选货位中的全部商品关联。

**Architecture:** 复用现有 `DELETE /api/react/v1/locations/{locationGuid}/products/{productCode}` 接口，不扩展后端。前端服务层负责逐项解绑并汇总成功、失败结果；页面用 Ant Design 表格多选、危险按钮和二次确认承载操作，未绑定商品的货位不可选。

**Tech Stack:** React 18、TypeScript、Ant Design、现有 request 封装、esbuild 脚本测试。

---

### Task 1: 批量解绑服务契约

**Files:**
- Create: `apps/web/src/services/locationService.bulkUnbind.test.ts`
- Modify: `apps/web/src/services/locationService.ts`
- Modify: `apps/web/src/types/location.ts`
- Modify: `apps/web/package.json`

- [ ] **Step 1: 写失败测试**

新增服务测试，构造两个绑定项，其中一个返回成功、一个返回 500；断言：

```ts
const result = await batchUnbindLocationProducts([
  { locationGuid: 'location-1', productCode: 'P-1' },
  { locationGuid: 'location-2', productCode: 'P/2' },
])

assert(calls[0].url.endsWith('/location-1/products/P-1'), '应调用第一条解绑接口')
assert(calls[1].url.endsWith('/location-2/products/P%2F2'), '路径参数必须编码')
assert(result.succeeded.length === 1, '应汇总成功项')
assert(result.failed.length === 1, '应汇总失败项')
```

同时把该测试接入 `test:warehouse-locations`。

- [ ] **Step 2: 运行测试并确认 RED**

Run: `npm --prefix apps/web run test:warehouse-locations`

Expected: FAIL，提示 `batchUnbindLocationProducts` 尚未导出。

- [ ] **Step 3: 写最小实现**

在类型文件中增加绑定项和批量结果类型：

```ts
export interface LocationProductBinding {
  locationGuid: string
  productCode: string
}

export interface LocationProductUnbindFailure extends LocationProductBinding {
  message: string
}

export interface BatchUnbindLocationProductsResult {
  succeeded: LocationProductBinding[]
  failed: LocationProductUnbindFailure[]
}
```

服务层对每个绑定调用现有 DELETE，路径使用 `encodeURIComponent`，用 `Promise.all` 将每一项转换为成功或失败结果，避免单项失败中断整批：

```ts
export async function batchUnbindLocationProducts(
  bindings: LocationProductBinding[],
): Promise<BatchUnbindLocationProductsResult> {
  const results = await Promise.all(bindings.map(async (binding) => {
    try {
      await request.delete<ApiResponse<LocationItem>>(
        `${API_BASE}/${encodeURIComponent(binding.locationGuid)}/products/${encodeURIComponent(binding.productCode)}`,
      )
      return { binding }
    } catch (error) {
      return {
        binding,
        message: error instanceof Error ? error.message : '解绑商品失败',
      }
    }
  }))

  return {
    succeeded: results.filter((item) => !item.message).map((item) => item.binding),
    failed: results
      .filter((item) => item.message)
      .map((item) => ({ ...item.binding, message: item.message! })),
  }
}
```

- [ ] **Step 4: 运行测试并确认 GREEN**

Run: `npm --prefix apps/web run test:warehouse-locations`

Expected: PASS。

### Task 2: 表格选择与批量解绑交互

**Files:**
- Modify: `apps/web/src/pages/Warehouse/Locations/warehouseLocationsCompactUi.logic.test.ts`
- Modify: `apps/web/src/pages/Warehouse/Locations/index.tsx`
- Modify: `apps/web/src/i18n/locales/zh.json`
- Modify: `apps/web/src/i18n/locales/en.json`

- [ ] **Step 1: 写失败测试**

扩展页面契约测试，断言页面具备：`selectedRowKeys`、`rowSelection`、未绑定商品行禁选、`batchUnbindLocationProducts`、`Modal.confirm`、执行期间 loading、成功/部分失败提示、刷新后清空选择，以及中英文批量解绑文案。

- [ ] **Step 2: 运行测试并确认 RED**

Run: `npm --prefix apps/web run test:warehouse-locations`

Expected: FAIL，提示缺少批量解绑 UI。

- [ ] **Step 3: 写最小页面实现**

页面增加选择和 loading 状态：

```ts
const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([])
const [batchUnbinding, setBatchUnbinding] = useState(false)
const selectedRows = data.filter((item) => selectedRowKeys.includes(item.locationGuid))
const selectedBindings = selectedRows.flatMap((item) =>
  item.products
    .filter((product): product is LocationProduct & { productCode: string } => Boolean(product.productCode))
    .map((product) => ({ locationGuid: item.locationGuid, productCode: product.productCode })),
)
```

表格增加行选择，空货位不可选：

```tsx
rowSelection={{
  selectedRowKeys,
  onChange: setSelectedRowKeys,
  getCheckboxProps: (record) => ({ disabled: !record.products.some((product) => product.productCode) }),
  preserveSelectedRowKeys: false,
}}
```

筛选区下方、表格上方增加紧凑操作条。无选择时按钮禁用；确认框明确展示货位数和商品关联数。确认后调用服务，完全成功时显示成功消息；部分失败时显示 warning 并保留失败货位选择；全部失败时显示 error。至少有一项成功时刷新当前页。

- [ ] **Step 4: 补齐中英文文案**

增加 `batchUnbind`、`selectedLocations`、`batchUnbindTitle`、`batchUnbindContent`、`batchUnbindConfirm`、`batchUnbindSuccess`、`batchUnbindPartialFailed`、`batchUnbindFailed` 等语义明确的键值，中文使用“货位/商品关联”，英文使用 “location/product links”。

- [ ] **Step 5: 运行目标测试和构建**

Run: `npm --prefix apps/web run test:warehouse-locations`

Expected: PASS。

Run: `npm --prefix apps/web run build`

Expected: PASS。

### Task 3: 变更边界与最终验证

**Files:**
- Review all modified files only.

- [ ] **Step 1: 检查差异与格式**

Run: `git diff --check`

Expected: 无输出。

- [ ] **Step 2: GitNexus 影响检查**

Run: `node /Users/sean/DEV/hb-platform/.gitnexus/run.cjs detect_changes --repo hb-platform`

Expected: 只涉及仓库货位页面、locationService、类型、文案和对应测试。

- [ ] **Step 3: 独立规格审查和两轮代码审查**

审查重点：选择边界、路径编码、部分失败行为、权限沿用、刷新后状态、中文注释、未修改后端和无关文件。
