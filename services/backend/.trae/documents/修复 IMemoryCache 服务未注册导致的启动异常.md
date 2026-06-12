## 异常原因分析
启动报错 `System.AggregateException` 和 `InvalidOperationException`，核心信息是：
`Unable to resolve service for type 'Microsoft.Extensions.Caching.Memory.IMemoryCache' while attempting to activate 'BlazorApp.Api.Authorization.PermissionAuthorizationHandler'.`

这意味着 `PermissionAuthorizationHandler` 依赖了 `IMemoryCache` 服务，但该服务未在依赖注入容器中注册。

经检查：
1. `PermissionAuthorizationHandler.cs` 确实注入了 `IMemoryCache` 用于缓存权限验证结果。
2. `Program.cs` 中缺少 `builder.Services.AddMemoryCache();` 的注册代码。

## 解决方案
在 `Program.cs` 中添加内存缓存服务的注册。

### 修改计划
1.  **编辑 `BlazorApp.Api/Program.cs`**：
    *   在服务注册区域（例如 `AddControllers` 之后或 `AddHttpContextAccessor` 附近）添加 `builder.Services.AddMemoryCache();`。

这样即可解决依赖注入失败的问题，让应用正常启动。