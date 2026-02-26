# AntBlazor Table 固定列功能实现指南

## 概述

固定列功能允许在表格水平滚动时保持某些重要列（如操作列、ID列）始终可见，提升用户体验。

## 基本概念

### 固定位置
- `Fixed="left"`: 固定在左侧
- `Fixed="right"`: 固定在右侧
- 不设置 `Fixed`: 普通可滚动列

### 必要条件
1. **必须设置 `ScrollX`**: 表格需要水平滚动才能触发固定列效果
2. **合理的列宽设置**: 确保固定列和普通列的宽度设置合理
3. **容器宽度限制**: 表格容器宽度小于所有列宽度之和

## 基础实现

### 简单固定列示例

```razor
<Table DataSource="@users" 
       TItem="UserModel"
       ScrollX="1200"
       Style="width: 800px;">
    
    <!-- 左侧固定列 -->
    <PropertyColumn Property="c => c.Id" 
                    Title="ID" 
                    Fixed="left" 
                    Width="80" />
    
    <PropertyColumn Property="c => c.Avatar" 
                    Title="头像" 
                    Fixed="left" 
                    Width="80">
        <Avatar Src="@context.Avatar" />
    </PropertyColumn>
    
    <!-- 普通可滚动列 -->
    <PropertyColumn Property="c => c.Name" 
                    Title="姓名" 
                    Width="120" />
    
    <PropertyColumn Property="c => c.Email" 
                    Title="邮箱" 
                    Width="200" />
    
    <PropertyColumn Property="c => c.Department" 
                    Title="部门" 
                    Width="150" />
    
    <PropertyColumn Property="c => c.Position" 
                    Title="职位" 
                    Width="120" />
    
    <PropertyColumn Property="c => c.Phone" 
                    Title="电话" 
                    Width="150" />
    
    <PropertyColumn Property="c => c.Address" 
                    Title="地址" 
                    Width="250" />
    
    <!-- 右侧固定列 -->
    <PropertyColumn Property="c => c.Status" 
                    Title="状态" 
                    Fixed="right" 
                    Width="100">
        <Tag Color="@(context.Status == "Active" ? "green" : "red")">
            @context.Status
        </Tag>
    </PropertyColumn>
    
    <ActionColumn Title="操作" 
                  Fixed="right" 
                  Width="150">
        <Space>
            <SpaceItem>
                <Button Type="link" Size="small" OnClick="() => Edit(context)">
                    编辑
                </Button>
            </SpaceItem>
            <SpaceItem>
                <Button Type="link" Size="small" Danger OnClick="() => Delete(context)">
                    删除
                </Button>
            </SpaceItem>
        </Space>
    </ActionColumn>
    
</Table>
```

## 高级实现

### 响应式固定列

```razor
<Table DataSource="@orders" 
       TItem="OrderModel"
       ScrollX="@scrollX"
       Style="@tableStyle">
    
    <!-- 基础固定列 - 在所有屏幕尺寸下都固定 -->
    <PropertyColumn Property="c => c.OrderNumber" 
                    Title="订单号" 
                    Fixed="left" 
                    Width="120" />
    
    <!-- 条件固定列 - 只在大屏幕下固定 -->
    <PropertyColumn Property="c => c.CustomerName" 
                    Title="客户姓名" 
                    Fixed="@(isLargeScreen ? "left" : null)" 
                    Width="150" />
    
    <!-- 普通列 -->
    <PropertyColumn Property="c => c.ProductName" Title="产品" Width="200" />
    <PropertyColumn Property="c => c.Quantity" Title="数量" Width="100" />
    <PropertyColumn Property="c => c.UnitPrice" Title="单价" Width="120" />
    <PropertyColumn Property="c => c.TotalAmount" Title="总金额" Width="120" />
    <PropertyColumn Property="c => c.OrderDate" Title="订单日期" Width="150" />
    <PropertyColumn Property="c => c.DeliveryDate" Title="交付日期" Width="150" />
    
    <!-- 操作列 - 始终固定在右侧 -->
    <ActionColumn Title="操作" 
                  Fixed="right" 
                  Width="@actionColumnWidth">
        <Space>
            <SpaceItem>
                <Button Type="primary" Size="small" OnClick="() => ViewDetail(context)">
                    查看
                </Button>
            </SpaceItem>
            @if (isLargeScreen)
            {
                <SpaceItem>
                    <Button Type="default" Size="small" OnClick="() => Edit(context)">
                        编辑
                    </Button>
                </SpaceItem>
                <SpaceItem>
                    <Dropdown>
                        <Overlay>
                            <Menu>
                                <MenuItem OnClick="() => Duplicate(context)">复制</MenuItem>
                                <MenuItem OnClick="() => Export(context)">导出</MenuItem>
                                <MenuDivider />
                                <MenuItem OnClick="() => Delete(context)" Danger>删除</MenuItem>
                            </Menu>
                        </Overlay>
                        <ChildContent>
                            <Button Size="small">
                                更多 <Icon Type="down" />
                            </Button>
                        </ChildContent>
                    </Dropdown>
                </SpaceItem>
            }
            else
            {
                <SpaceItem>
                    <Dropdown>
                        <Overlay>
                            <Menu>
                                <MenuItem OnClick="() => Edit(context)">编辑</MenuItem>
                                <MenuItem OnClick="() => Duplicate(context)">复制</MenuItem>
                                <MenuItem OnClick="() => Export(context)">导出</MenuItem>
                                <MenuDivider />
                                <MenuItem OnClick="() => Delete(context)" Danger>删除</MenuItem>
                            </Menu>
                        </Overlay>
                        <ChildContent>
                            <Button Size="small">
                                <Icon Type="more" />
                            </Button>
                        </ChildContent>
                    </Dropdown>
                </SpaceItem>
            }
        </Space>
    </ActionColumn>
    
</Table>

@code {
    private bool isLargeScreen = true;
    private string scrollX => isLargeScreen ? "1400" : "800";
    private string tableStyle => isLargeScreen ? "width: 100%;" : "width: 100%;";
    private string actionColumnWidth => isLargeScreen ? "200" : "80";
    
    protected override async Task OnInitializedAsync()
    {
        // 检测屏幕尺寸
        var windowWidth = await JSRuntime.InvokeAsync<int>("eval", "window.innerWidth");
        isLargeScreen = windowWidth >= 1200;
    }
}
```

### 带选择框的固定列

```razor
<Table DataSource="@products" 
       TItem="ProductModel"
       ScrollX="1600"
       RowSelection="@rowSelection"
       @bind-SelectedRows="@selectedProducts">
    
    <!-- 选择列 - 固定在最左侧 -->
    <Selection Key="@(context.Id.ToString())" Fixed="left" Width="50" />
    
    <!-- 序号列 - 固定在左侧 -->
    <Column Title="序号" Fixed="left" Width="60">
        @((products.ToList().IndexOf(context) + 1))
    </Column>
    
    <!-- 产品信息 - 固定在左侧 -->
    <PropertyColumn Property="c => c.ProductCode" 
                    Title="产品编码" 
                    Fixed="left" 
                    Width="120" />
    
    <PropertyColumn Property="c => c.ProductName" 
                    Title="产品名称" 
                    Fixed="left" 
                    Width="200" />
    
    <!-- 普通列 -->
    <PropertyColumn Property="c => c.Category" Title="分类" Width="120" />
    <PropertyColumn Property="c => c.Brand" Title="品牌" Width="100" />
    <PropertyColumn Property="c => c.Specification" Title="规格" Width="150" />
    <PropertyColumn Property="c => c.Unit" Title="单位" Width="80" />
    <PropertyColumn Property="c => c.PurchasePrice" Title="采购价" Width="100" />
    <PropertyColumn Property="c => c.SalePrice" Title="销售价" Width="100" />
    <PropertyColumn Property="c => c.Stock" Title="库存" Width="80" />
    <PropertyColumn Property="c => c.MinStock" Title="最小库存" Width="100" />
    <PropertyColumn Property="c => c.MaxStock" Title="最大库存" Width="100" />
    <PropertyColumn Property="c => c.Supplier" Title="供应商" Width="150" />
    
    <!-- 状态列 - 固定在右侧 -->
    <PropertyColumn Property="c => c.Status" 
                    Title="状态" 
                    Fixed="right" 
                    Width="100">
        <Tag Color="@GetStatusColor(context.Status)">
            @GetStatusText(context.Status)
        </Tag>
    </PropertyColumn>
    
    <!-- 操作列 - 固定在最右侧 -->
    <ActionColumn Title="操作" 
                  Fixed="right" 
                  Width="120">
        <Space>
            <SpaceItem>
                <Button Type="link" Size="small" OnClick="() => Edit(context)">
                    编辑
                </Button>
            </SpaceItem>
            <SpaceItem>
                <Popconfirm Title="确定删除吗？" 
                            OnConfirm="() => Delete(context)">
                    <Button Type="link" Size="small" Danger>
                        删除
                    </Button>
                </Popconfirm>
            </SpaceItem>
        </Space>
    </ActionColumn>
    
</Table>

@code {
    private ProductModel[] selectedProducts = Array.Empty<ProductModel>();
    
    private TableRowSelection<ProductModel> rowSelection = new()
    {
        Type = "checkbox",
        Fixed = true, // 选择列固定
        OnChange = (selectedRowKeys, selectedRows) =>
        {
            // 处理选择变化
            Console.WriteLine($"选中了 {selectedRows.Count()} 个产品");
        }
    };
}
```

## 样式自定义

### 固定列阴影效果

```css
/* 在 wwwroot/css/app.css 中添加 */

/* 左固定列阴影 */
.ant-table-fixed-left {
    box-shadow: 6px 0 6px -4px rgba(0, 0, 0, 0.15);
}

/* 右固定列阴影 */
.ant-table-fixed-right {
    box-shadow: -6px 0 6px -4px rgba(0, 0, 0, 0.15);
}

/* 固定列背景色 */
.ant-table-thead > tr > th.ant-table-cell-fix-left,
.ant-table-tbody > tr > td.ant-table-cell-fix-left {
    background: #fafafa;
}

.ant-table-thead > tr > th.ant-table-cell-fix-right,
.ant-table-tbody > tr > td.ant-table-cell-fix-right {
    background: #fafafa;
}

/* 暗色主题下的固定列 */
[data-theme="dark"] .ant-table-thead > tr > th.ant-table-cell-fix-left,
[data-theme="dark"] .ant-table-tbody > tr > td.ant-table-cell-fix-left,
[data-theme="dark"] .ant-table-thead > tr > th.ant-table-cell-fix-right,
[data-theme="dark"] .ant-table-tbody > tr > td.ant-table-cell-fix-right {
    background: #1f1f1f;
}
```

### 固定列宽度动画

```css
/* 固定列宽度变化动画 */
.ant-table-cell-fix-left,
.ant-table-cell-fix-right {
    transition: width 0.3s ease;
}

/* 鼠标悬停效果 */
.ant-table-tbody > tr:hover > td.ant-table-cell-fix-left,
.ant-table-tbody > tr:hover > td.ant-table-cell-fix-right {
    background: #e6f7ff;
}
```

## 性能优化

### 虚拟滚动 + 固定列

```razor
<Table DataSource="@largeDataSet" 
       TItem="DataModel"
       ScrollX="2000"
       ScrollY="400"
       Virtual="true"
       VirtualItemHeight="54">
    
    <!-- 固定列在虚拟滚动中的配置 -->
    <PropertyColumn Property="c => c.Id" 
                    Title="ID" 
                    Fixed="left" 
                    Width="80" />
    
    <!-- 大量普通列... -->
    @for (int i = 0; i < 20; i++)
    {
        var index = i;
        <Column Title="@($"列{index + 1}")" Width="120">
            @context.GetPropertyValue($"Column{index + 1}")
        </Column>
    }
    
    <ActionColumn Title="操作" 
                  Fixed="right" 
                  Width="100">
        <Button Type="link" Size="small">操作</Button>
    </ActionColumn>
    
</Table>
```

### 条件渲染优化

```razor
@code {
    private bool shouldShowFixedColumns => data.Length > 50; // 数据量大时才启用固定列
    
    private string GetFixedPosition(string position)
    {
        return shouldShowFixedColumns ? position : null;
    }
}

<Table DataSource="@data" 
       TItem="DataModel"
       ScrollX="@(shouldShowFixedColumns ? "1200" : null)">
    
    <PropertyColumn Property="c => c.Id" 
                    Title="ID" 
                    Fixed="@GetFixedPosition("left")" 
                    Width="80" />
    
    <!-- 其他列... -->
    
    <ActionColumn Title="操作" 
                  Fixed="@GetFixedPosition("right")" 
                  Width="120">
        <!-- 操作按钮 -->
    </ActionColumn>
    
</Table>
```

## 常见问题与解决方案

### 1. 固定列不生效

**问题**: 设置了 `Fixed` 属性但列没有固定

**解决方案**:
```razor
<!-- ❌ 错误：没有设置 ScrollX -->
<Table DataSource="@data" TItem="DataModel">
    <PropertyColumn Property="c => c.Id" Fixed="left" />
</Table>

<!-- ✅ 正确：必须设置 ScrollX -->
<Table DataSource="@data" TItem="DataModel" ScrollX="800">
    <PropertyColumn Property="c => c.Id" Fixed="left" Width="80" />
</Table>
```

### 2. 固定列宽度问题

**问题**: 固定列宽度不合适导致显示异常

**解决方案**:
```razor
<!-- ❌ 错误：没有设置宽度 -->
<PropertyColumn Property="c => c.LongText" Fixed="left" />

<!-- ✅ 正确：设置合适的宽度 -->
<PropertyColumn Property="c => c.LongText" 
                Fixed="left" 
                Width="200" 
                Ellipsis="true" />
```

### 3. 固定列层级问题

**问题**: 固定列被其他元素遮挡

**解决方案**:
```css
/* 提高固定列的 z-index */
.ant-table-cell-fix-left,
.ant-table-cell-fix-right {
    z-index: 2;
}

/* 确保表头固定列在最顶层 */
.ant-table-thead .ant-table-cell-fix-left,
.ant-table-thead .ant-table-cell-fix-right {
    z-index: 3;
}
```

### 4. 移动端适配

```razor
@code {
    private bool isMobile = false;
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var width = await JSRuntime.InvokeAsync<int>("eval", "window.innerWidth");
            isMobile = width < 768;
            StateHasChanged();
        }
    }
}

<Table DataSource="@data" 
       TItem="DataModel"
       ScrollX="@(isMobile ? "600" : "1200")"
       Size="@(isMobile ? TableSize.Small : TableSize.Default)">
    
    <!-- 移动端只固定最重要的列 -->
    <PropertyColumn Property="c => c.Id" 
                    Title="ID" 
                    Fixed="@(isMobile ? null : "left")" 
                    Width="80" />
    
    <!-- 操作列在移动端始终固定 -->
    <ActionColumn Title="操作" 
                  Fixed="right" 
                  Width="@(isMobile ? "60" : "120")">
        @if (isMobile)
        {
            <Dropdown>
                <Overlay>
                    <Menu>
                        <MenuItem OnClick="() => Edit(context)">编辑</MenuItem>
                        <MenuItem OnClick="() => Delete(context)">删除</MenuItem>
                    </Menu>
                </Overlay>
                <ChildContent>
                    <Button Size="small" Type="text">
                        <Icon Type="more" />
                    </Button>
                </ChildContent>
            </Dropdown>
        }
        else
        {
            <Space>
                <SpaceItem>
                    <Button Type="link" Size="small" OnClick="() => Edit(context)">编辑</Button>
                </SpaceItem>
                <SpaceItem>
                    <Button Type="link" Size="small" Danger OnClick="() => Delete(context)">删除</Button>
                </SpaceItem>
            </Space>
        }
    </ActionColumn>
    
</Table>
```

## 最佳实践总结

1. **必须设置 ScrollX**: 固定列功能的前提条件
2. **合理设置列宽**: 避免固定列过宽或过窄
3. **限制固定列数量**: 通常左右各不超过2-3列
4. **考虑响应式**: 在小屏幕设备上适当减少固定列
5. **性能考虑**: 大数据量时结合虚拟滚动使用
6. **用户体验**: 重要信息（如ID、操作）才需要固定
7. **样式统一**: 保持固定列与普通列的视觉一致性

通过以上指南，可以在 HB Platform 多店铺管理系统中实现高质量的固定列功能，提升用户的数据浏览体验。