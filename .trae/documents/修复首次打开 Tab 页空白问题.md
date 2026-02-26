## 修复计划

### 问题根因
`useAccessInfo` Hook 的初始化逻辑与 `useCurrentUser` 不一致，导致首次渲染时返回空对象，触发空白页面。

### 解决步骤

#### 1. 修复 `useAccessInfo` Hook 初始化逻辑
**文件**: `src/hooks/useGlobalState.ts`

**修改内容**:
- 将 `access` 初始值从 `{}` 改为 `getAccess()`，同步获取当前权限
- 将 `ready` 初始值从 `globalStateReady` 改为 `globalStateReady || Object.keys(getAccess()).length > 0`
- 保持与 `useCurrentUser` 一致的初始化模式

```typescript
// 修改前
const [access, setAccess] = useState<any>({});
const [ready, setReady] = useState(globalStateReady);

// 修改后
const [access, setAccess] = useState<any>(() => {
  const a = getAccess();
  return a || {};
});
const [ready, setReady] = useState(() => {
  return globalStateReady || Object.keys(getAccess()).length > 0;
});
```

#### 2. 调试日志优化
**文件**: `src/hooks/useGlobalState.ts`

**修改内容**:
- 在 `useAccessInfo` 初始化时添加调试日志，方便追踪状态
- 确保与 `useCurrentUser` 的日志风格一致

#### 3. 验证修复
- 清除浏览器缓存
- 首次打开 Dashboard 页面，验证数据正常显示
- 检查控制台日志，确认 `access` 对象首次就完整

---

### 预期效果
- ✅ 首次打开 Tab 页时，`access` 对象立即完整
- ✅ 页面正常显示内容，不再空白
- ✅ 所有调试日志清晰反映状态变化