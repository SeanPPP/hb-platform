## 迁移方案：使用 umi-plugin-keep-alive 简化现有实现

### 原因分析

* `@alitajs/tabs-layout` 文档明确说明"**请不要和 keep alive 插件一起使用**"

* 用户需求要求保留 keepalive 功能

* 因此使用 `umi-plugin-keep-alive` 更合适

### 实施步骤

#### 1. 安装 umi-plugin-keep-alive

```bash
npm install umi-plugin-keep-alive --save
```

#### 2. 修改 .umirc.ts 配置

* 添加 `plugins: ['umi-plugin-keep-alive']`

* 移除 `extraBabelPlugins: ['react-activation/babel']`（插件会自动处理）

#### 3. 简化 KeepAliveTabLayout.tsx

* 从 `@umijs/max` 导入 `KeepAlive`、`useAliveController`（替代 `react-activation`）

* 移除 `AliveScope` 包裹层

* 保留现有功能：

  * Tab 拖拽排序（@dnd-kit）

  * 右键菜单

  * componentMap 和 iconMap

  * Tab 状态管理（tabModel）

#### 4. 更新相关导入

```typescript
// 替换
import { AliveScope, KeepAlive, useAliveController } from 'react-activation';

// 为
import { KeepAlive, useAliveController } from '@umijs/max';
```

#### 5. 测试验证

* 验证 Tab 切换时状态保持

* 验证右键菜单功能

* 验证拖拽排序功能

* 验证刷新功能

### 预期效果

* 保持所有现有功能

* 简化依赖管理（移除 react-activation 直接依赖）

* 使用官方 Umi 插件获得更好的集成

