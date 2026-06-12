# 收银管理（PosAdmin）模块迁移计划

## 一、迁移范围

将旧前端 `HBweb-worktrees/Frontend/publish-master/src/pages/PosAdmin/` 目录下的 **9 个页面 + 1 个收银记录页** 迁移到新架构 `react-vite-admin-phase1`。

### 页面清单与复杂度

| # | 页面 | 旧路径 | 行数 | 复杂度 | Modal数 | API数 |
|---|------|--------|------|--------|---------|-------|
| 1 | 供应商管理 | `PosAdmin/SupplierManagement/` | 326 | 简单 | 1 | 3 |
| 2 | 商品信息管理 | `PosAdmin/ProductManagement/` | 2828 | 极其复杂 | 7+ | 19 |
| 3 | 分店商品价格 | `PosAdmin/StoreProductPrice/` | 1991 | 复杂 | 5 | 7 |
| 4 | 自动价格策略 | `PosAdmin/PricingStrategies/` | 649 | 中等 | 1 | 8 |
| 5 | 促销管理 | `PosAdmin/Promotions/` | 771+306 | 中等 | 2 | 9 |
| 6 | 收银用户条码 | `PosAdmin/CashRegisterUsers/` | 996 | 中等 | 2 | 6 |
| 7 | 进货单列表 | `PosAdmin/LocalSupplierInvoices/` | 1224 | 中等偏复杂 | 1 | 7 |
| 8 | 进货单编辑 | `PosAdmin/LocalSupplierInvoices/InvoiceEdit/` | 2649 | 极其复杂 | 5 | 16 |
| 9 | 进货单明细 | `PosAdmin/LocalSupplierInvoiceDetailPage/` | 1681 | 复杂 | 2 | 6 |
| 10 | 收银记录 | `PosmSalesOrders/` | - | 中等 | - | 3 |

**总计：约 12,421+ 行代码，55 个 API 端点**

---

## 二、迁移策略

### 2.1 路由规划

新增一级路由 `/pos-admin`，子路由结构：

```
/pos-admin
├── /pos-admin/suppliers              供应商管理
├── /pos-admin/products               商品信息管理
├── /pos-admin/store-product-price    分店商品价格
├── /pos-admin/pricing-strategies     自动价格策略
├── /pos-admin/promotions             促销管理
├── /pos-admin/cash-register-users    收银用户条码
├── /pos-admin/local-supplier-invoices      进货单列表
├── /pos-admin/local-supplier-invoices/:id  编辑进货单（hidden）
├── /pos-admin/invoice-detail/:id          进货单明细（hidden）
/posm/sales-orders                    收银记录（独立一级）
```

### 2.2 权限映射

| 页面 | accessKey | 说明 |
|------|-----------|------|
| 供应商管理 | `canManageStore` | 同旧前端 |
| 商品信息管理 | `canManageStore` | 仓库/管理员 |
| 分店商品价格 | `canManageStore` | 仓库/管理员 |
| 自动价格策略 | `isAdmin` | 仅管理员 |
| 促销管理 | `isAdmin` | 仅管理员 |
| 收银用户条码 | `canManageStore` | 仓库/管理员 |
| 进货单列表 | `canManageStore` | 仓库/管理员 |
| 收银记录 | `canReadOrder` | 同旧前端 |

### 2.3 依赖情况

- `jsbarcode` — **已安装**，已有 `src/utils/barcode.ts` 工具 + `src/components/BarcodePreview.tsx` 组件
- 所有条码相关页面（商品信息、分店价格、进货单、收银用户）直接复用现有条码工具，无需新增依赖

### 2.4 技术转换对照

| 旧前端（Umi + ag-grid） | 新架构（React + Vite + Ant Design） |
|---|---|
| `ag-grid` 虚拟滚动表格 | Ant Design `Table` + `virtual` 或分页 |
| `GridRequest` POST 分页 | 新架构 `request` + `unwrapPagedResult` |
| `umi-request` / `axios` | 自定义 `request` (基于 fetch) |
| `useModel` (Umi 状态) | Zustand store 或组件内 `useState` |
| `g_tabModel.addTab` | 新架构路由 Tab 自动管理 |
| `intl.formatMessage` 国际化 | 直接中文硬编码（同现有页面风格） |
| `SignalR` 进度推送 (copyStoreData) | SSE `EventSource`（新架构已有 SSE 先例） |
| `Form.List` 动态规则 | Ant Design `Form.List`（无需转换） |

---

## 三、分批迁移计划

### 第一批：基础设施 + 简单页面（打地基）

**目标：** 建立类型、服务层骨架，完成路由注册，迁移最简单的页面验证链路。

#### 步骤 1.1 — 类型定义文件（6 个）
- `src/types/localSupplier.ts` — 本地供应商类型
- `src/types/localSupplierInvoice.ts` — 进货单相关类型（含枚举）
- `src/types/pricingStrategy.ts` — 价格策略类型
- `src/types/promotion.ts` — 促销类型
- `src/types/storeProductPrice.ts` — 分店商品价格类型
- `src/types/cashRegisterUser.ts` — 收银用户类型（从 service 中提取）
- `src/types/posmSalesOrder.ts` — 收银记录类型

#### 步骤 1.2 — 服务层文件（7 个）
- `src/services/localSupplierService.ts`
- `src/services/localSupplierInvoiceService.ts`
- `src/services/pricingStrategyService.ts`
- `src/services/promotionService.ts`
- `src/services/storeProductPriceService.ts`
- `src/services/cashRegisterUserService.ts`
- `src/services/posmSalesOrderService.ts`

#### 步骤 1.3 — 路由注册
- 在 `routes.tsx` 中新增 `/pos-admin` 一级路由和所有子路由
- 在 `iconMap` 中添加新图标映射
- 导入所有新页面组件

#### 步骤 1.4 — 确认依赖

- `jsbarcode` 已安装，`src/utils/barcode.ts` + `BarcodePreview` 组件可直接复用
- 无需安装额外依赖

#### 步骤 1.5 — 迁移供应商管理页（最简单，验证链路）
- `src/pages/PosAdmin/SupplierManagement/index.tsx`
- 验证：类型 → 服务 → 页面 → 路由 → 菜单 → Tab 全链路

#### 步骤 1.6 — 迁移收银记录页（独立路由）
- `src/pages/PosmSalesOrders/index.tsx`
- 注册为 `/posm/sales-orders` 独立一级路由

---

### 第二批：中等复杂度页面（3 个）

#### 步骤 2.1 — 迁移自动价格策略
- `src/pages/PosAdmin/PricingStrategies/index.tsx`
- 重点：动态规则编辑 Form.List、策略测试面板

#### 步骤 2.2 — 迁移促销管理 + ProductPicker
- `src/pages/PosAdmin/Promotions/index.tsx`
- `src/pages/PosAdmin/Promotions/ProductPicker.tsx`
- 重点：商品选择器 Modal、动态商品列表

#### 步骤 2.3 — 迁移收银用户条码管理
- `src/pages/PosAdmin/CashRegisterUsers/index.tsx`
- 重点：条码生成(react-barcode)、条码打印、批量删除

---

### 第三批：进货单流程（3 个页面，业务核心链路）

#### 步骤 3.1 — 迁移进货单列表
- `src/pages/PosAdmin/LocalSupplierInvoices/index.tsx`
- 重点：颜色编码 Tag、随货单号重复检测、推送到 HQ、Tab 导航转路由 Tab

#### 步骤 3.2 — 迁移进货单明细（只读详情）
- `src/pages/PosAdmin/LocalSupplierInvoiceDetailPage/index.tsx`
- 重点：条码生成、价格变动高亮、分店权限过滤

#### 步骤 3.3 — 迁移进货单编辑（最复杂）
- `src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/index.tsx`
- 重点：粘贴 Excel、商品检测（条码/商品状态）、批量操作、虚拟滚动
- 此页面为独立 Tab（hidden 路由），通过列表页操作打开

---

### 第四批：核心复杂页面（2 个）

#### 步骤 4.1 — 迁移商品信息管理（最复杂，2828 行）
- `src/pages/PosAdmin/ProductManagement/index.tsx`
- 建议**拆分子组件**：
  - `EditProductModal.tsx` — 编辑商品弹窗
  - `BatchEditModal.tsx` — 批量编辑弹窗
  - `SyncToStoresModal.tsx` — 同步到分店弹窗
  - `SyncFromHqModal.tsx` — 从 HQ 同步弹窗
  - `IntegrityCheckModal.tsx` — 数据一致性检测弹窗
  - `CategoryManager.tsx` — 分类树管理
  - `SetCodeEditor.tsx` — 多码/套装编辑
- 重点：条码生成、多码/套装子表格、自动定价计算、分类树、数据一致性检测

#### 步骤 4.2 — 迁移分店商品价格（1991 行，含 SSE）
- `src/pages/PosAdmin/StoreProductPrice/index.tsx`
- 建议**拆分子组件**：
  - `BatchUpdateModal.tsx` — 批量更新弹窗
  - `SyncToStoresModal.tsx` — 同步到其他分店弹窗
  - `CopyStoreDataModal.tsx` — 复制分店数据弹窗（含 SSE 进度）
  - `SyncFromHqModal.tsx` — 从 HQ 更新弹窗
- 重点：复制分店数据 SSE 实时进度、图片懒加载、自定义单元格组件

---

## 四、关键注意事项

### 4.1 进货单编辑页的交互模式
- 旧前端使用 `g_tabModel.addTab` 打开编辑页
- 新架构应使用 **独立 Tab**（hidden 路由），路由为 `/pos-admin/local-supplier-invoices/:id`
- 进货单明细（只读）同样使用独立 Tab，路由为 `/pos-admin/invoice-detail/:id`

### 4.2 虚拟滚动表格
- 旧前端部分页面使用 `ag-grid` 虚拟滚动
- 新架构使用 Ant Design Table，大数据量场景考虑开启 `virtual` 或使用分页
- 按项目实际数据量决定是否需要虚拟化

### 4.3 SSE 长连接
- 分店商品价格的"复制分店数据"功能使用 SSE 推送进度
- 需在新架构中实现 `EventSource` 订阅，同 `request.ts` 的 base URL 配置

### 4.4 条码生成
- 商品信息、分店价格、进货单编辑页使用 `JsBarcode` 生成条码图片
- 收银用户条码页使用 `react-barcode` 组件
- 条码格式遵循项目已有规则：EAN13 校验通过用 EAN13，否则回退 CODE128

### 4.5 共享工具复用
- 分店列表获取：多个页面需要 `getActiveStores`，已存在 `storeService`
- 供应商列表获取：通过新建 `localSupplierService.getActiveLocalSuppliers`
- 条码工具：已有 `src/utils/barcode.ts`，需检查是否需要扩展

---

## 五、文件新增清单

### 类型文件（7 个）
```
src/types/localSupplier.ts
src/types/localSupplierInvoice.ts
src/types/pricingStrategy.ts
src/types/promotion.ts
src/types/storeProductPrice.ts
src/types/cashRegisterUser.ts
src/types/posmSalesOrder.ts
```

### 服务文件（7 个）
```
src/services/localSupplierService.ts
src/services/localSupplierInvoiceService.ts
src/services/pricingStrategyService.ts
src/services/promotionService.ts
src/services/storeProductPriceService.ts
src/services/cashRegisterUserService.ts
src/services/posmSalesOrderService.ts
```

### 页面文件（10+ 个）
```
src/pages/PosAdmin/SupplierManagement/index.tsx
src/pages/PosAdmin/ProductManagement/index.tsx
src/pages/PosAdmin/ProductManagement/EditProductModal.tsx
src/pages/PosAdmin/ProductManagement/BatchEditModal.tsx
src/pages/PosAdmin/ProductManagement/SyncToStoresModal.tsx
src/pages/PosAdmin/ProductManagement/SyncFromHqModal.tsx
src/pages/PosAdmin/ProductManagement/IntegrityCheckModal.tsx
src/pages/PosAdmin/ProductManagement/CategoryManager.tsx
src/pages/PosAdmin/StoreProductPrice/index.tsx
src/pages/PosAdmin/StoreProductPrice/BatchUpdateModal.tsx
src/pages/PosAdmin/StoreProductPrice/SyncToStoresModal.tsx
src/pages/PosAdmin/StoreProductPrice/CopyStoreDataModal.tsx
src/pages/PosAdmin/StoreProductPrice/SyncFromHqModal.tsx
src/pages/PosAdmin/PricingStrategies/index.tsx
src/pages/PosAdmin/Promotions/index.tsx
src/pages/PosAdmin/Promotions/ProductPicker.tsx
src/pages/PosAdmin/CashRegisterUsers/index.tsx
src/pages/PosAdmin/LocalSupplierInvoices/index.tsx
src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/index.tsx
src/pages/PosAdmin/LocalSupplierInvoiceDetailPage/index.tsx
src/pages/PosmSalesOrders/index.tsx
```

### 修改文件（2 个）
```
src/router/routes.tsx          — 新增路由配置
src/utils/access.ts            — 确认权限 key 是否需要新增（当前 key 已覆盖）
```

---

## 六、执行顺序总览

```
第一批（基础设施 + 简单页面）
  ├── 1.1 创建 7 个类型文件
  ├── 1.2 创建 7 个服务文件
  ├── 1.3 路由注册 + 图标映射
  ├── 1.4 安装 jsbarcode + react-barcode
  ├── 1.5 迁移供应商管理（验证链路）
  └── 1.6 迁移收银记录

第二批（中等复杂度）
  ├── 2.1 自动价格策略
  ├── 2.2 促销管理 + ProductPicker
  └── 2.3 收银用户条码管理

第三批（进货单流程）
  ├── 3.1 进货单列表
  ├── 3.2 进货单明细（只读）
  └── 3.3 进货单编辑（最复杂）

第四批（核心复杂页面）
  ├── 4.1 商品信息管理（拆分子组件）
  └── 4.2 分店商品价格（含 SSE）
```
