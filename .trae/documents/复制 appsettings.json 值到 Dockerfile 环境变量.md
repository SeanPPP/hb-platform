## 修复计划

### 问题
Dockerfile 和 docker-compose.yml 端口配置冲突：
- Dockerfile: `ASPNETCORE_URLS=http://+:5001`
- docker-compose.yml: `ASPNETCORE_URLS=http://+:80`，端口映射 `"5001:80"`

### 解决方案

**1. 修改 Dockerfile**
- 将端口从 5001 改为 80（与 docker-compose.yml 一致）
- 修改 EXPOSE 端口
- 修改 HEALTHCHECK 端口

**2. 创建 .env 文件**
- 添加所有环境变量配置
- 敏感信息（数据库密码、API 密钥）放在这里管理
- 不需要每次手动输入

**3. 同步修改 docker-compose.production.yml**
- 将端口映射从 `"5001:80"` 改为 `"5001:5001"`（如果选择方案2）
- 或者保持不变（如果选择方案1）

### 推荐方案：方案 1（统一使用 80 端口）

Dockerfile 使用 80 端口，docker-compose.yml 通过 `"5001:80"` 映射到宿主机。

**优点**：
- 与现有配置兼容
- 符合 Docker 最佳实践（容器内 80 端口）
- 端口映射清晰明确

### 预期效果

创建 `.env` 文件后，只需运行：
```bash
docker-compose up -d
```

无需每次手动输入环境变量。
