我将修复 `ReferenceError: SHA256 is not defined` 错误。

**问题原因**：
根据您提供的日志，`SHA256` 未定义。这通常是因为 `import { SHA256 } from 'crypto-js'` 这种按名导入方式在当前前端构建环境中不兼容。`crypto-js` 通常导出为一个默认对象。

**解决方案**：
将导入方式改为默认导入 `import CryptoJS from 'crypto-js'`，并使用 `CryptoJS.SHA256(...)` 调用。

### 实施计划

1.  **修改 `src/pages/Login/index.tsx`**：
    *   将 `import { SHA256 } from 'crypto-js';` 修改为 `import CryptoJS from 'crypto-js';`。
    *   将 `SHA256(values.password)` 修改为 `CryptoJS.SHA256(values.password)`。

2.  **修改 `src/pages/UserManagement/index.tsx`**：
    *   将 `import { SHA256 } from 'crypto-js';` 修改为 `import CryptoJS from 'crypto-js';`。
    *   将 `SHA256(values.password)` 修改为 `CryptoJS.SHA256(values.password)`。

3.  **验证**：
    *   代码修改完成后，请您在浏览器中重试登录。
    *   如果仍然失败，我将尝试使用 MCP 浏览器工具进一步诊断。

我将立即执行这些修改。
