# 🔍 HB-Platform 全模块代码审查报告

> **审查时间**: 2025-06-13  
> **审查范围**: 4 模块 + 基础设施 + API 契约  
> **审查方法**: 6 个子代理并行解耦审查  
> **审查深度**: 标准（正确性、安全性、可维护性、测试覆盖）

---

## 📋 模块概览

| 模块 | 语言/框架 | 文件数 | 评分 | 严重 | 警告 | 建议 |
|------|-----------|--------|------|------|------|------|
| `services/backend` | C# / ASP.NET Core | ~700 | **C+** | 5 | 7 | 6 |
| `apps/web` | React / TypeScript | ~200 | **B-** | 4 | 4 | 4 |
| `apps/mobile` | Expo / React Native | ~280 | **C+** | 3 | 5 | 8 |
| `apps/pos-wpf` | C# / WPF .NET | ~280 | **C+** | 6 | 9 | 8 |
| 基础设施 | Docker / Nginx / CI | — | **D** | 7 | 7 | 6 |
| API 契约一致性 | 跨模块 | — | **C+** | 2 | 4 | 4 |

---

## 🔴 严重问题清单（共 27 项 — 需立即修复）

### 🏆 最高优先级：凭据安全

| # | 模块 | 问题 | 位置 |
|---|------|------|------|
| 1 | 后端 | **生产凭据硬编码**：DB 密码、JWT 密钥、DeepSeek/腾讯云 API Key、邮箱密码 | `appsettings.Development.json:76-167` |
| 2 | 后端 | **密码哈希 SHA256 + 静态盐**（GPU 暴力破解可行） | `PasswordHasher.cs:17,35-46` |
| 3 | 后端 | **异常 StackTrace 泄露给客户端** | `ApiExceptionFilter.cs:29` |
| 4 | 后端 | **`[AllowAnonymous]` 暴露敏感数据库操作端点** | `ReactStoreProductMaintenanceController.cs:17` |
| 5 | Web | **前端 SHA256 密码哈希**（彩虹表可逆，重放攻击可行） | `utils/password.ts:3-4`, `pages/Login/index.tsx:45` |
| 6 | POS | **Square Access Token 明文 API 返回** | `SquareTokenService.cs:29-31` |
| 7 | POS | **DPAPI 加密无 Entropy**（同用户任意进程可解密） | `WindowsDpapiDeviceAuthorizationProtector.cs:68-70` |
| 8 | POS | **密钥片段泄露到日志** | `LinklyCloudBackendAsyncService.cs:2161-2169` |
| 9 | Mobile | **设备 authCode 明文存储** (AsyncStorage) | `deviceAuth.ts` |

### 🏆 基础设施

| # | 问题 | 位置 |
|---|------|------|
| 10 | **Docker ENV 硬编码占位符**（镜像层泄露） | `Dockerfile:61-69` |
| 11 | **Docker Compose 硬编码 REDACTED 连接串** | `docker-compose.yml:14-19` |
| 12 | **后端端口 5002 直通宿主机**（绕过 nginx 安全层） | `docker-compose.yml:8-9` |
| 13 | **Nginx 缺 HSTS / CSP 安全头** | `nginx.conf:8-10` |
| 14 | **Hangfire 面板无认证保护** | `nginx.conf:41-48` |
| 15 | **SSL 配置示例缺 TLS 强化** | `nginx.host-ssl.conf.example:8-22` |

### 🏆 API 契约

| # | 问题 | 位置 |
|---|------|------|
| 16 | **Mobile 端路由大小写风险** (`/auth/login` vs `/Auth/login`) | `api/config.ts:7` ↔ `AuthController.cs:17` |
| 17 | **DTO PascalCase/camelCase 双命名死代码**（每个字段两套回退） | `auth.ts:9-59` ↔ C# DTO |

### 🏆 正确性 / 架构

| # | 问题 | 位置 |
|---|------|------|
| 18 | Web **路由守卫绕过风险** (`accessKey` 缺失时放行) | `routes.tsx:600-605` |
| 19 | Web **无 CSRF 保护** | `request.ts` 全局 |
| 20 | POS **`async void` 崩溃风险**（4 处） | `App.xaml.cs:25,91` 等 |
| 21 | POS **巨型类 3975 行** `LinklyCloudBackendAsyncService` | 单文件 |
| 22 | POS **MainViewModel 构造函数 40 参数** | `MainViewModel.cs:186-308` |
| 23 | Mobile **巨型屏幕组件 >1000 行** | `warehouse.tsx`, `product-editing.tsx` |
| 24 | Mobile **API 密钥通过 `EXPO_PUBLIC_*` 打包进客户端 bundle** | `app.config.ts:12-13` |
| 25 | Mobile **缺失测试框架**（仅 25 个手动断言文件） | 全局 |
| 26 | 后端 **`ApiResponse<T>.Details` 包含完整 ModelState** | 多处 Controller |
| 27 | 后端 **JWT 密钥为有意义的短语** | `appsettings.Development.json:84` |

---

## 🟡 高风险警告（共 35 项）

<details>
<summary>点击展开完整警告清单</summary>

### 后端 (7)
| # | 问题 |
|---|------|
| W1 | `Console.WriteLine` 300+ 处代替 ILogger |
| W2 | 登录端点缺少速率限制 |
| W3 | `LoginRequest` DTO 缺 `[Required]` 注解 |
| W4 | `ProductsController` 响应格式与其他 Controller 不一致 |
| W5 | `GenerateRandomPassword` 混用安全/不安全随机数 |
| W6 | Swagger/DebugController 仅依赖环境判断 |
| W7 | `RegisterAsync` 中 PasswordHash 字段被滥用 |

### Web (4)
| # | 问题 |
|---|------|
| W8 | `console.error` 150+ 处直接打印原始 error |
| W9 | Store `console.error` 打印完整 error |
| W10 | 配置文件缺陷 |
| W11 | 路由守卫不完整 |

### Mobile (5)
| # | 问题 |
|---|------|
| W12 | 生产环境 `console.log` 泄露 |
| W13 | 巨型屏幕组件 |
| W14 | Token 刷新依赖双层嵌套结构 |
| W15 | API host 预设硬编码内网 IP |
| W16 | 开发环境使用 HTTP 明文 |

### POS (9)
| # | 问题 |
|---|------|
| W17 | 设备注册端点无认证 |
| W18 | SquareController 裸 catch 吞异常 |
| W19 | 通知端点日志记录完整 Bearer token |
| W20 | 广泛使用裸 `catch(Exception ex)` 无重抛 |
| W21 | LocalizationService 吞异常 |
| W22 | 日志文件提交到仓库 |
| W23 | Catalog Stores 端点 `[AllowAnonymous]` |
| W24 | WeatherForecastController 模板残留 |
| W25 | HttpClient 超时不一致 |

### 基础设施 (7)
| # | 问题 |
|---|------|
| W26 | 前端 Docker 容器以 root 运行 |
| W27 | Docker 镜像未锁定摘要 |
| W28 | `docker-compose.hotbargain.yml` YAML 格式不一致 |
| W29 | `ConnectionStrings` vs `ConnectionString` 键名不匹配 |
| W30 | Nginx 版本信息泄露 (缺 `server_tokens off`) |
| W31 | `AllowedHosts: "*"` Host Header Injection 风险 |
| W32 | CORS 过于宽松 (9 个来源) |

### API 契约 (4)
| # | 问题 |
|---|------|
| W33 | 路由版本号三套并存 (api/, api/v1/, api/react/v1/) |
| W34 | Web/Mobile 使用不同认证端点 |
| W35 | 三种错误响应模式 (ProblemDetails / ApiResponse.Error / 异常) |
| W36 | AuthController 中 `ModelState.IsValid` 死代码 |

</details>

---

## 🔵 改进建议（共 36 项）

<details>
<summary>点击展开完整建议清单</summary>

### 关键建议 TOP 10

1. **密码哈希升级** → bcrypt / argon2（后端 + 前端统一）
2. **引入 API 版本控制** → `Microsoft.AspNetCore.Mvc.Versioning`
3. **统一响应格式** → 全部 Controller 使用 `ApiResponse<T>`
4. **前端添加 jest + React Testing Library** → CI 集成
5. **Mobile 添加 expo-updates 错误监听器**
6. **POS 巨型类拆分** → `LinklyCloudAuthService` / `LinklyCloudTransactionService` 等
7. **Docker 镜像摘要锁定** → `FROM image@sha256:...`
8. **添加全局异常中间件** → POS Hbpos.Api
9. **统一 .NET 版本** → net9.0 全模块
10. **清理模板残留** → `WeatherForecastController.cs`, `UnitTest1.cs`, `appsettings.Development.json` 凭据

### 完整列表 (36)

| 领域 | 数量 | 示例 |
|------|------|------|
| 后端 | 6 | FluentValidation、集成测试、版本统一、Shared 常量枚举 |
| Web | 4 | branded types、统一错误日志、测试框架、权限拼写修正 |
| Mobile | 8 | expo-updates 监听、深度链接、i18n 复数、SecureStore 统一 |
| POS | 8 | 集成测试、结构化错误中间件、CancellationToken 传播、DataAnnotations |
| 基础设施 | 6 | .gitignore 增强、.dockerignore 扩展、Android 权限审查、CI 安全 |
| API 契约 | 4 | DTO 单命名、POS 回退安全、权限别名修正、枚举共享 |

</details>

---

## 📊 各模块评分详表

| 模块 | 安全性 | 正确性 | 可维护性 | 测试覆盖 | 综合 |
|------|--------|--------|----------|----------|------|
| **services/backend** | D | B | B- | B+ | **C+** |
| **apps/web** | C+ | B | B | B- | **B-** |
| **apps/mobile** | B | B+ | C+ | D | **C+** |
| **apps/pos-wpf** | C+ | B- | C+ | B | **C+** |
| **基础设施** | D | B | C+ | — | **D** |
| **API 契约** | — | B- | B- | — | **C+** |

---

## 🎯 优先修复路线图

### 🔥 立即（本周内 — 安全阻断项）
1. **轮换所有泄露凭据** — DB 密码、API Key、SecretKey、JWT 签名密钥
2. **`appsettings.Development.json`** — 移除真实凭据，改用环境变量 / Secret Manager
3. **密码哈希升级** — SHA256 → bcrypt (后端) + 移除前端哈希
4. **修复 `[AllowAnonymous]` 暴露** — `ReactStoreProductMaintenanceController` 加认证
5. **修复异常 StackTrace 泄露** — `ApiExceptionFilter` 移除 StackTrace

### ⚡ 短期（2 周内 — 安全加固）
6. Docker 端口不直通 + nginx HSTS/CSP/Hangfire 认证
7. POS Square Token 不通过 API 返回 + DPAPI 加 Entropy
8. Web 添加 CSRF 保护 + 路由守卫修复
9. Mobile authCode 迁移到 SecureStore + `EXPO_PUBLIC_*` 清理
10. POS 密钥日志脱敏

### 📋 中期（1 个月内 — 技术债）
11. POS 巨型类拆分 (`LinklyCloudBackendAsyncService` 3975 行)
12. Mobile 添加测试框架 (jest + RNTL)
13. DTO 双命名死代码清理
14. API 路由版本统一
15. POS `async void` 异常保护

### 🔮 长期（Q3 — 架构演进）
16. 统一 .NET 版本到 net9.0
17. 引入 API 版本控制中间件
18. 全局异常处理中间件
19. Docker 镜像摘要锁定
20. 统一 CI/CD 集成测试流水线

---

## ✅ 亮点

- **权限测试**：后端 `PermissionAuthorizationHandlerTests` 和 `ControllerAuthorizationMetadataTests` 详尽完善
- **API 归一化**：Mobile 端 API 客户端设计精心（token 刷新队列、自动重试、统一错误日志）
- **TypeScript Strict**：Web/Mobile 均开启 strict 模式
- **i18n**：完整中英文国际化覆盖
- **MVVM 一致性**：POS 端 DI 规范、视图模型命名清晰
- **CI 脚本安全**：`scripts/test-all.sh` 使用 `set -euo pipefail`，无注入风险

---

> *报告由 6 个独立子代理并行审查生成，主代理聚合汇总。*  
> *生成时间: 2025-06-13*  
> *审查工具: reasonix + codegraph + gitnexus*
