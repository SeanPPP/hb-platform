## 执行步骤

### 1. 清理和删除不再使用的 BlazorApp 前端
- 删除 `BlazorApp/` 目录（Blazor WebAssembly 前端）
- 更新解决方案文件移除对 BlazorApp 项目的引用

### 2. 提交当前更改
- 处理子模块 `ReactUmi/my-app` 的未提交修改
- 提交删除 BlazorApp 的更改

### 3. 更新 git remote 地址
- 将 origin 从 `https://github.com/SeanPPP/BlazorAppHBweb.git` 
- 改为 `https://github.com/SeanPPP/HBBblazorweb.git`

### 4. 推送到新仓库
- 强制推送到新的远程仓库（因为会更改仓库地址）

## 注意事项
- 请确认新仓库 `https://github.com/SeanPPP/HBBblazorweb.git` 已在 GitHub 上创建
- 如果仓库不存在，需要先在 GitHub 创建该仓库
- 删除 BlazorApp 后，将只保留 ReactUmi（React 前端）和 BlazorApp.Api（后端）