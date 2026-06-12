# 迁移到 umi-plugin-keep-alive

## 修改内容

### 1. 安装新插件
```bash
npm install umi-plugin-keep-alive
```

### 2. 修改 .umirc.ts
- 添加 `plugins: ['umi-plugin-keep-alive']`
- 删除 `extraBabelPlugins: ['react-activation/babel']`

### 3. 修改 KeepAliveTabLayout.tsx
- 导入从 `'react-activation'` 改为 `'umi'`
- 删除 `AliveScope` 包裹
- 删除自定义缓存管理器相关代码
- 保留 Tab 拖拽排序、右键菜单等所有 UI 功能

### 4. 卸载旧插件
```bash
npm uninstall react-activation
```

### 5. 清理工具文件
- 删除 `src/utils/tabCache.ts`
- 删除 `src/utils/tabMemoryMonitor.ts`

### 6. 测试
- 验证 Tab 切换、拖拽、右键菜单等功能正常