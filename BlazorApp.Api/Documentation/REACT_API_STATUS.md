# React AG Grid API 实现状态报告

## 📋 实施总结

为 React 项目的 AG Grid Community 表格组件实现了专用的后端 API 接口。

---

## ✅ 已完成的工作

### 1. 新增 DTO 类

✅ **AgGridRequestDto.cs** - AG Grid 请求相关DTO
- `AgGridRequestDto` - 主请求类
- `AgGridFilterModel` - 过滤器模型
- `AgGridSortModel` - 排序模型
- `AgGridResponseDto<T>` - 响应类
- `AgGridDataDto<T>` - 数据包装类

✅ **BatchDeleteRequestDto.cs** - 批量删除请求
- `BatchDeleteRequestDto` - 包含ID列表

✅ **UpdateSetItemsRequestDto.cs** - 套装商品更新请求
- `UpdateSetItemsRequestDto` - 包含套装子项列表
- `SetItemUpdateDto` - 单个子项更新数据

### 2. Controller 接口

✅ 在 `DomesticProductsController.cs` 中新增：
- `POST /api/v1/domestic-products/grid` - AG Grid 数据查询
- `DELETE /api/v1/domestic-products/batch-delete` - 批量删除
- `GET /api/v1/domestic-products/{id}/set-items` - 获取套装子项
- `PUT /api/v1/domestic-products/{id}/set-items` - 更新套装子项

### 3. Service 接口定义

✅ 在 `IDomesticProductService.cs` 中新增：
- `GetGridDataAsync` - AG Grid 数据查询
- `BatchDeleteAsync` - 批量删除
- `GetSetItemsAsync` - 获取套装子项
- `UpdateSetItemsAsync` - 更新套装子项

### 4. Service 实现

✅ 在 `DomesticProductService.cs` 中新增：
- 主要查询方法
- 过滤器处理方法（文本、数字、集合）
- 排序处理方法
- 批量操作方法
- 套装商品处理方法

### 5. 文档

✅ **REACT_API_IMPLEMENTATION.md** - 完整的API使用文档
- 接口说明
- 请求/响应示例
- 过滤器使用指南
- 技术实现细节
- 测试示例

---

## ⚠️ 发现的问题

### 问题 1: 字段名不匹配

**问题描述**: 
前端 React 使用的字段名与后端 Model 的实际字段名不一致。

**字段映射对照**:

| React 前端 | DTO 期望字段 | 实际 Model 字段 | 状态 |
|-----------|--------------|----------------|------|
| `id` | `Id` | `ProductCode` (主键) | ❌ 不匹配 |
| `nameEn` | `ProductNameEn` | `EnglishProductName` | ❌ 不匹配 |
| `specs` | `Specifications` | `ProductSpecification` | ❌ 不匹配 |
| `labelPrice` | `LabelPrice` | `OEMPrice` | ❌ 不匹配 |
| `material` | `Material` | ❓ **不存在** | ❌ Model中无此字段 |
| `packingSize` | `PackingSize` | ❓ **不存在** | ❌ Model中无此字段 |
| `remark` | `Remarks` | ❓ **不存在** | ❌ Model中无此字段 |
| `grossWeight` | `UnitGrossWeight` | ❓ **不存在** | ❌ Model中无此字段 |

### 问题 2: 缺少的 Model 字段

以下字段在 `DomesticProduct` Model 中**不存在**，但前端需要：
- ❌ `Material` - 材质
- ❌ `PackingSize` - 包装尺寸
- ❌ `Remarks` - 备注
- ❌ `UnitGrossWeight` - 单件毛重

### 问题 3: 主键类型不匹配

- **Model**: `ProductCode` (string, UUID7)
- **前端期望**: `id` (number)
- **影响**: 批量删除、套装子项查询等接口都基于 `id` (int)

### 问题 4: 套装商品相关类型未定义

- ❌ `DomesticSetProductItem` Model
- ❌ `DomesticSetProductItemDto` DTO

---

## 🔧 需要的修复

### 修复方案 A: 修改 Model（**推荐**）

**优点**: 
- 前端代码不需要修改
- 字段命名更符合前端习惯
- 统一字段命名规范

**缺点**:
- 需要数据库迁移
- 可能影响现有 Blazor 项目

**需要添加的字段**:
```sql
ALTER TABLE DomesticProduct ADD Material NVARCHAR(50);
ALTER TABLE DomesticProduct ADD PackingSize NVARCHAR(50);
ALTER TABLE DomesticProduct ADD Remarks NVARCHAR(500);
ALTER TABLE DomesticProduct ADD UnitGrossWeight DECIMAL(10,2);
```

**需要重命名的字段**:
```sql
EXEC sp_rename 'DomesticProduct.EnglishProductName', 'ProductNameEn', 'COLUMN';
EXEC sp_rename 'DomesticProduct.ProductSpecification', 'Specifications', 'COLUMN';
EXEC sp_rename 'DomesticProduct.OEMPrice', 'LabelPrice', 'COLUMN';
```

### 修复方案 B: 修改 Service 层（**当前可行**）

**优点**:
- 不影响数据库
- 不影响现有 Blazor 项目
- 快速实施

**缺点**:
- 缺少的字段无法提供数据（返回null）
- 需要修改 DTO 映射逻辑

**实施步骤**:
1. 修改 Service 中的字段映射
2. 对于缺少的字段返回 null 或默认值
3. 在响应中说明哪些字段不可用

### 修复方案 C: 修改前端（**不推荐**）

**优点**:
- 后端无需修改

**缺点**:
- 前端代码已经完成，需要大量修改
- 字段命名不一致，维护困难

---

## 🎯 推荐方案

### 短期方案（快速上线）

**使用方案 B**: 修改 Service 层
1. 修改字段映射以匹配实际 Model
2. 缺少的字段暂时返回 null
3. 前端显示时判断字段是否存在

### 长期方案（完整功能）

**使用方案 A**: 扩展 Model
1. 添加缺少的4个字段到数据库
2. 更新 Model 类
3. 执行数据库迁移
4. 完整支持所有前端功能

---

## 📝 待办事项

### 立即需要做的（修复编译错误）

- [ ] 修复 Service 中的字段名映射
- [ ] 处理缺少的字段（返回null或默认值）
- [ ] 修复主键类型不匹配问题
- [ ] 定义 `DomesticSetProductItem` 相关类型
- [ ] 修复套装商品查询逻辑

### 后续优化（功能完善）

- [ ] 添加缺少的数据库字段
- [ ] 统一字段命名规范
- [ ] 完善套装商品功能
- [ ] 添加单元测试
- [ ] 添加集成测试

---

## 🚀 下一步行动

**请用户决策**：

1. **是否可以修改数据库**？
   - ✅ 可以 → 执行方案A（添加缺少的4个字段）
   - ❌ 不可以 → 执行方案B（Service层适配）

2. **主键使用哪种方式**？
   - 选项1: 添加自增 `Id` 字段作为数字主键
   - 选项2: 继续使用 `ProductCode`，前端适配

3. **套装商品是否需要完整功能**？
   - ✅ 需要 → 定义完整的套装商品Model和DTO
   - ❌ 暂不需要 → 先移除相关接口

---

## 📂 已创建的文件清单

```
BlazorApp.Shared/DTOs/
├── AgGridRequestDto.cs                 ✅ 已创建
├── BatchDeleteRequestDto.cs            ✅ 已创建
└── UpdateSetItemsRequestDto.cs         ✅ 已创建

BlazorApp.Api/Controllers/
└── DomesticProductsController.cs       ✅ 已修改（新增4个接口）

BlazorApp.Api/Interfaces/
└── IDomesticProductService.cs          ✅ 已修改（新增4个方法）

BlazorApp.Api/Services/
└── DomesticProductService.cs           ✅ 已修改（新增实现，有编译错误）

BlazorApp.Api/Documentation/
├── REACT_API_IMPLEMENTATION.md         ✅ 已创建
└── REACT_API_STATUS.md                 ✅ 已创建（本文档）
```

---

## ⏰ 预估修复时间

- **方案A（完整修复）**: 2-3小时
  - 数据库迁移: 30分钟
  - 代码修改: 1小时
  - 测试验证: 1小时

- **方案B（快速修复）**: 30-60分钟
  - 代码修改: 30分钟
  - 测试验证: 30分钟

---

**状态**: ⚠️ 等待用户决策  
**最后更新**: 2025-01-21  
**文档版本**: v1.0.0

