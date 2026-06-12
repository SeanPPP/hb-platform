# 仓库商品批量管理功能 - 完整文档

## 📋 功能概述

仓库商品批量管理系统是一个强大的企业级批量编辑工具，支持对10W+商品数据进行高效管理。

### 核心特性
- ✅ **多条件过滤**：支持商品编码、名称、价格、库存等多维度过滤
- ✅ **批量编辑**：支持表格内直接编辑，失焦验证
- ✅ **智能复制粘贴**：从Excel复制数据直接粘贴到表格
- ✅ **增量保存 + 批量保存**：灵活的保存策略
- ✅ **批量操作**：批量设置价格、调整库存、设置状态、设置仓位
- ✅ **仓位管理**：单个商品限制一个仓位
- ✅ **乐观锁**：并发控制，防止数据冲突
- ✅ **导出功能**：支持Excel和PDF导出（待实现）
- ✅ **外部集成**：支持从货柜明细等页面跳转并预筛选商品
- ✅ **友好提示**：未保存提示、操作确认、实时验证

---

## 🎯 访问路径

```
路由: /warehouse/batch-edit
权限: Admin（管理员）或 Warehouse（仓库管理员）
```

### 外部页面跳转示例

从货柜明细页面跳转并传入商品集合：

```csharp
// 在 ContainerDetail.razor 中
private void OpenBatchEdit()
{
    var selectedCodes = SelectedProducts.Select(p => p.ProductCode).ToList();
    var codesParam = string.Join(",", selectedCodes);
    Navigation.NavigateTo($"/warehouse/batch-edit?codes={codesParam}");
}
```

---

## 🔧 技术架构

### 后端架构

```
BlazorApp.Api/
├── Controllers/
│   └── WarehouseProductBatchController.cs    # API控制器（10个端点）
├── Services/
│   ├── IWarehouseProductBatchService.cs      # 服务接口
│   └── WarehouseProductBatchService.cs       # 服务实现（事务处理）
├── Interfaces/
│   └── IWarehouseProductBatchService.cs
└── DTOs/ (Shared)
    ├── WarehouseProductBatchDto.cs           # 主DTO
    ├── WarehouseProductFilterDto.cs          # 过滤条件
    ├── BatchUpdateRequest.cs                  # 批量更新请求
    ├── IncrementalSaveRequest.cs             # 增量保存请求
    ├── BulkOperationRequest.cs               # 批量操作请求
    ├── LocationEditDto.cs                    # 仓位编辑
    └── PagedResultDto.cs                     # 分页结果
```

### 前端架构

```
BlazorApp/
├── Pages/Warehouse/
│   ├── WarehouseProductBatchEdit.razor       # 主页面
│   └── WarehouseProductBatchEdit.razor.cs    # 代码后置
├── Services/
│   ├── IWarehouseProductBatchClient.cs       # 服务接口
│   └── WarehouseProductBatchClient.cs        # 服务实现
└── wwwroot/js/
    └── clipboard-helper.js                    # 剪贴板辅助工具
```

---

## 📊 数据库设计

### 主要表结构

#### WarehouseProduct（仓库商品表）

```sql
CREATE TABLE WarehouseProduct (
    ProductCode VARCHAR(50) PRIMARY KEY,        -- 商品编码
    DomesticPrice DECIMAL(18,2),                -- 国内价格
    OEMPrice DECIMAL(18,2),                     -- 贴牌价格
    ImportPrice DECIMAL(18,2),                  -- 进口价格
    StockQuantity INT,                          -- 库存数量
    MinOrderQuantity INT,                       -- 最小订货量
    StockValue DECIMAL(18,2),                   -- 库存金额（自动计算）
    StockAlertQuantity INT,                     -- 库存预警数
    Volume DECIMAL(18,4),                       -- 单件体积
    IsActive BIT,                               -- 使用状态
    RowVersion VARBINARY(MAX),                  -- 行版本号（乐观锁）
    CreatedAt DATETIME,
    UpdatedAt DATETIME
);
```

**业务规则**：
- `StockValue = StockQuantity × ImportPrice`（自动计算）
- `RowVersion` 用于乐观锁并发控制

#### ProductLocation（商品仓位关联表）

```sql
CREATE TABLE ProductLocation (
    Guid VARCHAR(36) PRIMARY KEY,
    ProductCode VARCHAR(50),                    -- 商品编码（外键）
    LocationGuid VARCHAR(36),                   -- 仓位GUID（外键）
    CreatedAt DATETIME,
    UpdatedAt DATETIME
);
```

**业务规则**：
- 数据库支持多对多关系
- **UI限制**：一个商品只能设置一个仓位

---

## 🚀 核心功能详解

### 1. 多条件过滤

**支持的过滤字段**：
- 商品编码（模糊匹配）
- 商品名称（模糊匹配）
- 货号（模糊匹配）
- 供应商编码（模糊匹配）
- 国内价格范围（最小值~最大值）
- 库存范围（最小值~最大值）
- 使用状态（全部/启用/禁用）
- 仓位代码（模糊匹配）
- 指定商品编码集合（外部传入）

**性能优化**：
- 使用索引优化查询
- 分页查询（50/100/200/500/1000可选）
- 避免N+1查询，使用`Include`预加载关联数据

---

### 2. 批量编辑功能

#### 编辑方式

**双击编辑模式**：
1. 双击单元格进入编辑状态
2. 输入数据
3. 失焦后自动验证并标记为已修改
4. 已修改的行以黄色背景高亮显示

**可编辑字段**：
- 国内价格（≥0，2位小数）
- 贴牌价格（≥0，2位小数）
- 进口价格（≥0，2位小数）
- 库存数量（≥0，整数）
- 最小订货量（≥0，整数）
- 单件体积（≥0，4位小数）
- 使用状态（启用/禁用）

**自动计算**：
- 修改`进口价格`或`库存数量`时，自动计算`库存金额`

#### 数据验证

**验证规则**：
```csharp
[Range(0, double.MaxValue, ErrorMessage = "价格不能为负数")]
public decimal? DomesticPrice { get; set; }

[Range(0, int.MaxValue, ErrorMessage = "库存数量不能为负数")]
public int? StockQuantity { get; set; }
```

**验证时机**：
- **失焦验证**：输入框失去焦点时验证
- **保存前验证**：提交前完整验证

---

### 3. 保存策略

#### 增量保存（单行/部分行）

**使用场景**：
- 修改了几条数据，想先保存验证
- 不想一次性保存所有修改

**操作方式**：
- 点击每行的"保存"按钮

**后端实现**：
```csharp
public async Task<IncrementalSaveResult> IncrementalSaveAsync(IncrementalSaveRequest request)
{
    // 使用事务保证数据一致性
    await _db.Adt.UseTranAsync(async () =>
    {
        // 逐条保存
        // 乐观锁检查
        // 更新仓位关系
        // 返回新的RowVersion
    });
}
```

#### 批量保存（全部修改）

**使用场景**：
- 修改了多条数据，一次性全部保存

**操作方式**：
- 点击顶部"保存全部修改(N)"按钮
- 或使用快捷键 `Ctrl+S`

**保存流程**：
```
1. 收集所有已修改的商品
2. 弹出确认对话框（显示修改数量）
3. 使用事务批量更新
4. 乐观锁检查（并发控制）
5. 更新成功后清除修改标记
6. 刷新数据
```

---

### 4. 复制粘贴功能

#### 使用步骤

1. 在Excel中选择数据并复制（`Ctrl+C`）
2. 在批量管理页面表格区域，使用`Ctrl+V`粘贴
3. 系统弹出确认对话框
4. 确认后，数据从当前页第一行开始填充

#### 列映射规则

**按列顺序映射**（从选定单元格开始）：

```
列1 → 国内价格
列2 → 贴牌价格
列3 → 进口价格
列4 → 库存数量
列5 → 最小订货量
列6 → 单件体积
```

#### 实现原理

**JavaScript监听粘贴事件**：
```javascript
// clipboard-helper.js
element.addEventListener('paste', async (e) => {
    const clipboardData = e.clipboardData.getData('Text');
    const parsedData = parseClipboardData(clipboardData);
    await dotnetHelper.invokeMethodAsync('HandlePasteData', parsedData);
});
```

**Blazor处理粘贴数据**：
```csharp
[JSInvokable]
public async Task HandlePasteData(string[][] pastedData)
{
    // 解析数据
    // 填充到表格
    // 标记为已修改
}
```

---

### 5. 批量操作

#### 批量设置价格

**操作步骤**：
1. 选择多个商品（勾选复选框）
2. 点击"批量操作"按钮
3. 选择"批量设置价格"
4. 选择价格类型（国内价/贴牌价/进口价）
5. 输入新价格
6. 确认后执行

**后端实现**：
```csharp
await _db.Updateable<WarehouseProduct>()
    .SetColumns(wp => wp.DomesticPrice == request.Price)
    .SetColumns(wp => wp.UpdatedAt == DateTime.Now)
    .Where(wp => request.ProductCodes.Contains(wp.ProductCode))
    .ExecuteCommandAsync();
```

#### 批量调整库存

**调整方式**：
- **设置为**：直接设置为指定数量
- **增加**：当前数量 + 指定数量
- **减少**：当前数量 - 指定数量（最小为0）

**自动计算**：调整库存后，自动重新计算`库存金额`

#### 批量设置状态

**操作**：批量启用或禁用商品

#### 批量设置仓位

**操作**：批量将商品设置到同一个仓位

---

### 6. 仓位管理

#### 业务规则

- **数据库设计**：支持多对多关系（商品 ↔ 仓位）
- **UI限制**：一个商品只能设置一个仓位
- **操作方式**：
  - 点击商品行的"编辑"按钮
  - 在弹窗中选择仓位
  - 保存后更新关联关系

#### 仓位编辑流程

```
1. 删除该商品的所有现有仓位关联
2. 如果选择了新仓位，创建新的关联记录
3. 更新显示
```

**后端实现**：
```csharp
private async Task<bool> UpdateProductLocationAsync(string productCode, string? locationGuid)
{
    // 删除现有关联
    await _db.Deleteable<ProductLocation>()
        .Where(pl => pl.ProductCode == productCode)
        .ExecuteCommandAsync();

    // 创建新关联
    if (!string.IsNullOrEmpty(locationGuid))
    {
        var newRelation = new ProductLocation
        {
            Guid = Guid.NewGuid().ToString(),
            ProductCode = productCode,
            LocationGuid = locationGuid
        };
        await _db.Insertable(newRelation).ExecuteCommandAsync();
    }

    return true;
}
```

---

### 7. 乐观锁并发控制

#### 原理

使用`RowVersion`字段实现乐观锁：
- 每次更新时，SqlSugar自动递增`RowVersion`
- 更新前检查`RowVersion`是否一致
- 不一致则说明数据已被其他用户修改

#### 实现

**实体配置**：
```csharp
[SugarColumn(IsEnableUpdateVersionValidation = true, IsNullable = true)]
public byte[]? RowVersion { get; set; }
```

**更新时检查**：
```csharp
// 查询当前数据
var existing = await _db.Queryable<WarehouseProduct>()
    .Where(wp => wp.ProductCode == dto.ProductCode)
    .FirstAsync();

// 比较RowVersion
if (dto.RowVersion != null && existing.RowVersion != null)
{
    if (!dto.RowVersion.SequenceEqual(existing.RowVersion))
    {
        return Error("数据已被其他用户修改，请刷新后重试");
    }
}

// 更新（SqlSugar自动处理RowVersion）
await _db.Updateable(existing).ExecuteCommandAsync();
```

---

### 8. 未保存提示

#### 功能说明

当有未保存的修改时，用户尝试关闭页面或跳转到其他页面，系统会弹出确认对话框。

#### 实现方式

**启用警告**：
```javascript
window.onbeforeunload = function (e) {
    e.preventDefault();
    e.returnValue = "有 8 条未保存的修改，确定离开？";
    return "有 8 条未保存的修改，确定离开？";
};
```

**禁用警告**：
```javascript
window.onbeforeunload = null;
```

**Blazor调用**：
```csharp
// 有修改时启用
await JSRuntime.InvokeVoidAsync("pageLeaveWarning.enable", 
    $"有 {modifiedCount} 条未保存的修改，确定离开？");

// 保存完成后禁用
await JSRuntime.InvokeVoidAsync("pageLeaveWarning.disable");
```

---

## 📈 性能优化

### 数据库优化

#### 1. 索引优化

**建议索引**：
```sql
-- 商品编码（主键，自动索引）
CREATE INDEX IX_WarehouseProduct_ProductCode ON WarehouseProduct(ProductCode);

-- 常用过滤字段
CREATE INDEX IX_WarehouseProduct_DomesticPrice ON WarehouseProduct(DomesticPrice);
CREATE INDEX IX_WarehouseProduct_StockQuantity ON WarehouseProduct(StockQuantity);
CREATE INDEX IX_WarehouseProduct_IsActive ON WarehouseProduct(IsActive);

-- 外键索引
CREATE INDEX IX_ProductLocation_ProductCode ON ProductLocation(ProductCode);
CREATE INDEX IX_ProductLocation_LocationGuid ON ProductLocation(LocationGuid);
```

#### 2. 避免N+1查询

**使用Include预加载**：
```csharp
var query = _db.Queryable<WarehouseProduct>()
    .Includes(wp => wp.Product)        // 预加载Product信息
    .Includes(wp => wp.Locations);     // 预加载Location信息
```

#### 3. 批量操作使用BulkUpdate

**避免逐条更新**：
```csharp
// ❌ 慢：逐条更新
foreach (var product in products)
{
    await _db.Updateable(product).ExecuteCommandAsync();
}

// ✅ 快：批量更新
await _db.Updateable(products).ExecuteCommandAsync();
```

#### 4. 事务处理

**确保数据一致性**：
```csharp
await _db.Adt.UseTranAsync(async () =>
{
    // 所有操作在事务中执行
    // 任何一步失败都会回滚
});
```

### 前端优化

#### 1. 分页加载

**支持的分页大小**：50/100/200/500/1000

**建议**：
- 默认使用50或100
- 大数据量查询时使用虚拟滚动

#### 2. 防抖搜索

**实现防抖**：
```csharp
private System.Timers.Timer? searchDebounceTimer;

private void OnFilterChange()
{
    searchDebounceTimer?.Stop();
    searchDebounceTimer = new System.Timers.Timer(500);
    searchDebounceTimer.Elapsed += async (s, e) => await SearchProducts();
    searchDebounceTimer.AutoReset = false;
    searchDebounceTimer.Start();
}
```

---

## 🔒 权限控制

### API权限

```csharp
[Authorize(Roles = "Admin,Warehouse")]
public class WarehouseProductBatchController : ControllerBase
{
    // 只有Admin或Warehouse角色可访问
}
```

### 页面权限

在路由配置中添加：
```razor
@attribute [Authorize(Roles = "Admin,Warehouse")]
```

---

## 🧪 测试指南

### 功能测试

#### 1. 基础功能测试

- [ ] 页面正常加载
- [ ] 过滤条件正常工作
- [ ] 分页功能正常
- [ ] 双击编辑功能正常
- [ ] 数据验证正常

#### 2. 保存功能测试

- [ ] 单行保存成功
- [ ] 批量保存成功
- [ ] 保存失败时正确回滚
- [ ] 乐观锁冲突检测

#### 3. 批量操作测试

- [ ] 批量设置价格
- [ ] 批量调整库存
- [ ] 批量设置状态
- [ ] 批量设置仓位

#### 4. 特殊功能测试

- [ ] 复制粘贴功能
- [ ] 未保存提示
- [ ] 快捷键（Ctrl+S）
- [ ] 外部跳转参数传递

### 性能测试

#### 测试场景

**小数据量**（1000条）：
- 查询响应时间 < 1秒
- 批量保存（50条）< 2秒

**中等数据量**（10000条）：
- 查询响应时间 < 2秒
- 批量保存（100条）< 5秒

**大数据量**（100000条）：
- 查询响应时间 < 5秒（带索引）
- 批量保存（500条）< 10秒

---

## 🐛 已知问题和限制

### 当前限制

1. **导出功能未实现**：Excel和PDF导出功能待开发
2. **虚拟滚动未启用**：大数据量时建议使用分页
3. **复制粘贴列映射固定**：不支持自定义列顺序

### 计划改进

1. ✅ 实现Excel导出（使用EPPlus）
2. ✅ 实现PDF导出（使用QuestPDF）
3. ✅ 添加Excel导入功能
4. ✅ 支持自定义列映射
5. ✅ 添加虚拟滚动（大数据量场景）

---

## 📝 开发日志

### v1.0.0 (2024-10-03)

#### 已完成功能

✅ **后端**：
- 7个DTOs（数据传输对象）
- Service层（完整业务逻辑 + 事务处理）
- API Controller（10个端点）
- 服务注册（依赖注入）
- 乐观锁支持（RowVersion）

✅ **前端**：
- 主页面（1500+行代码）
- 过滤面板
- 批量编辑表格
- 批量操作Modal
- 仓位编辑Modal
- 复制粘贴功能
- 未保存提示
- 服务Client

✅ **优化**：
- 10W+数据性能优化
- 分页查询
- 事务处理
- 并发控制

#### 待实现功能

⏳ **导出功能**：
- Excel导出（需要EPPlus包）
- PDF导出（需要QuestPDF包）

⏳ **扩展功能**：
- Excel批量导入
- 数据审计日志
- 操作历史记录

---

## 🤝 贡献指南

### 代码规范

遵循项目的代码规范：
- 使用C#命名规范（PascalCase类名，camelCase变量名）
- 使用4空格缩进
- 添加详细的中文注释
- 所有公共方法添加XML文档注释

### 提交规范

```
feat: 添加Excel导出功能
fix: 修复批量保存并发问题
docs: 更新功能文档
perf: 优化查询性能
```

---

## 📞 联系方式

如有问题或建议，请联系：
- 项目负责人：HB Platform开发团队
- 邮箱：dev@hbplatform.com

---

**最后更新时间**：2024-10-03  
**文档版本**：v1.0.0

