# 修复 IntlProvider 上下文问题

## 问题
`useIntl()` 找不到 `IntlProvider`，因为 `umi-plugin-keep-alive` 缓存的组件没有正确保留 React Context

## 修复方案
在 `KeepAliveTabLayout.tsx` 中包裹 `IntlProvider`，确保被 KeepAlive 缓存的组件能够访问国际化上下文

## 修改内容

### 修改 `KeepAliveTabLayout.tsx`
1. 添加 `IntlProvider` 导入
2. 添加 `getLocale` 导入获取当前语言
3. 导入语言包（zh-CN, en-US）
4. 用 `IntlProvider` 包裹 `TabContent` 或整个内容区域