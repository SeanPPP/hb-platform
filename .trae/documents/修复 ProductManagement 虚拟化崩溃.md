问题来自虚拟行 VRow 读取到 undefined/null 记录：后端返回的数据结构可能不是数组，导致 dataSource 中出现非数组或空项。为此：

- 规范化接口响应：在 `loadProducts` 中将 `res.data?.items ?? res.data ?? []` 提取为数组；同步 `total` 为 `res.total ?? res.data?.total ?? 0`。
- 为表格提供健壮的数据源：`dataSource={Array.isArray(products) ? products : []}`，避免传入非数组。
- 保持现有 `rowKey='productCode'` 与虚拟化配置不变。

这将消除 VRow 的空记录错误，并维持虚拟化带来的性能提升。