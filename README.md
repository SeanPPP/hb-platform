# HBweb 全栈项目

## 项目简介
本项目是一个现代化的全栈应用，采用前后端分离架构。
- **后端**: 基于 .NET 9 的高性能 API 服务。
- **前端**: 基于 React 19 + Umi Max + Ant Design 5 的现代化单页应用 (SPA)。

## 项目结构
```
blazor/
├── BlazorApp.Api/       # 后端 API 项目 (.NET 9)
├── BlazorApp.Shared/    # 共享模型和 DTO (C# Class Library)
├── ReactUmi/
│   └── my-app/          # 前端项目 (React + Umi Max)
└── docs/                # 项目文档
```

## 技术栈

### 后端 (Backend)
- **框架**: .NET 9
- **ORM**: SqlSugar
- **数据库支持**: PostgreSQL / SQL Server / SQLite (开发环境)
- **认证**: JWT (JSON Web Token)
- **API 文档**: Swagger / OpenAPI

### 前端 (Frontend)
- **框架**: React 19
- **构建工具**: Umi Max
- **UI 组件库**: Ant Design 5 + ProComponents
- **表格组件**: AG Grid / Handsontable / React Data Grid
- **语言**: TypeScript

## 快速开始

### 1. 启动后端 API
确保已安装 .NET 9 SDK。

```bash
# 进入 API 目录
cd BlazorApp.Api

# 还原依赖
dotnet restore

# 启动服务 (HTTP 端口 5001)
dotnet run --launch-profile http
```
API 文档地址: http://localhost:5001/swagger

### 2. 启动前端应用
确保已安装 Node.js (推荐 LTS 版本) 和 npm/yarn/pnpm。

```bash
# 进入前端目录
cd ReactUmi/my-app

# 安装依赖
npm install

# 启动开发服务器
npm run dev
```
前端访问地址: http://localhost:8000 (默认 Umi 端口)

## 功能特性
- ✅ **多角色权限管理**: 支持店长、仓库经理、大区经理等不同角色的视图与权限控制。
- ✅ **现代化 UI**: 采用 Ant Design 5 设计语言，响应式布局。
- ✅ **业务模块**:
    - 用户与角色管理
    - 分店与供应商管理
    - 商品分类与商品管理
    - 进货单与订单管理
    - 价格策略管理
    - 仓库与库存管理
- ✅ **数据同步**: 支持 HQ (总部) 数据同步与本地业务处理。

## 开发说明
- **后端配置**: 数据库连接字符串位于 `BlazorApp.Api/appsettings.json`。
- **前端配置**: 代理配置位于 `ReactUmi/my-app/.umirc.ts` 或 `config/config.ts` (Umi 配置)。

---
*上次更新: 2026-01-03*
