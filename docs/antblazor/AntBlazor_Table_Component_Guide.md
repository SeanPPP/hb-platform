# AntBlazor Table 组件完整指南

## 概述

AntBlazor Table 组件是一个功能强大的数据展示组件，用于展示行列数据，支持排序、搜索、分页、自定义操作等复杂行为。

## 何时使用

- 当有大量结构化的数据需要展现时
- 当需要对数据进行排序、搜索、分页、自定义操作等复杂行为时

## 基本使用方法

### 数据源配置
指定表格的数据源 `DataSource` 为一个数组，可用 OnChange 事件传入的查询状态进行分页和筛选。

### 两个主要数据列类型

#### 1. PropertyColumn
- 继承自 Column
- 用 `Property="c=>c.User.Name"` 绑定列
- 支持级联访问
- 在 .NET6 以下使用需指定 `TItem`, `TProp` 的类型

```razor
<PropertyColumn Property="c => c.UserName" Title="用户名" />
<PropertyColumn Property="c => c.User.Name" Title="姓名" />
```

#### 2. Column
- 用 `@bind-Field="context.UserName"` 绑定时不支持级联访问
- 可以用 `DataIndex="'User.Name'"` 绑定级联属性

```razor
<Column @bind-Field="context.UserName" Title="用户名" />
<Column DataIndex="User.Name" Title="姓名" />
```

### 其他列类型

#### ActionColumn
- 用于放置操作按钮
- 也可以作为不绑定类型的模板

```razor
<ActionColumn Title="操作">
    <Button Type="primary" Size="small" OnClick="() => Edit(context)">编辑</Button>
    <Button Type="primary" Danger Size="small" OnClick="() => Delete(context)">删除</Button>
</ActionColumn>
```

#### Selection
- 用来开启选择列
- 提供选择框功能

```razor
<Selection Key="@(context.Id.ToString())" />
```

## 核心功能

### 1. 基础表格

```razor
<Table DataSource="@data" TItem="UserModel">
    <PropertyColumn Property="c => c.Id" Title="ID" />
    <PropertyColumn Property="c => c.Name" Title="姓名" />
    <PropertyColumn Property="c => c.Email" Title="邮箱" />
    <ActionColumn Title="操作">
        <Button Type="primary" Size="small">编辑</Button>
    </ActionColumn>
</Table>
```

### 2. 行选择功能

```razor
<Table DataSource="@data" TItem="UserModel" 
       RowSelection="rowSelection" 
       @bind-SelectedRows="selectedRows">
    <!-- 列定义 -->
</Table>

@code {
    private UserModel[] selectedRows = Array.Empty<UserModel>();
    private TableRowSelection<UserModel> rowSelection = new()
    {
        Type = "checkbox",
        OnChange = (selectedRowKeys, selectedRows) =>
        {
            // 处理选择变化
        }
    };
}
```

### 3. 排序功能

```razor
<PropertyColumn Property="c => c.CreateTime" 
                Title="创建时间" 
                Sortable 
                DefaultSortOrder="SortDirection.Descending" />
```

### 4. 筛选功能

#### 内置筛选器
```razor
<PropertyColumn Property="c => c.Status" 
                Title="状态" 
                Filterable />
```

#### 自定义筛选器
```razor
<PropertyColumn Property="c => c.Category" 
                Title="分类" 
                Filters="categoryFilters"
                OnFilter="(value, record) => record.Category == value?.ToString()" />

@code {
    private TableFilter[] categoryFilters = new[]
    {
        new TableFilter { Text = "分类1", Value = "category1" },
        new TableFilter { Text = "分类2", Value = "category2" }
    };
}
```

#### 高级过滤器操作符获取
如需获取用户在过滤面板中选择的具体操作符（如包含、等于、开始于等），可以通过分析`ITableFilterModel`的内部结构实现：

```csharp
private FilterOperator GetUserSelectedOperator(ITableFilterModel filter)
{
    // 分析Filters集合获取用户选择的操作符
    var filtersProperty = filter.GetType().GetProperty("Filters");
    if (filtersProperty?.GetValue(filter) is System.Collections.IEnumerable filters)
    {
        // 遍历过滤器项目查找操作符信息
        // 详细实现请参考: Table_Filter_Advanced_Analysis.md
    }
    return FilterOperator.Contains; // 默认值
}
```

> 💡 **提示**: 关于过滤器内部结构的深度分析和操作符获取的完整实现，请参考 [Table 过滤器高级分析技术文档](./Table_Filter_Advanced_Analysis.md)

### 5. 固定列功能

```razor
<PropertyColumn Property="c => c.Id" 
                Title="ID" 
                Fixed="left" 
                Width="80" />
<PropertyColumn Property="c => c.Name" 
                Title="姓名" 
                Width="120" />
<ActionColumn Title="操作" 
              Fixed="right" 
              Width="150">
    <!-- 操作按钮 -->
</ActionColumn>
```

### 6. 分页配置

```razor
<Table DataSource="@data" 
       TItem="UserModel"
       Total="@total"
       PageIndex="@pageIndex"
       PageSize="@pageSize"
       OnChange="@OnTableChange">
    <!-- 列定义 -->
</Table>

@code {
    private int total = 0;
    private int pageIndex = 1;
    private int pageSize = 10;
    
    private async Task OnTableChange(QueryModel<UserModel> queryModel)
    {
        pageIndex = queryModel.PageIndex;
        pageSize = queryModel.PageSize;
        // 加载数据逻辑
        await LoadData();
    }
}
```

### 7. 远程数据加载

```razor
<Table DataSource="@data" 
       TItem="UserModel"
       Loading="@loading"
       Total="@total"
       OnChange="@OnTableChange">
    <!-- 列定义 -->
</Table>

@code {
    private bool loading = false;
    private UserModel[] data = Array.Empty<UserModel>();
    
    private async Task OnTableChange(QueryModel<UserModel> queryModel)
    {
        loading = true;
        try
        {
            // 构建查询参数
            var result = await userService.GetUsersAsync(new UserQueryDto
            {
                PageIndex = queryModel.PageIndex,
                PageSize = queryModel.PageSize,
                SortField = queryModel.SortModel.FirstOrDefault()?.FieldName,
                SortOrder = queryModel.SortModel.FirstOrDefault()?.Sort?.ToString(),
                Filters = queryModel.FilterModel.ToDictionary(f => f.FieldName, f => f.FilterValue)
            });
            
            data = result.Data;
            total = result.Total;
        }
        finally
        {
            loading = false;
        }
    }
}
```

## 高级功能

### 1. 可展开行

```razor
<Table DataSource="@data" 
       TItem="OrderModel"
       ExpandTemplate="expandTemplate">
    <!-- 列定义 -->
</Table>

@code {
    private RenderFragment<OrderModel> expandTemplate = (order) => @<div>
        <p>订单详情：@order.Description</p>
        <Table DataSource="@order.Items" TItem="OrderItemModel" Size="small">
            <PropertyColumn Property="c => c.ProductName" Title="产品名称" />
            <PropertyColumn Property="c => c.Quantity" Title="数量" />
            <PropertyColumn Property="c => c.Price" Title="价格" />
        </Table>
    </div>;
}
```

### 2. 可编辑单元格

```razor
<Table DataSource="@data" TItem="UserModel">
    <PropertyColumn Property="c => c.Name" Title="姓名">
        @if (editingKey == context.Id.ToString())
        {
            <Input @bind-Value="editingRecord.Name" />
        }
        else
        {
            @context.Name
        }
    </PropertyColumn>
    <ActionColumn Title="操作">
        @if (editingKey == context.Id.ToString())
        {
            <Button Type="link" Size="small" OnClick="() => Save(context)">保存</Button>
            <Button Type="link" Size="small" OnClick="Cancel">取消</Button>
        }
        else
        {
            <Button Type="link" Size="small" OnClick="() => Edit(context)">编辑</Button>
        }
    </ActionColumn>
</Table>

@code {
    private string editingKey = "";
    private UserModel editingRecord = new();
    
    private void Edit(UserModel record)
    {
        editingKey = record.Id.ToString();
        editingRecord = new UserModel { /* 复制记录 */ };
    }
    
    private async Task Save(UserModel record)
    {
        // 保存逻辑
        await userService.UpdateAsync(editingRecord);
        editingKey = "";
        await LoadData();
    }
    
    private void Cancel()
    {
        editingKey = "";
    }
}
```

### 3. 虚拟滚动

```razor
<Table DataSource="@data" 
       TItem="UserModel"
       Virtual="true"
       ScrollY="400">
    <!-- 列定义 -->
</Table>
```

### 4. 表头分组

```razor
<Table DataSource="@data" TItem="UserModel">
    <ColumnGroup Title="基本信息">
        <PropertyColumn Property="c => c.Name" Title="姓名" />
        <PropertyColumn Property="c => c.Email" Title="邮箱" />
    </ColumnGroup>
    <ColumnGroup Title="统计信息">
        <PropertyColumn Property="c => c.LoginCount" Title="登录次数" />
        <PropertyColumn Property="c => c.LastLoginTime" Title="最后登录" />
    </ColumnGroup>
</Table>
```

## API 参考

### Table 主要属性

| 属性 | 说明 | 类型 | 默认值 |
|------|------|------|--------|
| DataSource | 数据数组 | TItem[] | - |
| Loading | 页面是否加载中 | bool | false |
| Size | 表格大小 | TableSize | default |
| Bordered | 是否展示外边框和列边框 | bool | false |
| ShowHeader | 是否显示表头 | bool | true |
| Total | 数据总数 | int | - |
| PageIndex | 当前页数 | int | 1 |
| PageSize | 每页条数 | int | 10 |
| HidePagination | 隐藏分页 | bool | false |
| PaginationPosition | 分页位置 | string | "bottomRight" |
| ScrollX | 设置横向滚动，也可用于指定滚动区域的宽 | string | - |
| ScrollY | 设置纵向滚动，也可用于指定滚动区域的高 | string | - |
| RowSelection | 行选择配置 | TableRowSelection | - |
| ExpandTemplate | 展开行模板 | RenderFragment<TItem> | - |
| OnChange | 分页、排序、筛选变化时触发 | EventCallback<QueryModel<TItem>> | - |
| OnRow | 设置行属性 | Func<TItem, Dictionary<string, object>> | - |

### Column 主要属性

| 属性 | 说明 | 类型 | 默认值 |
|------|------|------|--------|
| Title | 列头显示文字 | string | - |
| DataIndex | 列数据在数据项中对应的路径 | string | - |
| Width | 列宽度 | string \| number | - |
| Fixed | 列是否固定 | "left" \| "right" | - |
| Sortable | 是否可排序 | bool | false |
| DefaultSortOrder | 默认排序顺序 | SortDirection? | - |
| Filterable | 是否可筛选 | bool | false |
| Filters | 表头的筛选菜单项 | TableFilter[] | - |
| OnFilter | 本地模式下，确定筛选的运行函数 | Func<object, TItem, bool> | - |
| Ellipsis | 超过宽度将自动省略 | bool | false |
| Align | 设置列内容的对齐方式 | "left" \| "right" \| "center" | "left" |

## 最佳实践

### 1. 性能优化

```razor
@code {
    // 使用 ShouldRender 优化渲染
    protected override bool ShouldRender()
    {
        return dataChanged;
    }
    
    // 大数据量时启用虚拟滚动
    private bool shouldUseVirtualScroll => data.Length > 1000;
}
```

### 2. 错误处理

```razor
@code {
    private async Task OnTableChange(QueryModel<UserModel> queryModel)
    {
        loading = true;
        try
        {
            await LoadData(queryModel);
        }
        catch (Exception ex)
        {
            await message.Error($"加载数据失败: {ex.Message}");
        }
        finally
        {
            loading = false;
        }
    }
}
```

### 3. 响应式设计

```razor
<Table DataSource="@data" 
       TItem="UserModel"
       ScrollX="800"
       Size="@(isSmallScreen ? TableSize.Small : TableSize.Default)">
    <!-- 列定义 -->
</Table>

@code {
    private bool isSmallScreen = false;
    
    protected override async Task OnInitializedAsync()
    {
        // 检测屏幕尺寸
        isSmallScreen = await JSRuntime.InvokeAsync<bool>("window.innerWidth < 768");
    }
}
```

## 常见问题

### 1. OnChange 为什么会被调用两次？
Table 组件支持预渲染，在服务端渲染阶段会调用 OnChange 查询数据。当浏览器上的 Blazor 实例启动时，会再次调用一次 OnChange。

### 2. 如何避免在首次加载时触发 OnChange？
设置 PageIndex = 0，表示不希望 Table 自动加载数据。

### 3. 固定列不生效？
确保设置了 ScrollX 属性，并且列宽度设置合理。

### 4. 数据更新后表格不刷新？
确保调用 StateHasChanged() 或使用双向绑定。

## 示例项目集成

在我们的 HB Platform 多店铺管理系统中，可以这样使用：

```razor
@page "/orders"
@using BlazorApp.Shared.DTOs
@inject IOrderServiceClient OrderService

<Table DataSource="@orders" 
       TItem="OrderDto"
       Loading="@loading"
       Total="@total"
       PageIndex="@pageIndex"
       PageSize="@pageSize"
       OnChange="@OnTableChange"
       RowSelection="@rowSelection"
       @bind-SelectedRows="@selectedOrders">
    
    <Selection Key="@(context.Id.ToString())" />
    
    <PropertyColumn Property="c => c.OrderNumber" 
                    Title="订单号" 
                    Fixed="left" 
                    Width="120" />
    
    <PropertyColumn Property="c => c.StoreName" 
                    Title="店铺" 
                    Filterable />
    
    <PropertyColumn Property="c => c.CustomerName" 
                    Title="客户" />
    
    <PropertyColumn Property="c => c.TotalAmount" 
                    Title="总金额" 
                    Sortable>
        ¥@context.TotalAmount.ToString("F2")
    </PropertyColumn>
    
    <PropertyColumn Property="c => c.Status" 
                    Title="状态" 
                    Filters="@statusFilters"
                    OnFilter="@((value, record) => record.Status == value?.ToString())">
        <Tag Color="@GetStatusColor(context.Status)">@context.Status</Tag>
    </PropertyColumn>
    
    <PropertyColumn Property="c => c.CreateTime" 
                    Title="创建时间" 
                    Sortable 
                    DefaultSortOrder="SortDirection.Descending">
        @context.CreateTime.ToString("yyyy-MM-dd HH:mm")
    </PropertyColumn>
    
    <ActionColumn Title="操作" Fixed="right" Width="150">
        <Button Type="link" Size="small" OnClick="() => ViewOrder(context)">查看</Button>
        <Button Type="link" Size="small" OnClick="() => EditOrder(context)">编辑</Button>
        <Popconfirm Title="确定删除此订单吗？" 
                    OnConfirm="() => DeleteOrder(context)"
                    OkText="确定" 
                    CancelText="取消">
            <Button Type="link" Danger Size="small">删除</Button>
        </Popconfirm>
    </ActionColumn>
</Table>
```

这个文档涵盖了 AntBlazor Table 组件的核心功能和最佳实践，可以作为项目开发的参考指南。