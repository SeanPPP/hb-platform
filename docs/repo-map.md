# 仓库映射

| 原仓库 | 迁入路径 | 职责 |
| --- | --- | --- |
| `git@github.com:SeanPPP/HbwebExpo.git` | `apps/mobile` | Expo / React Native 移动端 |
| `git@github.com:SeanPPP/hbweb_rv.git` | `apps/web` | Vite / React Web 端 |
| `https://github.com/SeanPPP/hbpos_win.git` | `apps/pos-wpf` | WPF POS 客户端、POS API、POS Contracts |
| `git@github.com:SeanPPP/HBBblazorweb.git` | `services/backend` | 主业务后端和共享项目 |

## 后端边界

- 主业务后端位于 `services/backend`。
- POS 自带后端位于 `apps/pos-wpf/src/Hbpos.Api`。
- POS API 可以引用主后端的 `BlazorApp.Shared`，但不与主业务后端合并。

