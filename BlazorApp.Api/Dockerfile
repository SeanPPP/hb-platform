# =============================================================================
# HB Platform 后端API Docker配置
# 基于.NET 9 ASP.NET Core Web API
# 支持JWT认证、CORS、Swagger文档
# =============================================================================

# 构建阶段 - 使用官方.NET SDK镜像
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 配置 NuGet 缓存目录（跨构建重用）
RUN dotnet nuget locals all --clear

# 复制解决方案文件和项目文件（优化Docker层缓存）
COPY *.sln ./
COPY BlazorApp.Api/*.csproj ./BlazorApp.Api/
COPY BlazorApp.Shared/*.csproj ./BlazorApp.Shared/

# 恢复NuGet包（利用Docker层缓存，只有项目文件变化时才重新下载）
RUN dotnet restore "BlazorApp.Api/BlazorApp.Api.csproj" --verbosity minimal

# 复制所有源代码文件
COPY BlazorApp.Api/ ./BlazorApp.Api/
COPY BlazorApp.Shared/ ./BlazorApp.Shared/

# 构建应用程序（Release模式，优化性能）
# 先尝试 --no-restore，失败则自动回退到完整 restore
WORKDIR /src/BlazorApp.Api
RUN dotnet build "BlazorApp.Api.csproj" -c Release -o /app/build --no-restore || \
    (echo 'Retrying build with restore...' && \
     dotnet restore "BlazorApp.Api.csproj" --verbosity minimal && \
     dotnet build "BlazorApp.Api.csproj" -c Release -o /app/build --no-restore)

# 发布应用程序（生成运行时所需的所有文件）
FROM build AS publish
RUN dotnet publish "BlazorApp.Api.csproj" -c Release -o /app/publish --no-restore

# 运行时阶段 - 使用轻量级运行时镜像
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

WORKDIR /app

# 安装健康检查所需的 curl，避免容器实际可用但 healthcheck 因工具缺失误报异常。
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# 创建非root用户以增强安全性
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

# 从发布阶段复制应用程序文件
COPY --from=publish /app/publish .

# 配置环境变量 - 容器内部使用 5002 端口
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5002
ENV ASPNETCORE_HTTP_PORTS=5002

# 配置环境变量占位符（通过 Docker Compose 或 docker run 注入实际值）
ENV ConnectionString=REDACTED
ENV ConnectionString=REDACTED
ENV ConnectionString=REDACTED
ENV ConnectionString=REDACTED
ENV ConnectionString=REDACTED
ENV ConnectionString=REDACTED
ENV Jwt__Key=""
ENV Translation__Kimi__ApiKey=REDACTED

# 健康检查配置 - 使用容器内部 5002 端口
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5002/api/health || exit 1

# 暴露容器内部 5002 端口（通过 docker-compose 映射到 5002）
EXPOSE 5002

# 应用程序入口点 - 使用环境变量 ASPNETCORE_ENVIRONMENT 控制环境
ENTRYPOINT ["dotnet", "BlazorApp.Api.dll"]

# =============================================================================
# 镜像构建命令：
# docker build -t hb-platform-api:latest -f BlazorApp.Api/Dockerfile .
#
# 运行容器命令：
# docker run -d -p 5002:5002 --name hb-api \
#   -e "ConnectionString=REDACTED
#   -e "Jwt__Key=your-jwt-secret-key" \
#   -e "Translation__Kimi__ApiKey=REDACTED
#   hb-platform-api:latest
#
# 端口说明：
# - 容器内部：5002 端口（非特权端口，非 root 用户可绑定）
# - 宿主机映射：5002 端口（与开发环境一致）
# - 外部访问：http://localhost:5002
# =============================================================================
