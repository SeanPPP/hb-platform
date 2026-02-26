## 修复方案：移除手动 IntlProvider，使用 Umi Max 内置国际化

### 问题分析

1. Umi Max 在 `.umirc.ts` 中配置了 `locale: { ... }`
2. Umi Max 在应用根层级自动提供 `IntlProvider`
3. 我手动添加的 `react-intl` `IntlProvider` 与 Umi Max 的系统冲突
4. KeepAlive 缓存的组件从缓存恢复时，组件树结构发生变化

### 解决方案

**移除手动添加的 IntlProvider**，让 Umi Max 的全局 `IntlProvider` 工作：

1. 移除 `IntlProvider` 和语言包的导入
2. 移除 `TabContent` 中的 `locale` 和 `messages` 变量
3. 直接渲染组件，依赖 Umi Max 的全局国际化

### 修改文件

`src/layouts/KeepAliveTabLayout.tsx`

**移除导入**：
- `import { IntlProvider } from 'react-intl';`
- `import enUS from '../locales/en-US';`
- `import zhCN from '../locales/zh-CN';`

**修改 TabContent**：
```typescript
const TabContent: React.FC<{...}> = ({ tabKey, component, keepAlive = true, params }) => {
  const Component = componentMap[component];

  if (!Component) {
    return <div>组件未找到: {component}</div>;
  }

  return (
    <React.Suspense fallback={<div style={{ padding: 24 }}>加载中...</div>}>
      {keepAlive ? (
        <KeepAlive name={tabKey} id={tabKey}>
          <Component {...params} />
        </KeepAlive>
      ) : (
        <Component {...params} />
      )}
    </React.Suspense>
  );
};
```

### 原理

Umi Max 的 `useIntl()` hook 依赖全局的 `IntlProvider`。KeepAlive 不会影响全局 Provider 的传递，移除手动添加的 Provider 后，让 Umi Max 的系统正常工作。