## 修复计划

修改 `ReactUmi/my-app/src/pages/PosAdmin/CashRegisterUsers/index.tsx` 文件中的 `load()` 函数：

1. **修改 globalSearch 参数**：将 `keyword || undefined` 改为 `keyword || ''`，确保始终发送空字符串而不是 undefined
2. **修改 filterModel 参数**：将 `Object.keys(filterModel).length ? filterModel : undefined` 改为 `Object.keys(filterModel).length ? filterModel : {}`，确保始终发送空对象而不是 undefined

这样可以确保前端发送的请求格式始终正确，避免 JSON 序列化问题导致后端返回 400 错误。