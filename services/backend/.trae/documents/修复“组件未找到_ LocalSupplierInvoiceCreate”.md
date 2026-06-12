## 原因
- KeepAliveTabLayout 的 `componentMap` 未注册 `LocalSupplierInvoiceCreate`，导致通过 Tab 打开创建页时无法解析组件名。

## 修改
- 在 `ReactUmi/my-app/src/layouts/KeepAliveTabLayout.tsx`：
  - 引入 `PlusOutlined` 图标（可选，用于新建标签图标显示）。
  - 在 `componentMap` 中新增：
    - `LocalSupplierInvoiceCreate: React.lazy(() => import('@/pages/PosAdmin/LocalSupplierInvoiceCreate') as any)`
  - 在 `iconMap` 中新增（可选）：
    - `PlusOutlined: <PlusOutlined />`

## 验证
- 列表页点击“新增进货单”后，创建页成功在标签中打开；不再显示“组件未找到”。

## 影响范围
- 仅前端布局文件的组件映射注册，无其他模块影响。