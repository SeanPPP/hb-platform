## 修复 React Intl 与 KeepAlive 的集成问题

### 问题分析

当前代码中 `IntlProvider` 包裹在 `KeepAlive` 外部：
```typescript
<IntlProvider locale={locale} messages={messages}>
  <KeepAlive name={tabKey} id={tabKey}>
    <Component {...params} />
  </KeepAlive>
</IntlProvider>
```

这导致缓存的组件从缓存恢复时，无法访问 IntlProvider 上下文。

### 解决方案

将 `IntlProvider` 移到 `KeepAlive` **内部**：
```typescript
{keepAlive ? (
  <KeepAlive name={tabKey} id={tabKey}>
    <IntlProvider locale={locale} messages={messages}>
      <Component {...params} />
    </IntlProvider>
  </KeepAlive>
) : (
  <IntlProvider locale={locale} messages={messages}>
    <Component {...params} />
  </IntlProvider>
)}
```

### 修改文件

- `src/layouts/KeepAliveTabLayout.tsx` - 调整 TabContent 组件的 JSX 结构

### 预期效果

- SalesDashboard 和其他使用 useIntl() 的组件能正常工作
- Tab 切换时组件状态保持（KeepAlive 功能正常）
- 语言切换功能正常