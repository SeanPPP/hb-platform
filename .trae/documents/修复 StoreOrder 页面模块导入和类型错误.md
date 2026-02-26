# 修复 StoreOrder 前端错误

## 问题分析
用户报告了 `src/pages/StoreOrder/index.tsx` 中的两个错误：
1.  `[Error] 找不到模块“./components/CartDrawer”或其相应的类型声明。`
    *   `LS` 结果显示 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\StoreOrder\components\CartDrawer.tsx` 是存在的。
    *   这通常意味着 `CartDrawer.tsx` 可能是默认导出 (`export default`) 但引用方式不对，或者没有正确导出。需要检查该文件内容。
2.  `[Error] 参数“s”隐式具有“any”类型。`
    *   这是因为 `setInitialState` 的回调函数参数 `s` 没有指定类型。

## 实施步骤

### 1. 检查并修复 CartDrawer 导出
*   读取 `src/pages/StoreOrder/components/CartDrawer.tsx`。
*   确认其是否有 `export default CartDrawer;`。
*   如果导出正常，可能是 TypeScript 缓存问题或路径大小写问题（虽然 Windows 不敏感，但 TS 敏感）。`LS` 显示文件名是 `CartDrawer.tsx`，引用是 `./components/CartDrawer`，看起来匹配。

### 2. 修复隐式 any 类型错误
*   修改 `src/pages/StoreOrder/index.tsx`。
*   在 `setInitialState((s) => ({ ...s, selectedStore: null }));` 中，给 `s` 添加类型注解，例如 `(s: any)` 或 `(s: InitialState)`（如果能引入 InitialState 类型）。由于 `setInitialState` 是 umi 提供的，通常会自动推断，但显式声明更安全。

## 验证
*   重新编译前端，确认错误消失。

## 附带任务
*   如果有时间，可以顺便清理一些后端警告，但主要聚焦于修复前端构建错误。
