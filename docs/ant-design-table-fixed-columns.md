# Ant Design Blazor Table 固定列和固定表头实现

## 概述

本文档记录了在 HB Platform 多店铺订单管理系统中实现 Ant Design Blazor Table 固定列和固定表头的配置方法。

## 参考文档

- [Ant Design Blazor Table 官方文档](https://antblazor.com/zh-CN/components/table#components-table-demo-fixed-columns)

## 实现要点

### 1. 表格基础配置

```razor
<AntDesign.Table TItem="WarehouseProductListDto" 
                 DataSource="@products.Items"
                 Loading="loading"
                 Total="@(products?.Total ?? 0)"
                 PageSize="@filter.PageSize"
                 OnChange="@(async (args) => await HandleTableChange(args))"
                 ScrollX="1500"
                 ScrollY="600"
                 RowKey="@(context => context?.ProductCode ?? context?.ItemNumber ?? Guid.NewGuid().ToString())">
```

**关键属性说明：**
- `Total` - 设置数据总数，用于分页计算
- `PageSize` - 设置每页显示的数据条数
- `OnChange` - 处理分页、排序、筛选等表格变化事件
- `ScrollX="1500"` - 设置横向滚动宽度，当表格内容超过此宽度时启用横向滚动
- `ScrollY="600"` - 设置纵向滚动高度，当表格内容超过此高度时启用纵向滚动
- `RowKey` - 设置行的唯一标识符，用于优化渲染性能

### 2. 固定列配置

#### 左侧固定列（前3列）

```razor
<!-- 选择列 -->
<Column TData="WarehouseProductListDto" Title="Select" Width="50" Fixed="ColumnFixPlacement.Left">
    <!-- 列内容 -->
</Column>

<!-- 商品图片 -->
<Column TData="WarehouseProductListDto" Title="Image" Width="70" Fixed="ColumnFixPlacement.Left">
    <!-- 列内容 -->
</Column>

<!-- 货号 -->
<Column TData="WarehouseProductListDto" Title="Item Number" Width="130" Fixed="ColumnFixPlacement.Left">
    <!-- 列内容 -->
</Column>
```

#### 右侧固定列

```razor
<!-- 采购价 -->
<Column TData="WarehouseProductListDto" Title="Purchase Price" Width="130" Fixed="ColumnFixPlacement.Right">
    <!-- 列内容 -->
</Column>
```

**Fixed 属性值：**
- `ColumnFixPlacement.Left` - 固定在左侧
- `ColumnFixPlacement.Right` - 固定在右侧

### 3. CSS 样式优化

```css
/* 表格固定列样式优化 */
.ant-table-cell-fix-left,
.ant-table-cell-fix-right {
    background: #fff !important;
    z-index: 2;
}

/* 固定列阴影效果 */
.ant-table-cell-fix-left-last::after,
.ant-table-cell-fix-right-first::after {
    content: '';
    position: absolute;
    top: 0;
    bottom: 0;
    width: 30px;
    pointer-events: none;
    transition: box-shadow 0.3s;
}

.ant-table-cell-fix-left-last::after {
    right: -30px;
    box-shadow: inset 10px 0 8px -8px rgba(0, 0, 0, 0.15);
}

.ant-table-cell-fix-right-first::after {
    left: -30px;
    box-shadow: inset -10px 0 8px -8px rgba(0, 0, 0, 0.15);
}
```

## 实现效果

### 功能特性

1. **固定表头** - 当表格内容超出 `ScrollY` 高度时，表头会自动固定在顶部
2. **左侧固定列** - 选择列、图片列、货号列固定在左侧，横向滚动时保持可见
3. **右侧固定列** - 采购价列固定在右侧，横向滚动时保持可见
4. **阴影效果** - 固定列边缘有阴影效果，提供视觉分隔
5. **响应式设计** - 在不同屏幕尺寸下保持良好的显示效果

### 用户体验

- ✅ **高性能滚动** - 支持大量数据的流畅滚动
- ✅ **关键信息可见** - 重要的选择、图片、货号信息始终可见
- ✅ **操作便利** - 采购价等重要数据固定在右侧，便于查看
- ✅ **视觉清晰** - 固定列有阴影分隔，界面层次分明

## 注意事项

### 1. 版本兼容性

- 确保使用 Ant Design Blazor 1.4.3 或更高版本
- `Fixed` 属性在某些旧版本中可能不支持

### 2. 性能考虑

- 固定列会增加渲染复杂度，建议固定列数量控制在 3-4 列以内
- 大量数据时，`RowKey` 的正确设置对性能很重要

### 3. 响应式设计

- 在小屏幕设备上，固定列可能会影响表格的可读性
- 建议在移动端考虑隐藏部分固定列或调整布局

### 4. 样式定制

- 固定列的样式可以通过 CSS 类进行定制
- 阴影效果可以根据设计需求调整颜色和强度

## 相关文件

- `BlazorApp/Components/ProductList.razor` - 主要实现文件
- `docs/ant-design-table-fixed-columns.md` - 本文档

## 最新改进 (2024-01-XX)

### 1. 内置分页实现
- 移除了自定义分页控件
- 使用 Ant Design Table 的内置分页功能
- 支持分页大小选择器和快速跳转
- 通过 `OnChange` 事件处理分页，而不是 `Current` 属性

### 2. 列对齐优化
- 为所有列添加了 `Align` 属性
- 选择列和图片列：居中对齐 (`ColumnAlign.Center`)
- 文本列：左对齐 (`ColumnAlign.Left`)
- 价格列：右对齐 (`ColumnAlign.Right`)

### 3. 水平滚动优化
- 确保表格容器支持水平滚动
- 设置表格最小宽度为 1500px
- 优化滚动条显示

### 4. 表格事件处理
- 实现了 `OnChange` 事件处理
- 支持分页、排序、筛选的统一处理
- 使用 `QueryModel` 进行数据查询
- 通过 `queryModel.PageIndex` 和 `queryModel.PageSize` 获取分页信息

## 更新记录

- **2024-01-XX** - 初始实现，支持固定表头和固定列
- **2024-01-XX** - 优化阴影效果和响应式设计
- **2024-01-XX** - 取消固定列设置，恢复为普通表格
- **2024-01-XX** - 实现内置分页，修复列对齐问题，优化水平滚动
- **2024-01-XX** - 修复分页属性错误，移除不存在的 `Current` 属性 