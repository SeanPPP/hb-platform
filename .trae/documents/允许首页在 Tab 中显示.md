我将修改 `ReactUmi/my-app/src/app.ts` 文件，允许 "首页" (`/home`) 在 Tab 页签中打开。

### 1. 修改 `ReactUmi/my-app/src/app.ts`
*   **目标**: 移除对 `/home` 路径的显式排除逻辑。
*   **操作**: 将第 253 行的判断条件 `if (addTab && menuItemProps.path && menuItemProps.path !== '/home')` 修改为 `if (addTab && menuItemProps.path)`。

### 2. 验证
*   这将允许点击“首页”菜单时触发 `addTab` 逻辑，从而将首页作为一个正常的 Tab 页签显示。
