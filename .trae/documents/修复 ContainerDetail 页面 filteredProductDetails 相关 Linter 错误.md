## 问题
- 在 `index.tsx` 中，`createCandidateCount` 与 `updateCandidateCount` 的 `useMemo` 使用了 `filteredProductDetails`，但它们被声明在 `filteredProductDetails` 定义之前，导致“在赋值前使用变量/声明之前已使用”的 Linter 错误。

## 修复方案
- 将 `createCandidateCount`、`updateCandidateCount` 两个 `useMemo` 代码块整体下移，放置到 `filteredProductDetails` 的 `useMemo` 定义之后（建议紧跟其后，约 `index.tsx:449` 位置）。
- 依赖数组保持为 `[filteredProductDetails, selectedRows]`，逻辑不变：
  - 选择行存在 → 基于已选行与 `filteredProductDetails` 计算数量
  - 无选择行 → 基于当前筛选后的全部明细计算数量
- 按钮文案继续引用这两个变量，无需改动其它逻辑。

## 验证
- 迁移后运行诊断，确认“在赋值前使用变量/声明之前已使用”的错误消失。
- 同时保留此前对 `ApiResponse`/`AxiosResponse` 结构访问方式的修正，确保不引入新的类型错误。

确认后我将进行上述位置调整并重新运行诊断。