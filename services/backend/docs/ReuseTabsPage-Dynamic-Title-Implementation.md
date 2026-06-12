# IReuseTabsPage 动态标签标题实现指南

## 概述

通过实现 `IReuseTabsPage` 接口，可以使用 **AntDesign Blazor 官方 API** 动态更新 ReuseTabs 标签标题，无需手动操作 DOM。

## 为什么这样做？

### ❌ 旧方案（DOM 操作）

```csharp
// 使用 JavaScript 操作 DOM
var jsCode = $@"
setTimeout(function() {{
    const tabs = document.querySelectorAll('.ant-tabs-tab');
    tabs.forEach(tab => {{
        if (tab.classList.contains('ant-tabs-tab-active')) {{
            tab.querySelector('.ant-tabs-tab-btn').textContent = '{title}';
        }}
    }});
}}, 300);";

await JSRuntime.InvokeVoidAsync("eval", jsCode);
```

**缺点：**
- 🔴 不是官方 API，不稳定
- 🔴 需要延迟执行，有时序问题
- 🔴 DOM 结构变化会导致失效
- 🔴 代码复杂，难以维护

### ✅ 新方案（IReuseTabsPage 接口）

```csharp
// 实现接口
@implements IReuseTabsPage

// 实现方法
public RenderFragment GetPageTitle() => builder =>
{
    builder.AddContent(0, "动态标题");
};
```

**优点：**
- ✅ 官方 API，稳定可靠
- ✅ 自动更新，无需手动调用
- ✅ 类型安全，编译时检查
- ✅ 代码简洁，易于维护

## 实现步骤

### 1. 在 `.razor` 文件中添加接口声明

```razor
@page "/warehouse/batch-edit"
@using AntDesign
@implements IReuseTabsPage

<PageTitle>仓库商品批量管理 - HB Platform</PageTitle>
```

### 2. 在 `.cs` 文件中实现 GetPageTitle() 方法

#### 方式一：简单文本标题

```csharp
public RenderFragment GetPageTitle() => builder =>
{
    builder.AddContent(0, "仓库商品批量管理");
};
```

#### 方式二：带图标和动态内容

```csharp
public RenderFragment GetPageTitle() => builder =>
{
    var seq = 0;
    
    // 图标
    builder.OpenComponent<Icon>(seq++);
    builder.AddAttribute(seq++, "Type", "database");
    builder.AddAttribute(seq++, "Theme", IconThemeType.Outline);
    builder.CloseComponent();
    
    // 标题文本
    builder.OpenElement(seq++, "span");
    builder.AddAttribute(seq++, "style", "margin-left: 4px;");
    builder.AddContent(seq++, "仓库商品批量管理");
    builder.CloseElement();
    
    // 动态数量标签
    if (pagedResult.TotalCount > 0)
    {
        builder.OpenComponent<Tag>(seq++);
        builder.AddAttribute(seq++, "Color", "blue");
        builder.AddAttribute(seq++, "Style", "margin-left: 6px;");
        builder.AddAttribute(seq++, "ChildContent", (RenderFragment)((b2) =>
        {
            b2.AddContent(0, pagedResult.TotalCount);
        }));
        builder.CloseComponent();
    }
};
```

### 3. 触发标签标题更新

标签标题会在以下情况自动更新：

1. **首次打开 Tab 时**：自动调用 `GetPageTitle()`
2. **路由参数变化时**：自动重新调用
3. **调用 StateHasChanged() 后**：触发重新渲染，标题也会更新

```csharp
private async Task HandleSearch()
{
    // 查询数据
    pagedResult = await Client.GetByFilterAsync(filterDto);
    
    // 触发更新（包括标签标题）
    StateHasChanged(); // ✅ 会自动调用 GetPageTitle()
}
```

### 4. 可选：使用 ReuseTabsService 手动更新

如果需要手动更新其他页面的标签：

```csharp
[Inject] private ReuseTabsService ReuseTabsService { get; set; } = default!;

protected override void OnInitialized()
{
    // 更新指定 URL 的标签标题
    ReuseTabsService.UpdatePage("/order/123", opt => 
    {
        opt.Title = "新标题";
    });
}
```

## 实际应用示例

### WarehouseProductBatchEdit 页面

**效果：** 标签标题显示为 "🗄️ 仓库商品批量管理 [1234]"

#### .razor 文件

```razor
@page "/warehouse/batch-edit"
@attribute [Authorize(Roles = "Admin,Warehouse")]
@using AntDesign
@implements IDisposable
@implements IReuseTabsPage

<PageTitle>仓库商品批量管理 - HB Platform</PageTitle>
```

#### .cs 文件

```csharp
public partial class WarehouseProductBatchEdit : ComponentBase, IDisposable
{
    [Inject] private ReuseTabsService ReuseTabsService { get; set; } = default!;
    
    // 实现 IReuseTabsPage 接口
    public RenderFragment GetPageTitle() => builder =>
    {
        var seq = 0;
        
        // Icon
        builder.OpenComponent<Icon>(seq++);
        builder.AddAttribute(seq++, "Type", "database");
        builder.AddAttribute(seq++, "Theme", IconThemeType.Outline);
        builder.CloseComponent();
        
        // 标题文本
        builder.OpenElement(seq++, "span");
        builder.AddAttribute(seq++, "style", "margin-left: 4px;");
        builder.AddContent(seq++, "仓库商品批量管理");
        builder.CloseElement();
        
        // 数量标签
        if (pagedResult.TotalCount > 0)
        {
            builder.OpenComponent<Tag>(seq++);
            builder.AddAttribute(seq++, "Color", "blue");
            builder.AddAttribute(seq++, "Style", "margin-left: 6px;");
            builder.AddAttribute(seq++, "ChildContent", (RenderFragment)((b2) =>
            {
                b2.AddContent(0, pagedResult.TotalCount);
            }));
            builder.CloseComponent();
        }
    };
    
    // 查询数据后自动更新标签
    private async Task HandleSearch()
    {
        try
        {
            loading = true;
            StateHasChanged();
            
            pagedResult = await Client.GetByFilterAsync(filterDto);
            
            Logger.LogInformation("查询成功，共 {Count} 条数据", pagedResult.TotalCount);
        }
        finally
        {
            loading = false;
            
            // ✅ StateHasChanged() 会触发 GetPageTitle() 重新调用
            StateHasChanged();
        }
    }
}
```

## 对比：ContainerDetail 页面

### 为什么 ContainerDetail 仍使用 JavaScript？

ContainerDetail 页面需要将 URL 中的 GUID（如 `7a8d3f2e-1b4c-...`）替换为真实的货柜号（如 `HLCU1234567`），这种场景：

1. **路由参数是 GUID**，不是最终显示的标题
2. **需要异步加载数据**后才能获取真实标题
3. **IReuseTabsPage 不能完全满足需求**（首次打开时 GUID 已显示在标签上）

因此 ContainerDetail 使用 **混合方案**：
- 实现 `IReuseTabsPage` 返回货柜号（数据加载后）
- 同时使用 JavaScript 立即更新已显示的 GUID 标签

### 推荐使用场景

| 页面类型 | 推荐方案 | 原因 |
|---------|---------|------|
| 列表页面 | ✅ IReuseTabsPage | 标题固定，可能显示数量 |
| 详情页面（参数是 ID/Code） | ✅ IReuseTabsPage | 直接使用参数作为标题 |
| 详情页面（参数是 GUID） | ⚠️ 混合方案 | 需要异步加载真实标题 |
| 表单页面 | ✅ IReuseTabsPage | 标题固定或基于参数 |

## 最佳实践

### 1. 优先使用 IReuseTabsPage

```csharp
// ✅ 推荐
@implements IReuseTabsPage

public RenderFragment GetPageTitle() => builder =>
{
    builder.AddContent(0, $"订单 #{OrderId}");
};
```

### 2. StateHasChanged() 自动触发更新

```csharp
// ✅ 推荐
private async Task LoadData()
{
    data = await LoadAsync();
    StateHasChanged(); // 会自动更新标签标题
}
```

### 3. 避免频繁更新

```csharp
// ❌ 不推荐：每次输入都更新
private void OnInput(string value)
{
    searchText = value;
    StateHasChanged(); // 会导致标签频繁闪烁
}

// ✅ 推荐：只在查询后更新
private async Task OnSearch()
{
    await HandleSearch();
    StateHasChanged(); // 只在查询完成后更新
}
```

### 4. 使用 RenderTreeBuilder 构建复杂 UI

```csharp
// ✅ 推荐：使用 RenderTreeBuilder
public RenderFragment GetPageTitle() => builder =>
{
    var seq = 0;
    builder.OpenComponent<Icon>(seq++);
    builder.AddAttribute(seq++, "Type", "icon-name");
    builder.CloseComponent();
};

// ❌ 不推荐：在 .cs 文件中使用 Razor 语法
public RenderFragment GetPageTitle() => @<Icon Type="icon-name" />; // 编译错误
```

## 性能优化

### 1. 避免在 GetPageTitle() 中执行复杂逻辑

```csharp
// ❌ 不推荐：每次渲染都计算
public RenderFragment GetPageTitle() => builder =>
{
    var count = ExpensiveCalculation(); // 耗时操作
    builder.AddContent(0, $"数据 ({count})");
};

// ✅ 推荐：提前计算并缓存
private int cachedCount = 0;

private async Task LoadData()
{
    data = await LoadAsync();
    cachedCount = data.Count; // 缓存计算结果
    StateHasChanged();
}

public RenderFragment GetPageTitle() => builder =>
{
    builder.AddContent(0, $"数据 ({cachedCount})");
};
```

### 2. 使用条件渲染

```csharp
// ✅ 推荐
public RenderFragment GetPageTitle() => builder =>
{
    builder.AddContent(0, "标题");
    
    if (showBadge) // 条件渲染，避免不必要的组件
    {
        builder.OpenComponent<Badge>(1);
        // ...
        builder.CloseComponent();
    }
};
```

## 故障排查

### 问题：标签标题不更新

**原因：** 没有调用 `StateHasChanged()`

**解决：**
```csharp
private async Task LoadData()
{
    data = await LoadAsync();
    StateHasChanged(); // ✅ 添加这行
}
```

### 问题：编译错误 "在 X 和 X 之间具有二义性"

**原因：** `ReuseTabsService` 在 `.razor` 和 `.cs` 文件中都注入了

**解决：**
```razor
<!-- ❌ 不要在 .razor 文件中注入 -->
@inject ReuseTabsService ReuseTabsService

<!-- ✅ 只在 .cs 文件中注入 -->
[Inject] private ReuseTabsService ReuseTabsService { get; set; } = default!;
```

### 问题：GetPageTitle() 中无法使用 Razor 语法

**原因：** `.cs` 文件不支持 Razor 语法

**解决：** 使用 `RenderTreeBuilder`
```csharp
// ❌ 错误
public RenderFragment GetPageTitle() => @<span>标题</span>;

// ✅ 正确
public RenderFragment GetPageTitle() => builder =>
{
    builder.OpenElement(0, "span");
    builder.AddContent(1, "标题");
    builder.CloseElement();
};
```

## 总结

### 核心要点

1. **实现 IReuseTabsPage 接口** 是官方推荐的动态更新标签标题的方式
2. **GetPageTitle() 返回 RenderFragment**，支持图标、标签、动态内容
3. **StateHasChanged() 会自动触发** 标签标题更新
4. **避免 JavaScript 操作 DOM**，除非有特殊需求（如 GUID → 真实标题）
5. **使用 RenderTreeBuilder** 在 `.cs` 文件中构建 UI

### 代码改进效果

| 指标 | 旧方案（DOM操作） | 新方案（IReuseTabsPage） |
|-----|----------------|----------------------|
| 代码行数 | ~30 行 | ~10 行 |
| 维护难度 | 高（JavaScript字符串） | 低（类型安全） |
| 稳定性 | 中（依赖DOM结构） | 高（官方API） |
| 性能 | 中（setTimeout延迟） | 高（同步更新） |
| 扩展性 | 低（难以添加组件） | 高（RenderFragment） |

### 参考文档

- [AntDesign Blazor ReuseTabs 官方文档](https://antblazor.com/zh-CN/experimental/reusetabs)
- [Blazor RenderFragment 文档](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/rendering)

---

**作者：** AI Assistant  
**日期：** 2025-10-05  
**版本：** 1.0  
**适用范围：** HB Platform 所有使用 ReuseTabs 的页面
