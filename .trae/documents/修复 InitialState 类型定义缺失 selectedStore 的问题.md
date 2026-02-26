# 修复前端类型定义错误

## 问题分析
用户报告了前端编译错误：
`[Error] 类型“InitialState”上不存在属性“selectedStore”。`
这是因为在 `src/app.ts` 中定义的 `InitialState` 接口缺少 `selectedStore` 属性，但在 `StoreLayout.tsx` 和 `StoreHome/index.tsx` 中使用了该属性来存储当前选中的分店信息。

## 实施步骤

### 1. 修改 `src/app.ts`
*   **导入类型**: 引入 `UserStoreDto` (来自 `@/services/storeService`)。
*   **更新接口**: 在 `InitialState` 接口中添加 `selectedStore?: UserStoreDto | null;`。

## 验证
*   类型定义更新后，`StoreHome/index.tsx` 中的 `initialState.selectedStore` 访问将不再报错。
*   这将解决当前的构建阻碍。

## 关于后端警告
用户提供的诊断信息中包含大量 C# 后端警告（如可能的空引用、不必要的代码等）。鉴于当前主要任务是修复前端功能，我将优先解决导致前端构建失败的错误。后端警告属于代码质量优化，建议在前端修复完成后单独处理。
