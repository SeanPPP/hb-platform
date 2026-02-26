## 修改内容

### 1. 修改 Dockerfile
- 将 `ASPNETCORE_URLS` 从 `http://+:80` 改为 `http://+:5001`
- 将 `ASPNETCORE_HTTP_PORTS` 从 `80` 改为 `5001`
- 将 `EXPOSE` 从 `80` 改为 `5001`
- 更新健康检查 URL 端口为 `5001`

### 2. 修改 docker-compose.yml
- 将端口映射从 `"5001:80"` 改为 `"5001:5001"`
- 将 `ASPNETCORE_HTTP_PORTS` 环境变量从 `80` 改为 `5001`
- 更新健康检查 URL 端口为 `5001`

## 效果
- 容器内部使用 5001 端口（非特权端口，非 root 用户可绑定）
- 外部通过 5001 端口访问（`http://服务器IP:5001`）
- 与开发环境端口保持一致