## 原因分析
- 当前“供应商管理”路由定义在顶层 `/pos-admin`，未使用 `KeepAliveTabLayout`，点击后页面在默认布局中打开，不会进入Tab。

## 调整方案
- 为 `posAdmin` 路由组增加 `component: '@/layouts/KeepAliveTabLayout'`，其子路由 `supplierManagement` 作为该布局的内容，从而在Tab中显示。
- 保持现有菜单国际化与访问控制不变（`access: 'isAdmin'`）。

## 修改点
- 编辑 `ReactUmi/my-app/.umirc.ts`：在 `posAdmin` 路由组上设置 `component: '@/layouts/KeepAliveTabLayout'`，其子路由保持 `component: './PosAdmin/SupplierManagement'`。

## 验证
- 重载开发服务后，点击导航“供应商管理”，应在顶部Tab区域中新开一个标签显示该页面。
- 直接访问 `http://localhost:8000/pos-admin/supplier-management` 也应显示在Tab中。