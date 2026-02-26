我将按照您的要求，使用 `antd-virtualized-table` 为 `StoreRetailPrices` 页面启用表格虚拟化。

### 实施计划
1.  **安装依赖**: 使用 npm 安装 `antd-virtualized-table`。
2.  **修改 `StoreRetailPrices/index.tsx`**:
    *   从 `antd-virtualized-table` 引入 `VList`。
    *   使用 `useMemo` 创建虚拟化组件配置。
    *   更新 `<Table>` 组件，配置 `components` 属性，并确保 `scroll.y` 设置一致。

### 关于报错说明
*   **AxiosError**: 这通常表示网络请求失败（如 API 报错）。现有代码已包含 `message.error` 进行捕获处理。
*   **Antd Message Warning**: "Static function can not consume context" 警告是因为直接使用了静态的 `message` 方法。虽然使用 `App.useApp()` 是更规范的修复方式，但为了保证变更的最小化和安全性，本次将优先专注于您要求的表格虚拟化功能。
