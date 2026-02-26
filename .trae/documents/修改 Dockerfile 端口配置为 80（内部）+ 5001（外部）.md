## 修改 Dockerfile - 统一使用 80 端口（内部）+ 5001（外部映射）

### 修改内容

| 位置 | 原值 | 新值 |
|------|------|------|
| 环境变量 `ASPNETCORE_URLS` | `http://+:5001` | `http://+:80` |
| 环境变量 `ASPNETCORE_HTTP_PORTS` | `5001` | `80` |
| `EXPOSE` | `5001` | `80` |
| `HEALTHCHECK` | `http://localhost:5001/api/health` | `http://localhost:80/api/health` |

### 端口映射说明

```
开发环境 (dotnet run)  →  直接监听 5001 端口
                         ↓
生产环境 (Docker)      →  容器内部 80 端口
                         ↓
                         宿主机映射 5001:80
                         ↓
外部访问              →  http://localhost:5001
```

### 优势

- **容器内部标准 80 端口** - 符合微服务最佳实践
- **外部访问 5001** - 与开发环境端口一致，方便调试
- **开发环境不受影响** - `dotnet run` 仍使用 `launchSettings.json` 的 5001 端口