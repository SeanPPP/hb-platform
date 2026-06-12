# 迁移记录

## 迁移策略

本次迁移采用新建 monorepo 的方式，不直接覆盖原 4 个仓库。

使用 `git subtree add` 导入历史，保证每个项目可单独回滚。

## 迁入路径

```text
HbwebExpo/HbwebExpoApp       -> apps/mobile
hbweb_rv                     -> apps/web
hbpos_win                    -> apps/pos-wpf
HBBblazorweb-master-vite     -> services/backend
```

## 已知调整

- POS 仓库整体保留在 `apps/pos-wpf`，第一阶段不拆出 `services/pos-api`。
- POS API 的主后端共享项目引用已改为 `..\..\..\..\services\backend\BlazorApp.Shared\BlazorApp.Shared.csproj`。
- 本地环境文件、工具状态目录、依赖目录和构建产物不迁入。
