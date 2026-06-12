## 修复计划

1. **修正导入语句（第 2-3 行）**：
   - 从 `@ant-design/pro-components` 移除 `Alert`, `Tag`, `Space`, `Button`
   - 将这些组件添加到 `antd` 导入中
   - 将 `ClearOutlined` 从 `@ant-design/icons` 导入

2. **修复类型不匹配（第 282 行）**：
   - 将 `selectedBranchCode` 转换为 `undefined` 而不是 `null`，使用 `?? undefined` 运算符

这些修改将解决所有 8 个 TypeScript 错误，使代码符合 Ant Design 的正确导入规范。