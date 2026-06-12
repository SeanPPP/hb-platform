我将修复 `src/pages/UserManagement/index.tsx` 中的三个 Lint 错误：

1.  **`useRef` 参数缺失**：`useRef<ActionType>()` 缺少初始值，将修改为 `useRef<ActionType>(undefined)`。
2.  **`SHA256` 未定义**：代码中可能仍有残留的 `SHA256` 调用未被替换为 `CryptoJS.SHA256`，我将修复它。
3.  **`dayjs` 类型不匹配**：`text` 的类型推断包含 `bigint` 等 dayjs 不支持的类型，将修改为 `dayjs(text as string)` 进行显式类型断言。

### 实施计划

1.  **修改 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\UserManagement\index.tsx`**：
    *   **Line 35**: 将 `const actionRef = useRef<ActionType>();` 修改为 `const actionRef = useRef<ActionType>(undefined);`。
    *   **Line 55**: 确认将 `Password: SHA256(values.password).toString(),` 修改为 `Password: CryptoJS.SHA256(values.password).toString(),`（如果是残留问题）。
    *   **Line 203**: 将 `render: (text) => text ? dayjs(text).format(...) : '-',` 修改为 `render: (text) => text ? dayjs(text as string).format(...) : '-',`。

我将立即执行这些修改。
