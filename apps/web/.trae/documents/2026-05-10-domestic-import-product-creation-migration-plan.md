# 国内商品导入 & 货号条码创建 — 迁移计划

## 一、迁移范围

从旧前端 `HBweb-worktrees/Frontend/publish-master` 迁移 2 个独立页面到新架构 `react-vite-admin-phase1`：

| 页面 | 旧路径 | 新路由 | 归属菜单 |
|---|---|---|---|
| **货号条码批量创建** | `src/pages/DomesticProductCreations/` | `/domestic-purchase/product-creation` | 国内采购 |
| **国内商品导入** | `src/pages/DomesticProductImport/` | `/domestic-purchase/product-import` | 国内采购 |

## 二、核心原则

**不引入任何新 npm 依赖**，全部使用新架构已有组件：

| 旧前端依赖 | 新架构替代方案 |
|---|---|
| `react-data-grid` (DataGrid) | antd `Table` + 可编辑单元格（Input/InputNumber 内嵌 render） |
| `@ant-design/pro-components` (ProTable) | antd `Table` + 手动分页 + 手动 request |
| `@umijs/max` (`useIntl`) | 直接使用中文字符串 |
| `react-barcode` | 已有 `BarcodePreview` 组件 / `jsbarcode` |
| `exceljs` | 已安装，直接复用 |
| 旧 `request` | 新架构 `src/utils/request.ts` |

## 三、实施步骤

### 步骤 1：创建类型定义

#### 1.1 新建 `src/types/domesticProductCreation.ts`

- `ProductCreationType` 枚举（NORMAL=0, SET=1, SET_SUB_ITEM=2）
- `BatchInfo`、`BatchProductItem`、`BatchDetail`、`CreateBatchRequest`、`BatchListParams`、`UpdatePriceItem`
- `PrefixCodeListParams`、`PrefixCodeResponse`

#### 1.2 新建 `src/types/domesticProductImport.ts`

- `ProductImportItem`、`PageState`、`Statistics`、`DuplicateGroup`、`BatchDetectResponse`
- 匹配状态类型：`'new' | 'updated' | 'unchanged' | 'duplicate' | 'dbDuplicate' | 'error'`

### 步骤 2：创建服务层

#### 2.1 新建 `src/services/domesticProductCreationService.ts`

从旧 `services/domesticProductCreation.ts` 迁移，替换 `request` 导入：
- API 端点：`/api/v1/domestic-product-creation`
- 函数：`createBatch`、`getBatchList`、`getBatchDetail`、`updatePrivateLabelPrice`、`getActivePrefixes`
- 前缀码 CRUD：`getPrefixCodeList`、`createPrefixCode`、`updatePrefixCode`、`deletePrefixCode`、`togglePrefixCodeStatus`

#### 2.2 新建 `src/services/domesticProductImportService.ts`

从旧 `services/domesticProduct.ts` 提取导入相关 API：
- API 端点：`/api/react/v1/domestic-products`
- 函数：`batchDetectProducts`、`batchImportConfirm`、`batchUpdateDomesticProducts`、`fixProductImage`、`syncToHBSales`

### 步骤 3：迁移货号条码批量创建页面（先做，较简单）

#### 3.1 主页面 — `src/pages/DomesticPurchase/ProductCreation/index.tsx`

**改造要点：**
- `ProTable` → antd `Table` + 手动 `pagination` + 手动 `loadData`（参考新架构已有 `DomesticProductsPage` 模式）
- 去除 `useIntl`，直接中文字符串
- 使用 `PageContainer` 包裹
- 保留功能：批次列表、创建批次按钮、查看明细、分页

#### 3.2 批次创建模态框 — `src/pages/DomesticPurchase/ProductCreation/BatchCreateModal.tsx`

- 去除 `useIntl`
- 保留 3 步向导（基本信息 → 商品明细 → 预览确认）
- 内联 `PrefixCodeManageModal` 或单独文件

#### 3.3 批次明细模态框 — `src/pages/DomesticPurchase/ProductCreation/BatchDetailModal.tsx`

- 去除 `useIntl`、`react-barcode`
- 条码渲染改用 `BarcodePreview` 组件
- ExcelJS 导出保留（已有依赖）
- 保留功能：Tab 切换、贴牌价格编辑、条码展示

#### 3.4 前缀码管理 — `src/pages/DomesticPurchase/ProductCreation/PrefixCodeManageModal.tsx`

- 去除 `useIntl`
- 使用 antd Table + Modal

### 步骤 4：迁移国内商品导入页面（较复杂，后做）

#### 4.1 核心改造：react-data-grid → antd Table

旧页面使用 `react-data-grid` 的 `DataGrid` 实现可编辑表格。改造为 antd `Table` + 可编辑单元格：

**可编辑单元格模式：**
```tsx
// 每个可编辑列的 render 使用受控 Input/InputNumber
render: (value, record) => {
  if (editingKey === record.id) {
    return <Input value={value} onChange={...} />
  }
  return value || '--'
}
```

**Excel 粘贴处理：**
- 保留 `document.addEventListener('paste', handlePaste)` 全局粘贴监听
- 解析粘贴数据后写入 state 数组，触发 Table 重渲染
- 粘贴逻辑从依赖 `react-data-grid` 的 DOM 属性（`aria-rowindex`、`aria-colindex`）改为依赖 antd Table 的 DOM 结构

**批量选择：**
- antd Table 原生 `rowSelection` 替代 `react-data-grid` 的手动 checkbox 列

**行样式：**
- antd Table `rowClassName` 替代 `react-data-grid` 的 `rowClass`

#### 4.2 主页面 — `src/pages/DomesticPurchase/ProductImport/index.tsx`

- 使用 `PageContainer` 包裹
- antd `Table` 替代 `DataGrid`
- 保留功能：供应商选择、Excel 粘贴、检测匹配、批量新建/更新、发送货柜、同步 HBSales、冲突处理

#### 4.3 工具函数 — `src/pages/DomesticPurchase/ProductImport/utils.ts`

- 直接迁移，无框架依赖变更
- 保留：`generateImageUrl`、`createEmptyProduct`、`detectDuplicates`、`mergeDuplicateProducts`、`calculateStatistics`、`updateCalculatedFields`、`validateProduct`

#### 4.4 类型定义 — `src/pages/DomesticPurchase/ProductImport/types.ts`

- 直接迁移

#### 4.5 子组件

- `ConflictResolutionDialog.tsx` — 去除 `useIntl`
- `DuplicateDialog.tsx` — 去除 `useIntl`
- `ImageCell.tsx` — 去除 `useIntl`
- `styles.css` — 保留自定义样式（适配 antd Table 结构）

### 步骤 5：注册路由

在 `src/router/routes.tsx` 的 `domestic-purchase` 子路由中新增：

```tsx
import ProductCreationPage from '../pages/DomesticPurchase/ProductCreation'
import ProductImportPage from '../pages/DomesticPurchase/ProductImport'

// domestic-purchase children 中新增：
{
  path: '/domestic-purchase/product-creation',
  meta: {
    title: '货号条码创建',
    icon: 'TagsOutlined',
    keepAlive: true,
    accessKey: 'canManageWarehouse',
  },
  element: <ProductCreationPage />,
},
{
  path: '/domestic-purchase/product-import',
  meta: {
    title: '商品导入',
    icon: 'AppstoreOutlined',
    keepAlive: true,
    accessKey: 'canManageWarehouse',
  },
  element: <ProductImportPage />,
},
```

### 步骤 6：编译验证

- `npm run build` 确认 TypeScript 编译通过
- 在开发环境验证两个页面功能正常

## 四、文件清单

### 新建文件（约 15 个）

```
src/types/domesticProductCreation.ts
src/types/domesticProductImport.ts
src/services/domesticProductCreationService.ts
src/services/domesticProductImportService.ts
src/pages/DomesticPurchase/ProductCreation/index.tsx
src/pages/DomesticPurchase/ProductCreation/BatchCreateModal.tsx
src/pages/DomesticPurchase/ProductCreation/BatchDetailModal.tsx
src/pages/DomesticPurchase/ProductCreation/PrefixCodeManageModal.tsx
src/pages/DomesticPurchase/ProductImport/index.tsx
src/pages/DomesticPurchase/ProductImport/utils.ts
src/pages/DomesticPurchase/ProductImport/types.ts
src/pages/DomesticPurchase/ProductImport/ConflictResolutionDialog.tsx
src/pages/DomesticPurchase/ProductImport/DuplicateDialog.tsx
src/pages/DomesticPurchase/ProductImport/ImageCell.tsx
src/pages/DomesticPurchase/ProductImport/styles.css
```

### 修改文件（1 个）

```
src/router/routes.tsx  — 新增 2 条路由 + 2 个 import
```

### 无新增 npm 依赖

全部使用新架构已有组件和库。

## 五、关键技术改造说明

### 国内商品导入页：react-data-grid → antd Table

这是本次迁移最复杂的部分。旧页面有约 3000 行代码，其中 react-data-grid 的使用包括：

1. **可编辑表格**：每列定义 `renderEditCell` → 改为 antd Table 的 `render` 中内嵌 Input/InputNumber
2. **行选择**：手动 checkbox 列 → antd `rowSelection`
3. **行样式**：`rowClass` → antd `rowClassName`
4. **Excel 粘贴**：依赖 `react-data-grid` DOM 属性定位 → 改为依赖 antd Table DOM 结构
5. **拖拽填充**：`onFill` → 简化为复制粘贴
6. **冻结列**：`frozen: true` → antd `fixed: 'left'/'right'`

### 货号条码创建页：ProTable → antd Table

较简单，参考新架构已有 `DomesticProductsPage` 模式：
- `request` prop → 手动 `loadData` + `dataSource`
- `toolBarRender` → `PageContainer.extra`
- `search` → 手动筛选栏
