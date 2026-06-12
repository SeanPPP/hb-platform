修复 TypeScript 编译错误：

1. **修复 ResetPasswordModal.tsx 错误**:

   * 问题：`res.success` 报错，因为 `res` 是 `AxiosResponse` 对象，实际数据在 `res.data` 中。

   * 修复：将 `res.success` 修改为 `res.data.success`，同时处理 `res.data.message`。

2. **修复 UserManagement/index.tsx 错误**:

   * 问题：`dayjs(text)` 报错，因为 `text` 在 ProColumns 中被推断为 `ReactNode`，而 dayjs 不接受 ReactNode。

   * 修复：显式指定 `render` 函数的参数类型为 `any` 或更具体的类型（如 `string`），或者在传递给 `dayjs` 前进行类型断言 `dayjs(text as string)`。

实施步骤：

1. 修改 `src/pages/UserManagement/components/ResetPasswordModal.tsx`。
2. 修改 `src/pages/UserManagement/index.tsx` 中的两处 `dayjs` 调用。

