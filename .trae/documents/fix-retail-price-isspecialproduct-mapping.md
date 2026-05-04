# 修复计划：零售价同步缺失"是否特殊商品"字段映射

## 问题分析

`DIC_商品零售价表 → StoreRetailPrice` 的 AutoMapper 映射中，**缺少 `IsSpecialProduct`（是否特殊商品）字段**的映射配置。

### 根因

两个映射 Profile 文件中都没有配置 `dest.IsSpecialProduct ← src.H是否特殊商品`：

| 文件 | 缺失字段 |
|------|----------|
| `ReactStoreRetailPriceMappingProfile.cs` | `IsSpecialProduct` 未映射 |
| `StoreMappingProfile.cs` | `IsSpecialProduct` 未映射 |

### 对比参照

同一文件 `StoreMappingProfile.cs` 中，`DIC_分店一品多码表 → StoreMultiCodeProduct` 映射**正确包含**了该字段：
```csharp
.ForMember(dest => dest.IsSpecialProduct, opt => opt.MapFrom(src => src.H是否特殊商品 ?? false))
```

### 数据源确认

- **HQ 源实体** `DIC_商品零售价表.H是否特殊商品`（`bool` 类型，第38行） ✅ 存在
- **本地目标实体** `StoreRetailPrice.IsSpecialProduct`（`bool` 类型，默认 `false`，第88行） ✅ 存在

### 影响

`SyncStoreRetailPricesFromHqConcurrentAsync` 方法在第754行通过 `_mapper.Map<List<StoreRetailPrice>>(hqBatch)` 执行映射，缺失映射导致所有同步的零售价记录 `IsSpecialProduct` 始终为 `false`，无法正确反映 HQ 端的特殊商品标记。

## 修复方案

### 步骤 1：修复 `ReactStoreRetailPriceMappingProfile.cs`

在 `.ForMember(dest => dest.IsAutoPricing, ...)` 之后添加：
```csharp
.ForMember(dest => dest.IsSpecialProduct, opt => opt.MapFrom(src => src.H是否特殊商品))
```

### 步骤 2：修复 `StoreMappingProfile.cs`

在 `.ForMember(dest => dest.IsAutoPricing, ...)` 之后添加：
```csharp
.ForMember(dest => dest.IsSpecialProduct, opt => opt.MapFrom(src => src.H是否特殊商品))
```

### 步骤 3：验证

编译项目确保无错误。

## 涉及文件

1. `BlazorApp.Api/Mappings/Profiles/React/ReactStoreRetailPriceMappingProfile.cs` — 第31行后添加映射
2. `BlazorApp.Api/Mappings/Profiles/StoreMappingProfile.cs` — 第32行后添加映射
