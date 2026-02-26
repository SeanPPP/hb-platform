# 修复两个警告

## 问题分析

### 问题1: `getCachingNodes is not a function`
之前的解构方式不正确，`getCachingNodes` 需要作为方法调用。

### 问题2: `Tabs.TabPane is deprecated`
Ant Design 已弃用 `Tabs.TabPane`，推荐使用 `items` API。

## 修复方案

### 1. 修复 getCachingNodes 调用
不进行解构，直接使用 `aliveController.getCachingNodes()` 方法调用。

### 2. 使用 items API 替代 TabPane
改用现代的 `items` API 配置 Tabs，并使用 `activeKey` 控制显示。

```tsx
<Tabs
  type="editable-card"
  activeKey={activeKey}
  onChange={setActiveKey}
  onEdit={(targetKey, action) => {
    if (action === 'remove') {
      handleRemoveTab(targetKey as string);
    }
  }}
  hideAdd
  className={styles.tabs}
  renderTabBar={(props, DefaultTabBar) => (
    <DefaultTabBar {...props}>
      {(node: any) => (
        <Dropdown
          menu={getTabContextMenu(node.key as string)}
          trigger={['contextMenu']}
        >
          <DraggableTabNode {...node.props}>{node}</DraggableTabNode>
        </Dropdown>
      )}
    </DefaultTabBar>
  )}
  items={tabs.map((tab) => ({
    key: tab.key,
    label: (
      <>
        {tab.icon && iconMap[tab.icon]}
        <span style={{ marginLeft: tab.icon ? 8 : 0 }}>
          {typeof tab.title === 'string' &&
          (tab.title.startsWith('menu.') ||
            tab.title.startsWith('common.') ||
            tab.title.startsWith('store.') ||
            tab.title.startsWith('user.') ||
            tab.title.startsWith('role.'))
            ? intl.formatMessage({ id: tab.title })
            : tab.title}
        </span>
      </>
    ),
    closable: tab.closable,
    children: (
      <TabContent
        tabKey={tab.key}
        component={tab.component}
        keepAlive={tab.keepAlive !== false}
        params={tab.params}
      />
    ),
  }))}
/>
```

### 3. 修复调试日志调用
在 useEffect 中使用 `aliveController.getCachingNodes()` 而不是解构的方法。