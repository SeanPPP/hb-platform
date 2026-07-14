# =============================================================================
# HBPOS WPF 后端 API Docker 配置
# 独立于主站 hb-platform-vite-api，生产容器固定使用 5003 端口。
# =============================================================================

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# 清理 NuGet 本地缓存，保持容器构建和当前源代码依赖一致。
RUN dotnet nuget locals all --clear

# 先复制项目文件，利用 Docker layer cache 加速 restore。
COPY apps/pos-wpf/src/Hbpos.Api/Hbpos.Api.csproj ./apps/pos-wpf/src/Hbpos.Api/
COPY apps/pos-wpf/src/Hbpos.Contracts/Hbpos.Contracts.csproj ./apps/pos-wpf/src/Hbpos.Contracts/
COPY services/backend/BlazorApp.Shared/BlazorApp.Shared.csproj ./services/backend/BlazorApp.Shared/

RUN dotnet restore "apps/pos-wpf/src/Hbpos.Api/Hbpos.Api.csproj" --verbosity minimal

# 复制运行所需源码；不要把测试项目带进镜像构建上下文。
COPY apps/pos-wpf/src/Hbpos.Api/ ./apps/pos-wpf/src/Hbpos.Api/
COPY apps/pos-wpf/src/Hbpos.Contracts/ ./apps/pos-wpf/src/Hbpos.Contracts/
COPY services/backend/BlazorApp.Shared/ ./services/backend/BlazorApp.Shared/

WORKDIR /src/apps/pos-wpf/src/Hbpos.Api
RUN dotnet build "Hbpos.Api.csproj" -c Release -o /app/build --no-restore || \
    (echo 'Retrying build with restore...' && \
     dotnet restore "Hbpos.Api.csproj" --verbosity minimal && \
     dotnet build "Hbpos.Api.csproj" -c Release -o /app/build --no-restore)

FROM build AS publish
RUN dotnet publish "Hbpos.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# healthcheck 依赖 curl；日志目录需要 appuser 可写。
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* && \
    adduser --disabled-password --gecos "" appuser && \
    mkdir -p /app/logs && \
    chown -R appuser /app

COPY --from=publish --chown=appuser:appuser /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5003
ENV ASPNETCORE_HTTP_PORTS=5003

USER appuser

HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:5003/api/v1/health || exit 1

EXPOSE 5003

ENTRYPOINT ["dotnet", "Hbpos.Api.dll"]
