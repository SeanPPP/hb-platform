# Ant Design Blazor 升级指南

## 概述

本文档记录了基于最新 [Ant Design Blazor 文档](https://antblazor.com/zh-CN/docs/introduce) 的系统升级和错误修复。

## 升级内容

### 1. App.razor 配置更新

根据最新文档，需要在 `App.razor` 中添加 `<AntContainer />` 组件：

```razor
@using BlazorApp.Components

<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <LayoutView Layout="@typeof(MainLayout)">
            <p role="alert">Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>

<!-- 添加AntContainer组件用于动态显示弹出组件 -->
<AntContainer />
```

### 2. 消息服务实现

创建了统一的消息服务接口和实现，避免与Ant Design的IMessageService冲突：

#### ICustomMessageService 接口
```csharp
public interface ICustomMessageService
{
    Task Success(string message);
    Task Error(string message);
    Task Warning(string message);
    Task Info(string message);
    Task<bool> Confirm(string message);
}
```

#### MessageService 实现
```csharp
public class MessageService : ICustomMessageService
{
    private readonly AntDesign.IMessageService _messageService;
    private readonly ILogger<MessageService> _logger;

    public MessageService(AntDesign.IMessageService messageService, ILogger<MessageService> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    public Task Success(string message)
    {
        _messageService.Success(message);
        _logger.LogInformation("Success message: {Message}", message);
        return Task.CompletedTask;
    }

    // ... 其他方法实现
}
```

### 3. 服务注册更新

在 `Program.cs` 中添加了必要的服务注册：

```csharp
// 注册角色服务
builder.Services.AddScoped<IRoleServiceClient, RoleServiceClient>();

// 注册消息服务
builder.Services.AddScoped<ICustomMessageService, MessageService>();
```

## 新版本特性

### 1. 支持的 .NET 版本
- 兼容 .NET Core 3.1 / .NET 5 / .NET 6 / .NET 7 / .NET 8 / .NET 9
- 推荐使用 .NET 9

### 2. 托管方式
- 支持 WebAssembly 静态文件部署
- 支持服务端双向绑定
- 支持 Blazor Server 和 Blazor WebAssembly

### 3. 浏览器支持
- 主流 4 款现代浏览器
- Internet Explorer 11+ （仅 Blazor Server）
- 不支持 IE 浏览器（WebAssembly）

### 4. 设计规范
- 与 Ant Design 设计规范定期同步
- 支持 4.x 样式

## 使用指南

### 1. 安装依赖
```bash
dotnet add package AntDesign
```

### 2. 服务注册
```csharp
builder.Services.AddAntDesign();
```

### 3. 命名空间引用
```razor
@using AntDesign
```

### 4. 组件使用
```razor
<Button Type="@ButtonType.Primary">Hello World!</Button>
```

## 常见问题解决

### 1. 弹出组件不显示
**问题**：Modal、Message 等弹出组件不显示
**解决**：确保在 `App.razor` 中添加了 `<AntContainer />` 组件

### 2. 消息服务未注册
**问题**：`ICustomMessageService` 未注册错误
**解决**：在 `Program.cs` 中注册 `ICustomMessageService`

### 3. 样式不生效
**问题**：Ant Design 样式未加载
**解决**：确保正确引用了 Ant Design CSS

### 4. 命名冲突
**问题**：`IMessageService` 与 Ant Design 的接口冲突
**解决**：使用 `ICustomMessageService` 避免冲突

## 最佳实践

### 1. 错误处理
使用统一的消息服务进行错误提示：
```csharp
await MessageService.Error("操作失败");
```

### 2. 加载状态
使用 Loading 属性显示加载状态：
```razor
<Table Loading="@loading" />
```

### 3. 响应式设计
使用 Ant Design 的响应式组件：
```razor
<Row>
    <Col Span="24" Xs="24" Sm="12" Md="8" Lg="6" Xl="4">
        <!-- 内容 -->
    </Col>
</Row>
```

## 版本兼容性

### 当前版本
- Ant Design Blazor: 1.4.3
- .NET: 9.0
- 浏览器: Chrome, Firefox, Safari, Edge

### 升级注意事项
1. 确保所有依赖包版本兼容
2. 测试所有现有功能
3. 检查自定义样式兼容性
4. 验证第三方组件集成

## 参考资料

- [Ant Design Blazor 官方文档](https://antblazor.com/zh-CN/docs/introduce)
- [Blazor 官方文档](https://docs.microsoft.com/zh-cn/aspnet/core/blazor/)
- [.NET 9 文档](https://docs.microsoft.com/zh-cn/dotnet/)

## 更新日志

### 2024-12-19
- 添加 AntContainer 组件支持
- 实现统一消息服务（ICustomMessageService）
- 修复服务注册问题
- 解决命名冲突问题
- 更新技术文档

---

*本文档将随着 Ant Design Blazor 的更新而持续维护* 