# HB Platform Monorepo

HB Platform 是统一源码仓库，包含移动端、Web 端、POS/WPF 和主业务后端。

## 项目入口

```text
apps/mobile          Expo 移动端
apps/web             Vite Web 端
apps/pos-wpf         POS/WPF，内部包含 POS API 和 POS Contracts
services/backend     主业务后端
```

## 常用命令

移动端：

```bash
cd apps/mobile
npm install
npm run start
```

Web 端：

```bash
cd apps/web
npm install
npm run dev
```

主业务后端：

```bash
cd services/backend
dotnet restore BlazorApp.sln
dotnet run --project BlazorApp.Api/BlazorApp.Api.csproj
```

POS/WPF：

```bash
cd apps/pos-wpf
dotnet restore hbpos_win.slnx
dotnet run --project src/Hbpos.Client.Wpf/Hbpos.Client.Wpf.csproj
```

## 架构边界

本仓库是 monorepo，不是单体应用。各项目仍保持独立依赖、独立构建和独立部署。

仓库内有两个后端：

- `services/backend`：主业务后端。
- `apps/pos-wpf/src/Hbpos.Api`：POS 自带后端。

