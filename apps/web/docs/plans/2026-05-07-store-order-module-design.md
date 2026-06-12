# Store order module implementation plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to
> implement this plan task-by-task.

**Goal:** Build the first executable migration slice for the store order
module in the new React + Vite admin app, starting with the order list page and
preparing the detail tab route.

**Architecture:** Keep the new framework's route-driven menu, tabs, and
keep-alive model. Migrate the store order list first, then add the detail page
as an independent tab route, and isolate API response quirks inside a dedicated
service adapter instead of scattering compatibility code in page components.

**Tech Stack:** React, Vite, TypeScript, Ant Design, Zustand,
`react-router-dom`, project `request.ts`, project tab store

---

## Scope

This plan covers the first migration slice for the old store order module from
`publish-master` into `react-vite-admin-phase1`.

The first delivery target is:

- `分店订货列表`: `/warehouse/store-orders`
- `分店订货明细`: `/warehouse/store-order/detail/:id`

This plan uses the following rules that are already fixed:

- `仓库商品管理` and `仓库标签管理` use modal-based editing.
- `分店订货明细` must use an independent tab, not a modal.
- `商品导入工作台` is not part of the warehouse menu and is out of scope.

## Old system findings

The old module is not a simple table page. It is a combined workflow that
contains:

- A list page with filters, row actions, and toolbar actions.
- A detail page with rich editing, batch operations, and print entry points.
- A tab-first navigation model with route fallback.
- Several store-order-specific APIs with inconsistent response envelopes.

The highest-risk findings are:

- `createOrder` returns a `string` order GUID in the backend, not an object.
- Some list and detail APIs return wrapped `data`, while others return direct
  payloads.
- The old frontend defines `batch-lookup` and `paste-replace`, but backend
  implementation was not confirmed in the current backend code.
- Quantity and amount field semantics are not fully aligned across UI comments,
  TypeScript types, and backend calculations.

## Delivery strategy

Use a two-pass delivery model.

Pass 1 builds a stable first version:

- List page route and menu
- List filters and remote pagination
- New order
- Copy order
- Batch status
- Delete order
- Click order number to open detail tab
- Detail route shell and tab opening protocol

Pass 2 fills complex behaviors:

- Detail page full editing surface
- Excel paste workflow
- Picking list and invoice print pages
- Status transition cleanup
- Advanced edge-case workflows

## File map

**Create:**

- `src/types/storeOrder.ts`
- `src/services/storeOrderService.ts`
- `src/pages/Warehouse/StoreOrders/index.tsx`
- `src/pages/Warehouse/StoreOrders/Detail.tsx`

**Modify:**

- `src/router/routes.tsx`
- `src/types/auth.ts` if warehouse permissions need additional access keys
- `src/store/tabs.ts` only if a route-driven tab helper is missing

**Reference only:**

- `src/pages/Warehouse/Products/index.tsx`
- `src/services/domesticProductService.ts`
- `src/utils/request.ts`
- Old list page:
  `HBweb-worktrees/Frontend/publish-master/src/pages/WareHouse/StoreOrder/OrderList/index.tsx`
- Old detail page:
  `HBweb-worktrees/Frontend/publish-master/src/pages/WareHouse/StoreOrder/OrderDetails/index.tsx`
- Backend controller:
  `HBweb-worktrees/Backend/publish-master/BlazorApp.Api/Controllers/React/ReactStoreOrderController.cs`

### Task 1: define stable TypeScript contracts

**Files:**

- Create: `src/types/storeOrder.ts`

**Step 1: Create first-pass list contracts**

Define:

- `StoreOrderFlowStatus`
- `StoreOrderListItem`
- `StoreOrderListQuery`
- `StoreOrderListResult`
- `CreateStoreOrderPayload`
- `CopyStoreOrderPayload`
- `BatchUpdateStoreOrderStatusPayload`
- `UsedBranchOption`

Keep naming aligned with the new project style, not the old Umi naming style.

**Step 2: Normalize known backend quirks in comments**

Document:

- `createOrder` returns `orderGuid: string`
- list pagination shape is `items + total + pageNumber + pageSize`
- `storeCodes` is the preferred multi-store filter input

**Step 3: Verify types compile**

Run:

```bash
npm run build
```

Expected:

- Build passes with no type errors from the new type file.

**Step 4: Commit**

```bash
git add src/types/storeOrder.ts
git commit -m "feat: add store order types"
```

### Task 2: build the store-order service adapter

**Files:**

- Create: `src/services/storeOrderService.ts`
- Reference: `src/utils/request.ts`

**Step 1: Add response adapter helpers**

Create small helpers that normalize:

- wrapped payloads: `{ success, data, message }`
- direct payloads
- paged payloads

Do not leak old compatibility logic into pages.

**Step 2: Implement first-pass APIs**

Add:

- `getStoreOrderList`
- `getUsedBranches`
- `createStoreOrder`
- `copyStoreOrder`
- `updateStoreOrderStatus`
- `batchUpdateStoreOrderStatus`
- `deleteStoreOrder`

Target backend base path:

```txt
/api/react/v1/store-order
```

**Step 3: Match real backend return values**

Apply these rules:

- `createStoreOrder` returns `string`
- `copyStoreOrder` returns `{ orderGUID, orderNo }`
- `batchUpdateStoreOrderStatus` may return count-like numeric data

**Step 4: Verify service compilation**

Run:

```bash
npm run build
```

Expected:

- Build passes with no service type errors.

**Step 5: Commit**

```bash
git add src/services/storeOrderService.ts src/types/storeOrder.ts
git commit -m "feat: add store order service adapter"
```

### Task 3: add warehouse routes for list and detail

**Files:**

- Modify: `src/router/routes.tsx`

**Step 1: Add list route**

Add:

```ts
{
  path: '/warehouse/store-orders',
  meta: {
    title: '分店订货列表',
    icon: 'ShopOutlined',
    keepAlive: true,
    accessKey: 'canManageWarehouse',
  },
  element: <StoreOrdersPage />,
}
```

**Step 2: Add detail route**

Add hidden detail route:

```ts
{
  path: '/warehouse/store-order/detail/:id',
  meta: {
    title: '订货明细',
    hidden: true,
    keepAlive: true,
    accessKey: 'canManageWarehouse',
    activeMenu: '/warehouse/store-orders',
    dynamicTitle: (params) => `订货明细 - ${params.id || ''}`,
  },
  element: <StoreOrderDetailPage />,
}
```

**Step 3: Verify menu and tab behavior**

Confirm route metadata supports:

- menu highlight from detail back to list
- independent tabs by pathname
- keep-alive behavior for detail pages

**Step 4: Verify compilation**

Run:

```bash
npm run build
```

Expected:

- Route tree compiles and menu generation still works.

**Step 5: Commit**

```bash
git add src/router/routes.tsx
git commit -m "feat: add store order routes"
```

### Task 4: implement the first-pass store-order list page

**Files:**

- Create: `src/pages/Warehouse/StoreOrders/index.tsx`
- Reference: `src/pages/Warehouse/Products/index.tsx`

**Step 1: Build page state**

Add local state for:

- `keyword`
- `dateRange`
- `statusList`
- `selectedStoreCodes`
- `page`
- `pageSize`
- `total`
- `selectedRowKeys`
- `sortField`
- `sortOrder`

Keep it page-local. Do not add a new global store for list filters.

**Step 2: Build the toolbar**

Add buttons for:

- `新建订单`
- `同步订单`
- `复制订单`
- `批量状态`

For Pass 1:

- keep `同步订单` visible only if backend contract is confirmed
- if not confirmed, render disabled or omit it from first pass

**Step 3: Build first-pass filters**

Add:

- keyword search
- date range
- status multi-select
- store multi-select from `getUsedBranches`

Preserve the old page's key interaction:

- clicking store tag in the table back-fills the store filter

**Step 4: Build the table**

Include at minimum:

- order number
- store
- order date
- status
- order quantity
- order amount
- send quantity
- send amount
- remarks
- created at
- updated at
- actions

Enable:

- row selection
- remote pagination
- remote sorting
- order number click to detail tab

**Step 5: Implement row and toolbar actions**

Add:

- new order -> choose store -> create -> navigate to detail tab
- copy order -> choose target store -> create copied order -> navigate to
  detail tab
- single status update
- batch status update
- delete

Use modal dialogs for new-order store selection and copy-order configuration.
Do not use independent tabs for those dialogs.

**Step 6: Verify first-pass behavior manually**

Run:

```bash
npm run dev
```

Expected:

- list page opens from warehouse menu
- filters trigger remote reload
- order number opens detail route
- new order opens new detail tab
- copy order opens new detail tab

**Step 7: Verify production build**

Run:

```bash
npm run build
```

Expected:

- Build passes.

**Step 8: Commit**

```bash
git add src/pages/Warehouse/StoreOrders/index.tsx src/services/storeOrderService.ts src/types/storeOrder.ts
git commit -m "feat: add store order list page"
```

### Task 5: add the detail route shell before full detail migration

**Files:**

- Create: `src/pages/Warehouse/StoreOrders/Detail.tsx`

**Step 1: Create a route-valid detail shell**

Build a minimal page that:

- reads `id` from route params
- sets dynamic tab title using current order number or ID placeholder
- renders loading, empty, and placeholder states

This shell prevents broken navigation while the full detail page is still being
implemented.

**Step 2: Add first-pass detail fetch**

If backend contract is stable enough, add:

- `getStoreOrderDetail`

If not stable, keep a placeholder shell and defer data rendering to Pass 2.

**Step 3: Verify tab routing**

Manually confirm:

- `/warehouse/store-order/detail/:id` opens as an independent tab
- multiple detail tabs do not overwrite each other
- browser refresh preserves direct entry

**Step 4: Verify build**

Run:

```bash
npm run build
```

Expected:

- Build passes and route resolution works.

**Step 5: Commit**

```bash
git add src/pages/Warehouse/StoreOrders/Detail.tsx src/router/routes.tsx
git commit -m "feat: add store order detail route shell"
```

### Task 6: define pass-2 detail migration scope

**Files:**

- Modify later: `src/pages/Warehouse/StoreOrders/Detail.tsx`
- Create later: subcomponents under `src/pages/Warehouse/StoreOrders/components/`

**Step 1: Split detail features into subareas**

Pass 2 should separately implement:

- order header editor
- line table
- row editing
- batch editing
- product picker
- container product picker
- paste modal
- print entry buttons

**Step 2: Freeze deferred items**

Explicitly defer until after list delivery:

- Excel paste
- batch lookup
- paste replace
- picking list print
- invoice print
- complex status machine cleanup

**Step 3: Record backend blockers**

Before Pass 2 starts, confirm:

- `getOrderDetail`
- `completeOrder`
- `startPicking`
- `batchLookupStoreOrderProducts`
- `pasteReplaceOrderLines`
- line update quantity semantics

**Step 4: Commit**

```bash
git add docs/plans/2026-05-07-store-order-module-design.md
git commit -m "docs: record store order migration plan"
```

## Manual verification checklist

Use this checklist after Pass 1:

1. Open **仓库管理 > 分店订货列表**.
2. Search by keyword and confirm server-side reload.
3. Filter by status and date range.
4. Filter by store code from the toolbar.
5. Click an order number and confirm a new detail tab opens.
6. Open two different order details and confirm they keep separate tabs.
7. Create a new order and confirm it opens in a new detail tab.
8. Copy an order and confirm it opens in a new detail tab.
9. Change single and batch status and confirm list refresh.
10. Refresh the browser on a detail route and confirm direct entry works.

## Known risks

- Backend `create` contract differs from old frontend assumptions.
- Some old detail APIs appear to be declared in the frontend but were not
  confirmed in the current backend controller.
- Quantity and amount naming requires explicit business confirmation during
  detail migration.
- The old backend controller currently exposes risky authorization posture and
  must not be used as proof that the contract is final.

## Next steps

After this plan is approved, implement only Pass 1 first:

1. Add types.
2. Add service adapter.
3. Add routes.
4. Add list page.
5. Add detail shell.

Do not start the full detail page until Pass 1 is stable in the browser.
