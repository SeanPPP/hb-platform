# 修复 Ant Design `message` 静态方法调用警告

用户报告了 `Warning: [antd: message] Static function can not consume context like dynamic theme. Please use 'App' component instead.` 警告，以及分类树点击后过滤商品失败的问题。这通常是因为在 Ant Design v5 中，直接使用 `message.error` 等静态方法无法获取上下文（Context），导致无法使用动态主题或国际化配置，有时甚至会因为上下文丢失而报错。

## 1. 问题分析

**警告信息**:
`Warning: [antd: message] Static function can not consume context like dynamic theme. Please use 'App' component instead.`

**原因**:
在 Ant Design v5 中，官方推荐使用 `<App>` 组件包裹应用，并通过 `App.useApp()` hook 来获取 `message`, `modal`, `notification` 实例。直接从 `antd` 导入的 `message` 是静态方法，无法感知 Context。

**关联问题**:
`Failed to fetch category tree: [blank message]` 和 `AxiosError` 表明数据获取失败。虽然 `message` 警告本身可能只是样式问题，但如果代码逻辑依赖于 message 的回调或者上下文中的其他状态，可能会间接导致问题。更重要的是，用户反馈“点击分类数 过滤商品失败”，这可能是因为请求逻辑或状态更新逻辑有问题。

**涉及文件**:
*   `src/app.ts`: 全局请求拦截器中使用了 `message.error`。
*   `src/pages/StoreOrder/components/CategorySidebar.tsx`: 数据加载失败时可能使用了 `message`（虽然用户提供的日志显示了 `console.error`，但通常 UI 组件也会弹窗提示）。
*   `src/services/warehouseCategory.ts` 和后端控制器: 之前的修复可能已经解决了数据格式问题，但如果请求仍然失败（AxiosError），可能是网络问题或参数问题。

## 2. 解决方案

### 方案 A: 使用 Ant Design 的 `App` 组件 (推荐)

1.  **修改入口**: 在 `src/app.ts` 或 `src/layouts/index.tsx` (如果有) 的最外层包裹 `<App>` 组件。
2.  **修改调用**: 将静态 `message` 调用改为 `App.useApp().message`。但对于 `app.ts` 这种非 React 组件文件（如请求拦截器），无法使用 hook。
3.  **非组件环境解决方案**: Ant Design 提供了 `App.useApp` 的替代方案，或者对于 `app.ts` 中的静态方法，我们可以接受警告，但为了消除警告并确保功能，可以使用 `App.useApp` 提供的静态方法注册机制，或者在 `request` 配置中尽量减少对 `message` 的依赖，改用返回错误并在组件层处理。

**针对本项目的具体修复**:
由于 `src/app.ts` 是 Umi 的运行时配置，我们可以在 `rootContainer` 中包裹 `<App>`。

**步骤**:
1.  **修改 `src/app.ts`**: 添加 `rootContainer` 配置，包裹 `App` 组件。
2.  **组件层**: 在 `CategorySidebar.tsx` 等组件中，改用 `App.useApp()` 获取 `message` 实例。

### 方案 B: 修复分类点击过滤失败 (核心业务问题)

用户提到“点击分类数 过滤商品失败”，这说明之前的 `camelCase` 修复可能还不够，或者点击事件处理逻辑有问题。

1.  **检查 `CategorySidebar.tsx`**: 确认 `onSelect` 回调传递的 `categoryGuid` 是否正确。
2.  **检查 `StoreOrder/index.tsx`**: 确认接收到 `categoryGuid` 后，是否正确触发了商品列表的刷新，并且参数名是否匹配（后端期望 camelCase 还是 PascalCase 的 Query 参数？ASP.NET Core 通常不区分 Query 参数大小写，但最好确认）。

## 3. 详细修复步骤

### 3.1 消除 Antd Warning (可选但推荐)
在 `src/app.ts` 中添加 `rootContainer`。

```typescript
import { App } from 'antd';

export function rootContainer(container: React.ReactNode) {
  return React.createElement(App, null, container);
}
```

### 3.2 修复分类过滤逻辑
我们需要检查 `CategorySidebar.tsx` 的 `handleSelect` 和 `StoreOrder/index.tsx` 的联动。

**假设问题**: `CategorySidebar` 传递的 ID 正确，但父组件调用 API 时参数不对。

**验证**:
*   `CategorySidebar` 选中节点时，`selectedKeys[0]` 应该是 `categoryGUID`。
*   `StoreOrder` 页面接收到 ID 后，调用 `getProductsByCategory`。

**关键点**:
之前的修复将 `WarehouseCategoryDto` 改为了 camelCase。
但是，`Tree` 组件的 `key` 属性必须有值。
在 `transformToDataNode` 中：
```typescript
key: cat.categoryGUID,
```
如果后端返回的数据确实是 `categoryName` (camelCase)，那么 `cat.categoryGUID` 应该能取到值。
但是，如果后端返回的仍然是 PascalCase (尽管配置了 CamelCase，有时为了兼容旧代码可能会有特殊处理)，那么 `cat.categoryGUID` 就是 `undefined`。

**为了稳健性**:
我将修改 `transformToDataNode`，尝试同时读取 camelCase 和 PascalCase 属性，确保 `key` 和 `title` 不为空。

```typescript
title: cat.categoryName || cat.CategoryName,
key: cat.categoryGUID || cat.CategoryGUID,
```

并且，对于 `Failed to fetch category tree: [blank message]`，我需要在 `CategorySidebar.tsx` 中添加更详细的错误日志，打印完整的 `error` 对象。

## 4. 执行计划

1.  **修改 `src/pages/StoreOrder/components/CategorySidebar.tsx`**:
    *   增强数据映射的鲁棒性（兼容 camelCase 和 PascalCase）。
    *   优化错误日志输出。
2.  **修改 `src/app.ts`**:
    *   引入 `App` 组件包裹应用，解决 Context 警告。

## 5. 验证
用户需要刷新页面，查看：
1.  警告是否消失。
2.  分类树是否显示。
3.  点击分类是否能触发商品过滤。
