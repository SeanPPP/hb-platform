# HB Platform 表格组件使用规范

## 项目标准

基于 AntBlazor Table 组件，制定适用于 HB Platform 多店铺管理系统的表格使用规范。

## 通用模板

### 基础列表页模板

```razor
@page "/example-list"
@using BlazorApp.Shared.DTOs
@inject IExampleServiceClient ExampleService
@inject IMessageService MessageService
@inject NavigationManager Navigation

<AuthGuard>
    <PageTitle>示例列表 - HB Platform</PageTitle>
    
    <div class="page-container">
        <div class="page-header">
            <h1 class="page-title">示例管理</h1>
            <div class="page-actions">
                <Space>
                    <SpaceItem>
                        <Button Type="default" OnClick="RefreshData">
                            <Icon Type="reload" /> 刷新
                        </Button>
                    </SpaceItem>
                    <SpaceItem>
                        <Button Type="primary" OnClick="ShowCreateModal">
                            <Icon Type="plus" /> 新建
                        </Button>
                    </SpaceItem>
                </Space>
            </div>
        </div>
        
        <!-- 搜索区域 -->
        <Card Class="search-card" Style="margin-bottom: 16px;">
            <Form Layout="inline" OnFinish="SearchData">
                <FormItem Label="名称">
                    <Input @bind-Value="searchModel.Name" Placeholder="请输入名称" />
                </FormItem>
                <FormItem Label="状态">
                    <Select @bind-Value="searchModel.Status" Placeholder="请选择状态" AllowClear>
                        <SelectOption Value="Active">启用</SelectOption>
                        <SelectOption Value="Inactive">禁用</SelectOption>
                    </Select>
                </FormItem>
                <FormItem>
                    <Space>
                        <SpaceItem>
                            <Button Type="primary" HtmlType="submit">
                                <Icon Type="search" /> 查询
                            </Button>
                        </SpaceItem>
                        <SpaceItem>
                            <Button OnClick="ResetSearch">
                                <Icon Type="undo" /> 重置
                            </Button>
                        </SpaceItem>
                    </Space>
                </FormItem>
            </Form>
        </Card>
        
        <!-- 批量操作区域 -->
        @if (selectedRows.Any())
        {
            <Alert Type="info" 
                   Message="@($"已选择 {selectedRows.Length} 项")" 
                   ShowIcon 
                   Style="margin-bottom: 16px;">
                <MessageTemplate>
                    <span>已选择 <strong>@selectedRows.Length</strong> 项</span>
                    <Divider Type="vertical" />
                    <Button Type="link" Size="small" OnClick="BatchDelete">
                        批量删除
                    </Button>
                    <Button Type="link" Size="small" OnClick="BatchExport">
                        批量导出
                    </Button>
                    <Button Type="link" Size="small" OnClick="ClearSelection">
                        清除选择
                    </Button>
                </MessageTemplate>
            </Alert>
        }
        
        <!-- 主表格 -->
        <Card>
            <Table DataSource="@dataSource" 
                   TItem="ExampleDto"
                   Loading="@loading"
                   Total="@total"
                   PageIndex="@pageIndex"
                   PageSize="@pageSize"
                   OnChange="@OnTableChange"
                   RowSelection="@rowSelection"
                   @bind-SelectedRows="@selectedRows"
                   ScrollX="1400"
                   Size="middle">
                
                <!-- 选择列 -->
                <Selection Key="@(context.Id.ToString())" Fixed="left" Width="50" />
                
                <!-- 序号列 -->
                <Column Title="序号" Fixed="left" Width="70" Align="center">
                    @(((pageIndex - 1) * pageSize) + dataSource.ToList().IndexOf(context) + 1)
                </Column>
                
                <!-- 基础信息列 -->
                <PropertyColumn Property="c => c.Name" 
                                Title="名称" 
                                Fixed="left" 
                                Width="150" 
                                Sortable 
                                Filterable />
                
                <PropertyColumn Property="c => c.Code" 
                                Title="编码" 
                                Width="120" 
                                Sortable />
                
                <PropertyColumn Property="c => c.Category" 
                                Title="分类" 
                                Width="120" 
                                Filters="@categoryFilters"
                                OnFilter="@((value, record) => record.Category == value?.ToString())" />
                
                <PropertyColumn Property="c => c.Description" 
                                Title="描述" 
                                Width="200" 
                                Ellipsis="true" />
                
                <PropertyColumn Property="c => c.CreatedBy" 
                                Title="创建人" 
                                Width="100" />
                
                <PropertyColumn Property="c => c.CreatedTime" 
                                Title="创建时间" 
                                Width="160" 
                                Sortable 
                                DefaultSortOrder="SortDirection.Descending">
                    @context.CreatedTime.ToString("yyyy-MM-dd HH:mm")
                </PropertyColumn>
                
                <!-- 状态列 -->
                <PropertyColumn Property="c => c.Status" 
                                Title="状态" 
                                Fixed="right" 
                                Width="100" 
                                Align="center">
                    <Tag Color="@GetStatusColor(context.Status)">
                        @GetStatusText(context.Status)
                    </Tag>
                </PropertyColumn>
                
                <!-- 操作列 -->
                <ActionColumn Title="操作" Fixed="right" Width="150" Align="center">
                    <Space>
                        <SpaceItem>
                            <Tooltip Title="查看详情">
                                <Button Type="link" Size="small" OnClick="() => ViewDetail(context)">
                                    <Icon Type="eye" />
                                </Button>
                            </Tooltip>
                        </SpaceItem>
                        <SpaceItem>
                            <Tooltip Title="编辑">
                                <Button Type="link" Size="small" OnClick="() => Edit(context)">
                                    <Icon Type="edit" />
                                </Button>
                            </Tooltip>
                        </SpaceItem>
                        <SpaceItem>
                            <Dropdown>
                                <Overlay>
                                    <Menu>
                                        <MenuItem OnClick="() => Duplicate(context)">
                                            <Icon Type="copy" /> 复制
                                        </MenuItem>
                                        <MenuItem OnClick="() => Export(context)">
                                            <Icon Type="download" /> 导出
                                        </MenuItem>
                                        @if (context.Status == "Active")
                                        {
                                            <MenuItem OnClick="() => ToggleStatus(context)">
                                                <Icon Type="stop" /> 禁用
                                            </MenuItem>
                                        }
                                        else
                                        {
                                            <MenuItem OnClick="() => ToggleStatus(context)">
                                                <Icon Type="play-circle" /> 启用
                                            </MenuItem>
                                        }
                                        <MenuDivider />
                                        <MenuItem OnClick="() => Delete(context)" Danger>
                                            <Icon Type="delete" /> 删除
                                        </MenuItem>
                                    </Menu>
                                </Overlay>
                                <ChildContent>
                                    <Button Type="link" Size="small">
                                        <Icon Type="more" />
                                    </Button>
                                </ChildContent>
                            </Dropdown>
                        </SpaceItem>
                    </Space>
                </ActionColumn>
                
            </Table>
        </Card>
    </div>
    
</AuthGuard>

@code {
    // 数据源
    private ExampleDto[] dataSource = Array.Empty<ExampleDto>();
    private ExampleDto[] selectedRows = Array.Empty<ExampleDto>();
    
    // 分页参数
    private bool loading = false;
    private int total = 0;
    private int pageIndex = 1;
    private int pageSize = 10;
    
    // 搜索参数
    private SearchModel searchModel = new();
    
    // 筛选器
    private TableFilter[] categoryFilters = new[]
    {
        new TableFilter { Text = "分类A", Value = "CategoryA" },
        new TableFilter { Text = "分类B", Value = "CategoryB" },
        new TableFilter { Text = "分类C", Value = "CategoryC" }
    };
    
    // 行选择配置
    private TableRowSelection<ExampleDto> rowSelection = new()
    {
        Type = "checkbox",
        Fixed = true,
        OnChange = (selectedRowKeys, selectedRows) =>
        {
            // 处理选择变化
        }
    };
    
    // 生命周期
    protected override async Task OnInitializedAsync()
    {
        await LoadData();
    }
    
    // 数据加载
    private async Task LoadData()
    {
        loading = true;
        try
        {
            var result = await ExampleService.GetPagedAsync(new ExampleQueryDto
            {
                PageIndex = pageIndex,
                PageSize = pageSize,
                Name = searchModel.Name,
                Status = searchModel.Status
            });
            
            dataSource = result.Data;
            total = result.Total;
        }
        catch (Exception ex)
        {
            await MessageService.Error($"加载数据失败: {ex.Message}");
        }
        finally
        {
            loading = false;
        }
    }
    
    // 表格变化事件
    private async Task OnTableChange(QueryModel<ExampleDto> queryModel)
    {
        pageIndex = queryModel.PageIndex;
        pageSize = queryModel.PageSize;
        
        // 处理排序
        if (queryModel.SortModel.Any())
        {
            var sortModel = queryModel.SortModel.First();
            searchModel.SortField = sortModel.FieldName;
            searchModel.SortOrder = sortModel.Sort?.ToString();
        }
        
        // 处理筛选
        foreach (var filter in queryModel.FilterModel)
        {
            switch (filter.FieldName)
            {
                case nameof(ExampleDto.Name):
                    searchModel.Name = filter.FilterValue?.ToString();
                    break;
                case nameof(ExampleDto.Category):
                    searchModel.Category = filter.FilterValue?.ToString();
                    break;
            }
        }
        
        await LoadData();
    }
    
    // 搜索功能
    private async Task SearchData()
    {
        pageIndex = 1; // 重置到第一页
        await LoadData();
    }
    
    private void ResetSearch()
    {
        searchModel = new SearchModel();
        pageIndex = 1;
        _ = LoadData();
    }
    
    // 操作方法
    private void ViewDetail(ExampleDto item)
    {
        Navigation.NavigateTo($"/example/detail/{item.Id}");
    }
    
    private void Edit(ExampleDto item)
    {
        Navigation.NavigateTo($"/example/edit/{item.Id}");
    }
    
    private async Task Delete(ExampleDto item)
    {
        var confirmed = await MessageService.Confirm("确定删除此项吗？");
        if (confirmed)
        {
            try
            {
                await ExampleService.DeleteAsync(item.Id);
                await MessageService.Success("删除成功");
                await LoadData();
            }
            catch (Exception ex)
            {
                await MessageService.Error($"删除失败: {ex.Message}");
            }
        }
    }
    
    // 工具方法
    private string GetStatusColor(string status) => status switch
    {
        "Active" => "success",
        "Inactive" => "default",
        "Pending" => "processing",
        "Error" => "error",
        _ => "default"
    };
    
    private string GetStatusText(string status) => status switch
    {
        "Active" => "启用",
        "Inactive" => "禁用",
        "Pending" => "待处理",
        "Error" => "错误",
        _ => status
    };
    
    // 搜索模型
    public class SearchModel
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string Category { get; set; } = "";
        public string SortField { get; set; } = "";
        public string SortOrder { get; set; } = "";
    }
}
```

## 列配置规范

### 1. 固定列配置

```razor
<!-- 左侧固定列：重要标识信息 -->
<Selection Key="@(context.Id.ToString())" Fixed="left" Width="50" />
<Column Title="序号" Fixed="left" Width="70" Align="center">...</Column>
<PropertyColumn Property="c => c.Name" Fixed="left" Width="150" />

<!-- 右侧固定列：状态和操作 -->
<PropertyColumn Property="c => c.Status" Fixed="right" Width="100" />
<ActionColumn Title="操作" Fixed="right" Width="150" />
```

### 2. 列宽度规范

| 列类型 | 推荐宽度 | 说明 |
|--------|----------|------|
| 选择框 | 50px | 固定宽度 |
| 序号 | 70px | 固定宽度 |
| ID/编码 | 80-120px | 根据内容长度 |
| 名称 | 120-200px | 主要信息列 |
| 状态 | 80-100px | 标签显示 |
| 时间 | 160px | 完整时间显示 |
| 操作 | 120-200px | 根据按钮数量 |
| 描述 | 200-300px | 启用省略号 |

### 3. 排序配置

```razor
<!-- 默认排序：通常按创建时间倒序 -->
<PropertyColumn Property="c => c.CreatedTime" 
                Sortable 
                DefaultSortOrder="SortDirection.Descending" />

<!-- 常用排序字段 -->
<PropertyColumn Property="c => c.Name" Sortable />
<PropertyColumn Property="c => c.Code" Sortable />
<PropertyColumn Property="c => c.UpdatedTime" Sortable />
```

### 4. 筛选配置

```razor
<!-- 文本筛选：启用内置筛选器 -->
<PropertyColumn Property="c => c.Name" Filterable />

<!-- 枚举筛选：自定义筛选器 -->
<PropertyColumn Property="c => c.Status" 
                Filters="@statusFilters"
                OnFilter="@((value, record) => record.Status == value?.ToString())" />
```

## 状态显示规范

### 通用状态映射

```csharp
private string GetStatusColor(string status) => status switch
{
    "Active" or "Enabled" or "Success" => "success",      // 绿色
    "Inactive" or "Disabled" => "default",                // 灰色  
    "Pending" or "Processing" => "processing",            // 蓝色
    "Warning" => "warning",                               // 橙色
    "Error" or "Failed" => "error",                       // 红色
    "Draft" => "default",                                 // 灰色
    _ => "default"
};

private string GetStatusText(string status) => status switch
{
    "Active" => "启用",
    "Inactive" => "禁用", 
    "Pending" => "待处理",
    "Processing" => "处理中",
    "Success" => "成功",
    "Error" => "错误",
    "Failed" => "失败",
    "Draft" => "草稿",
    _ => status
};
```

## 操作按钮规范

### 标准操作组合

```razor
<ActionColumn Title="操作" Fixed="right" Width="150">
    <Space>
        <!-- 主要操作：直接显示 -->
        <SpaceItem>
            <Tooltip Title="查看">
                <Button Type="link" Size="small" OnClick="() => View(context)">
                    <Icon Type="eye" />
                </Button>
            </Tooltip>
        </SpaceItem>
        <SpaceItem>
            <Tooltip Title="编辑">
                <Button Type="link" Size="small" OnClick="() => Edit(context)">
                    <Icon Type="edit" />
                </Button>
            </Tooltip>
        </SpaceItem>
        
        <!-- 次要操作：下拉菜单 -->
        <SpaceItem>
            <Dropdown>
                <Overlay>
                    <Menu>
                        <MenuItem OnClick="() => Copy(context)">
                            <Icon Type="copy" /> 复制
                        </MenuItem>
                        <MenuItem OnClick="() => Export(context)">
                            <Icon Type="download" /> 导出
                        </MenuItem>
                        <MenuDivider />
                        <MenuItem OnClick="() => Delete(context)" Danger>
                            <Icon Type="delete" /> 删除
                        </MenuItem>
                    </Menu>
                </Overlay>
                <ChildContent>
                    <Button Type="link" Size="small">
                        <Icon Type="more" />
                    </Button>
                </ChildContent>
            </Dropdown>
        </SpaceItem>
    </Space>
</ActionColumn>
```

### 权限控制

```razor
<ActionColumn Title="操作" Fixed="right" Width="150">
    <Space>
        <!-- 查看权限：所有人都有 -->
        <SpaceItem>
            <Button Type="link" Size="small" OnClick="() => View(context)">
                查看
            </Button>
        </SpaceItem>
        
        <!-- 编辑权限：需要权限检查 -->
        @if (HasPermission("Edit"))
        {
            <SpaceItem>
                <Button Type="link" Size="small" OnClick="() => Edit(context)">
                    编辑
                </Button>
            </SpaceItem>
        }
        
        <!-- 删除权限：需要高级权限 -->
        @if (HasPermission("Delete"))
        {
            <SpaceItem>
                <Popconfirm Title="确定删除吗？" OnConfirm="() => Delete(context)">
                    <Button Type="link" Size="small" Danger>
                        删除
                    </Button>
                </Popconfirm>
            </SpaceItem>
        }
    </Space>
</ActionColumn>
```

## 响应式适配

### 移动端优化

```razor
@code {
    private bool isMobile = false;
    private string tableScrollX => isMobile ? "800" : "1400";
    private TableSize tableSize => isMobile ? TableSize.Small : TableSize.Middle;
}

<Table ScrollX="@tableScrollX" Size="@tableSize">
    <!-- 移动端隐藏次要列 -->
    <PropertyColumn Property="c => c.Description" 
                    Title="描述" 
                    Hidden="@isMobile" />
    
    <!-- 移动端简化操作 -->
    <ActionColumn Title="操作" Fixed="right" Width="@(isMobile ? "80" : "150")">
        @if (isMobile)
        {
            <Dropdown>
                <Overlay>
                    <Menu>
                        <MenuItem OnClick="() => View(context)">查看</MenuItem>
                        <MenuItem OnClick="() => Edit(context)">编辑</MenuItem>
                        <MenuItem OnClick="() => Delete(context)" Danger>删除</MenuItem>
                    </Menu>
                </Overlay>
                <ChildContent>
                    <Button Size="small"><Icon Type="more" /></Button>
                </ChildContent>
            </Dropdown>
        }
        else
        {
            <!-- 桌面端完整操作 -->
        }
    </ActionColumn>
</Table>
```

## 性能优化

### 大数据量处理

```razor
<!-- 启用虚拟滚动 -->
<Table DataSource="@data" 
       Virtual="@(data.Length > 1000)"
       ScrollY="400" />

<!-- 分页配置 -->
<Table PageSize="@(data.Length > 10000 ? 50 : 20)" />

<!-- 延迟加载 -->
@code {
    private async Task OnTableChange(QueryModel<T> queryModel)
    {
        // 防抖处理
        await Task.Delay(300);
        if (cancellationTokenSource?.IsCancellationRequested == false)
        {
            await LoadData(queryModel);
        }
    }
}
```

## 错误处理

### 统一错误处理

```csharp
private async Task SafeExecute(Func<Task> action, string errorMessage = "操作失败")
{
    try
    {
        loading = true;
        await action();
    }
    catch (ApiException ex)
    {
        await MessageService.Error(ex.Message ?? errorMessage);
    }
    catch (Exception ex)
    {
        await MessageService.Error($"{errorMessage}: {ex.Message}");
        // 记录详细错误日志
        Logger.LogError(ex, errorMessage);
    }
    finally
    {
        loading = false;
    }
}

// 使用示例
private async Task Delete(ExampleDto item)
{
    await SafeExecute(async () =>
    {
        await ExampleService.DeleteAsync(item.Id);
        await MessageService.Success("删除成功");
        await LoadData();
    }, "删除失败");
}
```

## 样式规范

### CSS 类命名

```css
/* 页面容器 */
.page-container {
    padding: 24px;
}

.page-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 24px;
}

.page-title {
    margin: 0;
    font-size: 20px;
    font-weight: 500;
}

/* 搜索区域 */
.search-card .ant-card-body {
    padding: 16px 24px;
}

/* 表格定制 */
.table-container .ant-table-thead > tr > th {
    background: #fafafa;
    font-weight: 500;
}

.table-container .ant-table-tbody > tr:hover > td {
    background: #e6f7ff;
}
```

通过遵循这些规范，可以确保 HB Platform 中所有表格组件的一致性和用户体验。