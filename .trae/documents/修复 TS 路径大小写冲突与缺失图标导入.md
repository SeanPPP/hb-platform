## 问题分析
- 报错一（路径大小写冲突）：TypeScript 检测到 `node_modules/antd/es/index.d.ts` 被同一工程以两种不同绝对路径大小写引用：`d:/Development/cline/blazor/reactumi/my-app/...` 与 `D:/Development/cline/blazor/ReactUmi/my-app/...`。来源包含 Umi 生成文件 `src/.umi/plugin-layout/Layout.tsx`，其第 13-15 行使用了绝对路径导入：
  - `d:/Development/cline/blazor/reactumi/my-app/src/.umi/plugin-layout/Layout.tsx:13-15` 显示 `import { ProLayout } from "D:/Development/cline/blazor/ReactUmi/my-app/node_modules/@ant-design/pro-components"`（绝对路径携带大写 `ReactUmi`），导致 TypeScript 在 Windows 下触发“文件名仅在大小写方面不同”的错误。
- 报错二（缺失图标导入）：`src/layouts/KeepAliveTabLayout.tsx` 第 74 行使用了 `SettingOutlined`，但没有从 `@ant-design/icons` 导入：
  - `d:/Development/cline/blazor/reactumi/my-app/src/layouts/KeepAliveTabLayout.tsx:74` 用到了 `<SettingOutlined />`，顶部仅导入了其他图标。

## 修复方案
- 步骤 1：补齐图标导入
  - 在 `src/layouts/KeepAliveTabLayout.tsx` 顶部的图标导入中加入 `SettingOutlined`：
  - 文件位置：`d:/Development/cline/blazor/reactumi/my-app/src/layouts/KeepAliveTabLayout.tsx:4-22`，在同一组 `@ant-design/icons` 导入里新增 `SettingOutlined`。
- 步骤 2：消除路径大小写报错（二选一，推荐优先 A）
  - A. 统一工程路径大小写并重建 `.umi`
    - 关闭前端开发进程与编辑器的 TypeScript 服务。
    - 删除或清空 `src/.umi`（Umi 会重建）。
    - 以统一大小写的路径打开工程（建议使用 `D:\Development\cline\blazor\ReactUmi\my-app`，与 `.umi` 生成的绝对路径保持一致），重新启动 `pnpm dev`，让 `.umi` 重新生成，避免混用 `reactumi` 与 `ReactUmi`。
  - B. 快速工程级消除（Windows 下可接受）
    - 在根 `tsconfig.json` 添加：`compilerOptions.forceConsistentCasingInFileNames: false`，关闭大小写一致性强制检查，防止该类报错阻塞开发。
- 步骤 3（可选加强）：将 `src/.umi/plugin-layout/Layout.tsx` 的绝对路径导入改为模块名导入
  - 把 `import { ProLayout } from "D:/.../node_modules/@ant-design/pro-components"` 改为 `import { ProLayout } from "@ant-design/pro-components"`，避免绝对路径参与模块解析，降低再次出现大小写冲突的可能性。注意 `.umi` 会在重启或路由/配置变更时重生，该改动可能被覆盖，仍建议按步骤 2A 统一路径。

## 验证方案
- 运行类型检查或启动开发服务器验证：
  - 执行 `pnpm type-check` 或 `pnpm dev`，确认不再出现“仅在大小写方面不同”的 TypeScript 报错。
- 页面功能验证：
  - 打开带有 Tab 的页面（例如仓库或分店管理页），右键 Tab 显示菜单，确认 `SettingOutlined` 不再报错，整体编译通过。

## 预期影响与说明
- 方案 2A 不改变代码逻辑，仅统一路径与重建生成文件，是最干净稳妥的长期方案。
- 方案 2B 旨在快速消除阻塞，牺牲大小写一致性检查，对 Windows 项目通常可接受。
- 步骤 1 为纯类型修复，不影响运行时行为。