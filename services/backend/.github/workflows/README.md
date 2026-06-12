# Docker 自动构建和推送配置说明

本配置实现了 GitHub 提交后自动构建 Docker 镜像并推送到 Docker Hub 的功能。

## 工作原理

当代码推送到 `master` 分支时，GitHub Actions 会自动触发以下流程：

1. 检出代码
2. 设置 Docker Buildx（支持多平台构建）
3. 登录 Docker Hub
4. 生成日期时间标签（格式：`YYYY-MM-DD-HHmm`）
5. 构建并推送镜像，生成两个标签：
   - `dogbad/hbweb:latest` - 始终指向最新版本
   - `dogbad/hbweb:YYYY-MM-DD-HHmm` - 记录构建时间，方便回滚

## 配置步骤

### 1. 在 Docker Hub 生成 Access Token

1. 登录 [Docker Hub](https://hub.docker.com/)
2. 点击右上角头像 → **Account Settings**
3. 左侧菜单选择 **Security**
4. 点击 **New Access Token**
5. 输入 Token 描述（如：GitHub Actions）
6. 点击 **Generate**
7. **重要**：复制生成的 Token（只显示一次）

### 2. 在 GitHub 仓库配置 Secrets

1. 打开 GitHub 仓库页面
2. 点击 **Settings** 标签
3. 左侧菜单选择 **Secrets and variables** → **Actions**
4. 点击 **New repository secret**
5. 添加以下两个 Secrets：

   | Name | Value |
   |------|-------|
   | `DOCKER_USERNAME` | `dogbad`（你的 Docker Hub 用户名） |
   | `DOCKER_PASSWORD` | 刚才生成的 Docker Hub Access Token |

6. 点击 **Add secret** 保存

### 3. 验证配置

推送代码到 `master` 分支：

```bash
git add .
git commit -m "配置 Docker 自动构建"
git push origin master
```

然后在 GitHub 仓库中：

1. 点击 **Actions** 标签
2. 查看最新的 workflow 运行状态
3. 点击进入查看详细日志

构建成功后，可以在 [Docker Hub](https://hub.docker.com/r/dogbad/hbweb) 查看推送的镜像。

## 使用新镜像

每次构建会生成两个标签：

- **`latest`** - 始终指向最新版本，用于生产部署
- **`YYYY-MM-DD-HHmm`** - 日期时间标签，用于版本回滚

### 拉取最新镜像

```bash
docker pull dogbad/hbweb:latest
```

或使用 docker-compose：

```bash
docker-compose pull hb-api
docker-compose up -d hb-api
```

### 拉取特定版本

```bash
docker pull dogbad/hbweb:2026-02-08-1430
```

### 查看所有标签

访问 [Docker Hub](https://hub.docker.com/r/dogbad/hbweb/tags) 查看所有可用标签。

## 自定义配置

### 修改触发分支

编辑 `.github/workflows/docker-build-push.yml` 文件，修改 `on.push.branches` 部分：

```yaml
on:
  push:
    branches:
      - main  # 改为 main 分支
      - develop  # 或添加其他分支
```

### 添加多个标签

修改 `tags` 部分：

```yaml
tags: |
  ${{ secrets.DOCKER_USERNAME }}/hbweb:latest
  ${{ secrets.DOCKER_USERNAME }}/hbweb:${{ github.sha }}
  ${{ secrets.DOCKER_USERNAME }}/hbweb:v1.0.0
```

### 构建多平台镜像

在 `build-push-action` 中添加 `platforms` 参数：

```yaml
platforms: linux/amd64,linux/arm64
```

## 故障排查

### 构建失败

1. 检查 GitHub Actions 日志中的错误信息
2. 确认 Docker Hub 用户名和 Access Token 正确
3. 检查 Docker Hub 仓库是否公开

### 推送失败

1. 确认 Docker Hub Access Token 有推送权限
2. 检查是否超过 Docker Hub 免费限额
3. 确认镜像名称正确（用户名/仓库名）

### 权限问题

确保 Docker Hub Token 具有 **Read, Write & Delete** 权限。

## 相关文件

- Workflow 配置：`.github/workflows/docker-build-push.yml`
- Dockerfile：`BlazorApp.Api/Dockerfile`
- Docker Compose：`docker-compose.yml`

## 参考资源

- [GitHub Actions 官方文档](https://docs.github.com/en/actions)
- [Docker Build Push Action](https://github.com/docker/build-push-action)
- [Docker Hub Access Tokens](https://docs.docker.com/security/for-developers/access-tokens/)
