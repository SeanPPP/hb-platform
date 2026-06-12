## 修改 StoreInvoiceView Details 表格

**修改内容：**

1. **移除分页功能**
   - 删除 `pagination` 配置
   - 删除 `page`、`pageSize`、`total` 状态

2. **启用虚拟化（超过50行时）**
   - 使用 `scroll.y` 固定高度以启用虚拟滚动
   - 设置为 `calc(100vh - 380px)`
   - 保留 `scroll.x` 横向滚动

3. **清理相关代码**
   - 移除 `setPage`、`setPageSize`、`setTotal` 调用
   - 简化 `load` 函数