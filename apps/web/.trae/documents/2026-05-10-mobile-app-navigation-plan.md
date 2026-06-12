# AdminLayout 移动端 App 导航改造计划（antd-mobile 原生级体验）

> 范围：仅改造 AdminLayout（后台管理），ShopLayout 不涉及
> 要求：移动端使用频率高，需原生级体验 → 引入 antd-mobile

## 一、现状分析

### 1.1 AdminLayout 桌面端架构

```
┌──────────┬──────────────────────────────────┐
│          │  Header（面包屑 + 用户信息）        │
│  Sider   ├──────────────────────────────────┤
│  248px   │  AppTabs（多页签 + 拖拽 + 刷新）    │
│  侧边菜单  ├──────────────────────────────────┤
│          │  Content（RouteKeepAlive）         │
│          │                                   │
└──────────┴──────────────────────────────────┘
```

- 5 个一级菜单分组：工作台 / 系统管理 / 国内采购 / 仓库管理 / 收银管理
- 多 Tab 页签支持拖拽排序（dnd-kit）、固定页签、刷新
- 路由守卫基于 `access` 权限控制

### 1.2 移动端问题

| 问题 | 影响 |
|---|---|
| Sider 248px 固定宽度 | 手机上占满屏幕，内容区无空间 |
| 多 Tab 页签栏 | 窄屏无法横向滚动，拖拽无意义 |
| 面包屑 + Header Tag | 信息密度过高 |
| Ant Design Table | 横向溢出，无法触摸操作 |
| Modal 弹窗 | 小屏几乎全屏但布局不合理 |
| 无底部导航 | 缺少移动端 App 标准交互模式 |

## 二、技术方案

### 2.1 核心决策：引入 antd-mobile

**理由**：
- 移动端使用频率高，需要原生级体验（触摸反馈、手势、动画）
- antd-mobile 的 TabBar / NavBar / List / PullToRefresh / InfiniteScroll 等组件专为移动端设计
- antd-mobile 与 antd 可以共存，按屏幕宽度切换使用

**新增依赖**：
```
antd-mobile          # 移动端 UI 组件库
antd-mobile-icons    # 移动端图标
```

### 2.2 响应式双模式架构

```
桌面端 (≥768px)                移动端 (<768px)
┌──────────┬───────┐          ┌───────────────────┐
│          │Header │          │ NavBar (返回+标题)  │
│  Sider   ├───────┤          ├───────────────────┤
│  菜单    │ Tabs  │          │                   │
│          ├───────┤          │  内容（移动端组件）   │
│          │Content│          │                   │
└──────────┴───────┘          ├───────────────────┤
                               │ TabBar (底部导航)  │
                               └───────────────────┘
```

### 2.3 antd-mobile 关键组件映射

| 桌面端 (antd) | 移动端 (antd-mobile) | 用途 |
|---|---|---|
| Layout.Sider | `SideBar` / `Popup` | 侧滑菜单 |
| Header + Breadcrumb | `NavBar` | 顶部导航栏 |
| AppTabs 多页签 | 无（单页面导航） | 移动端不用多 Tab |
| — | `TabBar` | 底部固定导航 |
| Table | `List` + `InfiniteScroll` | 列表数据展示 |
| Modal | `Popup` / `Dialog` | 弹窗/表单 |
| Form | `Form` (antd-mobile) | 移动端表单 |
| Spin | `Skeleton` / `PullToRefresh` | 加载状态 |
| Pagination | `InfiniteScroll` | 无限滚动分页 |

## 三、移动端导航设计

### 3.1 底部 TabBar 结构

```
┌────────┬────────┬────────┬────────┬────────┐
│  🏠    │  📦    │  🛒    │  🏷️    │  ⚙️    │
│ 工作台  │  仓库   │ 收银   │ 国内采购 │  系统   │
└────────┴────────┴────────┴────────┴────────┘
```

5 个主入口对应路由一级分组：
1. **工作台** → `/dashboard`
2. **仓库** → `/warehouse/store-orders`（默认子路由）
3. **收银** → `/pos-admin/suppliers`（默认子路由）
4. **国内采购** → `/domestic-purchase/china-suppliers`（默认子路由）
5. **系统** → `/system/stores`（默认子路由）

> TabBar 根据 `access` 权限动态渲染，无权限的 Tab 自动隐藏

### 3.2 页面导航流程

```
TabBar 点击 → 直接跳转分组默认页
              ↓
   如果是分组首页（如仓库管理），页面顶部显示 antd-mobile Tabs 切换子模块
              ↓
   点击列表项 → Push 到详情页（NavBar 显示返回箭头）
              ↓
   返回 → 回到列表页
```

### 3.3 分组内子模块切换

仓库管理分组示例：
```
┌─────────────────────────────┐
│ ←  仓库管理              👤  │  ← NavBar
├─────────────────────────────┤
│ [分店订货] [商品管理] [分类] [标签] │  ← antd-mobile Tabs
├─────────────────────────────┤
│                             │
│  PullToRefresh + List       │  ← 移动端列表
│  - 订单 SO-2026-001         │
│  - 订单 SO-2026-002         │
│  - ...                      │
│  InfiniteScroll             │
│                             │
└─────────────────────────────┘
```

## 四、实现步骤

### Phase 1：基础设施（核心导航改造）

#### 步骤 1.1：安装 antd-mobile

```bash
npm install antd-mobile antd-mobile-icons
```

#### 步骤 1.2：创建 `useIsMobile` hook

- 新建 `src/hooks/useIsMobile.ts`
- 使用 `window.matchMedia('(max-width: 767px)')` 检测
- 返回 `boolean`，响应窗口变化

#### 步骤 1.3：创建 `MobileTabBar` 组件

- 新建 `src/components/MobileTabBar.tsx`
- 使用 antd-mobile `TabBar` 组件
- 5 个 Tab 对应一级菜单分组
- 根据 `access` 权限过滤显示
- `position: fixed; bottom: 0` 固定底部
- 配合 `SafeArea` 组件处理 iOS 安全区

#### 步骤 1.4：创建 `MobileNavBar` 组件

- 新建 `src/components/MobileNavBar.tsx`
- 使用 antd-mobile `NavBar` 组件
- 根据路由层级显示：首页显示标题，子页显示返回箭头 + 标题
- 右侧放用户头像/更多操作

#### 步骤 1.5：创建 `MobileLayout` 组件

- 新建 `src/layout/MobileLayout.tsx`
- 移动端专用布局：NavBar + Content + TabBar
- 内容区底部预留 TabBar 高度 padding
- 使用 antd-mobile `Popup` 实现侧滑菜单（汉堡菜单触发）

#### 步骤 1.6：改造 `AdminLayout.tsx`

- 引入 `useIsMobile` hook
- 桌面端（≥768px）：保持现有 Sider + Header + Tabs 布局完全不变
- 移动端（<768px）：渲染 `MobileLayout` 替代
- 共享 `useAuthStore`、`useTabsStore` 等状态

#### 步骤 1.7：补充移动端 CSS

在 `global.css` 中：
- 移动端内容区全宽、减小 padding
- 底部 TabBar 占位
- iOS 安全区域适配 `env(safe-area-inset-bottom)`
- antd-mobile 与 antd 样式隔离（antd-mobile 自带 CSS 前缀 `adm-`）

### Phase 2：移动端页面适配（渐进式）

#### 步骤 2.1：Dashboard 移动端适配

- 使用 antd-mobile `Card` / `List` 替代 antd `Card` / `Statistic`
- 创建 `DashboardMobile` 视图或通过 `useIsMobile` 条件渲染

#### 步骤 2.2：列表页移动端适配

通用策略：创建 `MobileListView` 复用组件
- 使用 antd-mobile `List` + `InfiniteScroll` + `PullToRefresh`
- 每行显示关键信息摘要 + 箭头指示可点击
- 搜索/筛选使用 antd-mobile `SearchBar` + `Selector`
- 需要适配的页面：
  1. 仓库商品管理
  2. 分店订货列表
  3. 国内商品 / 国内供应商 / 前缀管理
  4. 收银管理各子页面
  5. 系统管理（分店/用户/角色）

#### 步骤 2.3：详情页移动端适配

- NavBar 显示返回箭头 + 标题
- 内容使用 antd-mobile `List` 展示字段
- 操作按钮固定在底部（`FloatingBubble` 或固定 footer）

#### 步骤 2.4：表单/弹窗移动端适配

- 新增/编辑表单使用 antd-mobile `Form` + `Popup`（底部弹出）
- 删除确认使用 antd-mobile `Dialog`
- 复杂表单使用多步 `Steps` 引导

### Phase 3：高级移动端体验（可选）

- 下拉刷新（PullToRefresh）全局支持
- 骨架屏（Skeleton）加载占位
- 滑动操作（SwipeAction）列表项左滑删除/编辑
- 触觉反馈（navigator.vibrate）
- PWA 离线缓存策略优化

## 五、文件结构规划

```
src/
├── hooks/
│   └── useIsMobile.ts              # 移动端检测 hook
├── layout/
│   ├── AdminLayout.tsx             # 改造：响应式双模式分发
│   └── MobileLayout.tsx            # 新增：移动端布局壳层
├── components/
│   ├── MobileTabBar.tsx            # 新增：底部 TabBar
│   ├── MobileNavBar.tsx            # 新增：顶部导航栏
│   ├── MobileMenuDrawer.tsx        # 新增：侧滑菜单
│   └── MobileListView.tsx          # 新增：通用移动端列表视图
├── pages/
│   ├── Dashboard/
│   │   ├── index.tsx               # 改造：条件渲染桌面/移动视图
│   │   └── MobileDashboard.tsx     # 新增：移动端工作台视图
│   ├── Warehouse/
│   │   ├── Products/
│   │   │   ├── index.tsx           # 改造：条件渲染
│   │   │   └── MobileProducts.tsx  # 新增：移动端商品列表
│   │   └── StoreOrders/
│   │       ├── index.tsx           # 改造
│   │       ├── MobileStoreOrders.tsx  # 新增
│   │       └── ...
│   └── ...（其他页面同理）
└── styles/
    └── mobile.css                  # 新增：移动端专用样式
```

## 六、共享逻辑与状态

### 桌面端和移动端共享：
- **路由定义** (`routes.tsx`) — 相同的路由路径和权限配置
- **API 服务层** (`services/`) — 完全复用
- **类型定义** (`types/`) — 完全复用
- **状态管理** (`store/`) — 完全复用（auth, tabs, shop）
- **工具函数** (`utils/`) — 完全复用

### 桌面端独有：
- `AppTabs` 多页签 + dnd-kit 拖拽
- `RouteKeepAlive` 缓存
- `AdminLayout` Sider 布局

### 移动端独有：
- `MobileTabBar` 底部导航
- `MobileNavBar` 顶部导航
- `MobileLayout` 移动端布局壳层
- 各页面的 `Mobile*` 视图组件

## 七、风险与注意事项

1. **Bundle 体积**：antd-mobile 支持按需加载（tree-shaking），新增约 30-50KB gzip
2. **样式冲突**：antd 使用 `.ant-` 前缀，antd-mobile 使用 `.adm-` 前缀，不会冲突
3. **Keep-Alive 适配**：移动端不使用多 Tab 缓存，`RouteKeepAlive` 在移动端需跳过
4. **权限过滤**：TabBar 必须根据 `access` 动态渲染，与桌面端 Sider 菜单保持一致
5. **路由守卫**：移动端和桌面端使用相同的路由配置和权限检查
6. **PWA 兼容**：已有 `vite-plugin-pwa`，antd-mobile 的 CSS 为内联样式，与 PWA 无冲突
7. **iOS SafeArea**：使用 antd-mobile 的 `SafeArea` 组件处理底部安全区域
8. **TypeScript**：antd-mobile v5 完整 TypeScript 支持，无需额外 @types

## 八、执行优先级

| 优先级 | 内容 | 预估工作量 |
|---|---|---|
| **P0** | Phase 1 基础设施（hook + TabBar + NavBar + MobileLayout + AdminLayout 改造） | 核心 |
| **P1** | Phase 2.1 Dashboard 移动端适配 | 快速 |
| **P1** | Phase 2.2 高频列表页适配（仓库商品、分店订货） | 中等 |
| **P2** | Phase 2.2 其余列表页适配 | 渐进 |
| **P2** | Phase 2.3-2.4 详情页和表单适配 | 渐进 |
| **P3** | Phase 3 高级体验优化 | 可选 |
