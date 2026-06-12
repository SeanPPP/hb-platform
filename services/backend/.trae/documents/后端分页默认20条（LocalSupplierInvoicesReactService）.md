## 目标
- 在后端服务 LocalSupplierInvoicesReactService 的 GetGridDataAsync 中实现统一的分页默认值：当请求未提供有效 PageSize 时，默认使用 20 条。

## 修改点
- 在方法内部统一处理分页参数：
  - 计算 `pageSize = request.PageSize > 0 ? request.PageSize : 20`
  - 计算 `startRow = Math.Max(0, request.StartRow)`
  - 查询分页：`Skip(startRow).Take(pageSize)`
- 保持现有返回结构不变：`Items` 与 `Total`。
- 不调整前端分页设置（前端自定义仍可覆盖）；仅后端默认策略统一为 20。

## 验证
- 未传或传入非正的 PageSize → 返回 20 条数据。
- 传入有效 PageSize → 按传入值执行分页。