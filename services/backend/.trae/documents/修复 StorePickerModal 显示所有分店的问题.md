# 修复分店选择组件显示问题

用户反馈 `StorePickerModal` 没有显示所有分店。经分析，主要原因是代码中硬编码了 `isActive: true`，且搜索参数映射存在局限性。

## 修改计划

### 1. 修改 `StorePickerModal.tsx`

*   **优化搜索列**:
    *   新增一个虚拟列 `Keyword` (`dataIndex: 'search'`)，用于统一搜索分店名称和代码。
    *   将原有的 `Store Name` 和 `Store Code` 列设为 `hideInSearch: true`，避免产生误导性的多个搜索框。
*   **修复参数传递**:
    *   在 `request` 方法中，移除 `isActive: true` 的硬编码。
    *   改为从 `params.isActive` 获取状态筛选值，允许用户在界面上筛选“全部”、“激活”或“未激活”。
    *   处理 `isActive` 的类型转换（确保传递给后端的是 boolean 或 undefined）。

### 2. 验证

*   确认修改后，默认进入组件时 `isActive` 为 undefined（显示所有分店），或者根据业务需求设为默认值但允许清除。
*   确认搜索框能正常工作。

## 预期效果

*   用户打开“新建订单”弹窗时，能看到所有分店（除非手动筛选）。
*   可以通过关键字同时搜索分店名和代码。
*   可以通过状态下拉框筛选激活/未激活分店。
