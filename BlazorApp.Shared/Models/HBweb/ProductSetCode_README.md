# ProductSetCode 产品套装多码表设计文档

## 概述

基于现有的 `Product.cs` 产品实体类，参考 `DomesticSetProduct.cs` 的设计模式，创建了 `ProductSetCode.cs` 产品套装多码表，用于管理产品的套装编码和多码信息。

## 文件结构

### 1. 实体类 (Entity)
- **文件位置**: `BlazorApp.Shared/Models/HBweb/ProductSetCode.cs`
- **用途**: 数据库实体类，定义套装多码表的数据结构
- **继承**: `BaseEntity` (包含创建时间、更新时间、删除标记等基础字段)

### 2. DTO类 (Data Transfer Objects)
- **文件位置**: `BlazorApp.Shared/DTOs/ProductSetCodeDto.cs`
- **包含的DTO类型**:
  - `ProductSetCodeDto`: 完整的数据传输对象
  - `CreateProductSetCodeDto`: 创建套装多码的请求DTO
  - `UpdateProductSetCodeDto`: 更新套装多码的请求DTO
  - `ProductSetCodeQueryDto`: 查询套装多码的请求DTO

### 3. 映射扩展 (Mapping Extensions)
- **文件位置**: `BlazorApp.Shared/Mappings/ProductSetCodeMappingExtensions.cs`
- **用途**: 提供实体类与DTO之间的转换方法

## 数据库表设计

### 表名: `ProductSetCode`

| 字段名 | 类型 | 是否必填 | 说明 |
|--------|------|----------|------|
| SetCodeId | string(PK) | ✅ | 套装编码主键(UUID7) |
| ProductCode | string(FK) | ✅ | 关联Product表的产品编码 |
| SetItemNumber | string | ✅ | 套装货号 |
| SetBarcode | string | ❌ | 套装条码 |
| SetName | string | ❌ | 套装名称 |
| SetQuantity | int | ✅ | 套装数量(默认:1) |
| SetPurchasePrice | decimal | ❌ | 套装采购价格 |
| SetRetailPrice | decimal | ❌ | 套装零售价格 |
| SetType | int | ✅ | 套装类型(1:组合套装,2:固定套装,3:变量套装) |
| IsActive | bool | ✅ | 是否启用(默认:true) |

### 继承自BaseEntity的字段
| 字段名 | 类型 | 说明 |
|--------|------|------|
| CreatedAt | DateTime | 创建时间 |
| UpdatedAt | DateTime | 更新时间 |
| CreatedBy | string | 创建者 |
| UpdatedBy | string | 更新者 |
| IsDeleted | bool | 软删除标记 |

## 关键特性

### 1. 数据验证
- 所有必填字段都有 `[Required]` 验证
- 数值范围验证 (`[Range]`)
- 字符串长度限制
- 内置业务逻辑验证方法 `ValidateSetData()`

### 2. 导航属性
```csharp
// 关联产品
[Navigate(NavigateType.OneToOne, nameof(ProductCode), nameof(Product.ProductCode))]
public Product? Product { get; set; }
```

### 3. 计算属性
- `SetTypeDescription`: 套装类型描述
- `StatusDescription`: 状态描述
- `AverageUnitPrice`: 单品平均价格

### 4. UUID7主键
使用 `UuidHelper.GenerateUuid7()` 生成基于时间的UUID，提供更好的排序性能。

## 使用示例

### 创建套装多码
```csharp
var createDto = new CreateProductSetCodeDto
{
    ProductCode = "PROD001",
    SetItemNumber = "SET001",
    SetName = "优惠套装A",
    SetQuantity = 3,
    SetRetailPrice = 299.99m,
    SetType = 1, // 组合套装
    IsActive = true
};

var entity = createDto.ToEntity("admin");
```

### 查询套装多码
```csharp
var queryDto = new ProductSetCodeQueryDto
{
    ProductCode = "PROD001",
    IsActive = true,
    SetType = 1,
    PageNumber = 1,
    PageSize = 20
};
```

### 实体转DTO
```csharp
var dto = entity.ToDto();
var dtoList = entityList.ToDtoList();
```

## 业务规则

### 套装类型定义
1. **组合套装**: 可以灵活组合不同产品
2. **固定套装**: 固定的产品组合，不可拆分
3. **变量套装**: 可变数量的产品组合

### 价格计算
- 支持套装价格与单品价格的自动换算
- `AverageUnitPrice` = `SetRetailPrice` / `SetQuantity`

### 状态管理
- `IsActive`: 控制套装是否可用
- `IsDeleted`: 软删除标记

## 扩展建议

### 1. 后续开发计划
- [ ] 创建对应的Service层
- [ ] 创建对应的Controller API
- [ ] 创建对应的Blazor页面组件
- [ ] 添加数据库迁移脚本

### 2. 可能的业务扩展
- 套装库存管理
- 套装优惠策略
- 套装销售统计
- 套装推荐算法

## 注意事项

1. **数据一致性**: 确保 `ProductCode` 外键的有效性
2. **价格逻辑**: 套装价格应合理，避免出现负数
3. **数量限制**: `SetQuantity` 必须大于0
4. **性能考虑**: 大量数据时注意分页查询
5. **并发控制**: 更新操作时注意并发处理

## 版本记录

- **v1.1** (2025-01-16): 简化版本，移除非必要字段
  - ✅ 删除英文名称、成本价格、拆分销售等冗余字段
  - ✅ 删除供应商、类别、排序等扩展字段
  - ✅ 保持核心套装多码功能
  - ✅ 更新所有相关DTO和映射方法
- **v1.0** (2025-01-16): 初始版本，完整的套装多码管理功能
  - ✅ 实体类设计完成
  - ✅ DTO类设计完成  
  - ✅ 映射扩展方法完成
  - ✅ 数据验证规则完成
