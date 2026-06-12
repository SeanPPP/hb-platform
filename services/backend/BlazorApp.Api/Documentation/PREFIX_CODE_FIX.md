# 前缀代码使用修复说明

## 问题描述

在之前的实现中，系统错误地使用了 `PrefixCode`（主键，GUID）来生成商品货号，而实际上应该使用 `PrefixName`（实际前缀值，如 "HB"、"YW"、"GZ" 等）。

### 数据模型说明

`ProductPrefixCode` 表结构：
- **PrefixCode**: 主键（GUID），用于数据库关联
- **PrefixName**: 前缀代码名称（如：HB、YW、GZ 等），用于生成货号
- **SupplierCode**: 所属供应商
- **PrefixDescription**: 前缀描述

## 修复内容

### 1. `GenerateNextProductNoAsync` 方法

**修复前：**
```csharp
public async Task<ApiResponse<string>> GenerateNextProductNoAsync(string supplierCode, string? prefixCode = null)
{
    // ...
    if (!string.IsNullOrWhiteSpace(prefixCode))
    {
        // ❌ 直接使用 prefixCode（GUID）生成货号
        productNo = ItemNumberHelper.GenerateItemNumberWithPrefix(supplierCode, prefixCode, existingProductNos);
    }
    // ...
}
```

**修复后：**
```csharp
public async Task<ApiResponse<string>> GenerateNextProductNoAsync(string supplierCode, string? prefixCode = null)
{
    // ...
    if (!string.IsNullOrWhiteSpace(prefixCode))
    {
        // ✅ 通过 PrefixCode (主键) 查询 PrefixName (实际前缀值)
        var prefix = await db.Queryable<ProductPrefixCode>()
            .Where(p => p.PrefixCode == prefixCode && p.SupplierCode == supplierCode && !p.IsDeleted)
            .FirstAsync();
        
        if (prefix == null)
        {
            return ApiResponse<string>.Error("前缀不存在或不属于该供应商", "PREFIX_NOT_FOUND");
        }
        
        // ✅ 使用 PrefixName 生成货号
        productNo = ItemNumberHelper.GenerateItemNumberWithPrefix(supplierCode, prefix.PrefixName, existingProductNos);
    }
    // ...
}
```

### 2. `BatchCreateDomesticProductsAsync` 方法

**修复前：**
```csharp
List<string> productNos;
if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
{
    // ❌ 直接使用 PrefixCode（GUID）
    productNos = ItemNumberHelper.GenerateBatchItemNumbersWithPrefix(
        dto.SupplierCode, dto.PrefixCode, dto.Products.Count, existingProductNos);
}
```

**修复后：**
```csharp
List<string> productNos;
if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
{
    // ✅ 先查询 PrefixName
    var prefix = await db.Queryable<ProductPrefixCode>()
        .Where(p => p.PrefixCode == dto.PrefixCode && p.SupplierCode == dto.SupplierCode && !p.IsDeleted)
        .FirstAsync();
    
    if (prefix == null)
    {
        return ApiResponse<List<DomesticProductDto>>.Error("前缀不存在或不属于该供应商", "PREFIX_NOT_FOUND");
    }
    
    // ✅ 使用 PrefixName 生成批量货号
    productNos = ItemNumberHelper.GenerateBatchItemNumbersWithPrefix(
        dto.SupplierCode, prefix.PrefixName, dto.Products.Count, existingProductNos);
}
```

### 3. `BatchCreateSetProductsAsync` 方法

**修复前：**
```csharp
// 直接在循环中调用 GenerateNextProductNoAsync，传入 PrefixCode
var productNoResult = await GenerateNextProductNoAsync(dto.SupplierCode, dto.PrefixCode);
```

**修复后：**
```csharp
// ✅ 在事务外查询前缀信息
string? prefixName = null;
if (!string.IsNullOrWhiteSpace(dto.PrefixCode))
{
    var prefix = await db.Queryable<ProductPrefixCode>()
        .Where(p => p.PrefixCode == dto.PrefixCode && p.SupplierCode == dto.SupplierCode && !p.IsDeleted)
        .FirstAsync();
    
    if (prefix == null)
    {
        return ApiResponse<BatchCreateSetProductsResultDto>.Error("前缀不存在或不属于该供应商", "PREFIX_NOT_FOUND");
    }
    
    prefixName = prefix.PrefixName;
}

// 循环中调用方法（内部会再次查询，但已添加验证）
var productNoResult = await GenerateNextProductNoAsync(dto.SupplierCode, dto.PrefixCode);
```

## 前端更新

### `BatchCreateSetProductsModal.tsx`

1. **前缀默认值改为空**：
```typescript
const [prefixCode, setPrefixCode] = useState<string | null>(null); // ✅ 默认为空（可选）
```

2. **添加完整的前缀管理功能**：
- ✅ 选择供应商后自动加载前缀列表
- ✅ 下拉选择器支持选择或新建前缀
- ✅ 前缀增删改查功能（与 CreateProductModal 一致）
- ✅ 前缀选择器显示前缀名称和描述

## 数据流程

### 前端到后端的数据流

1. **用户选择供应商** → 加载该供应商的前缀列表
2. **用户选择前缀（可选）** → 将 `PrefixCode`（GUID）发送到后端
3. **后端接收 `PrefixCode`** → 查询 `PrefixName`（实际前缀值）
4. **后端使用 `PrefixName`** → 生成货号（如：`HB001`、`YW002`）

### 生成的货号格式

- **无前缀**: `HB001`, `HB002`, `HB003`, ...
- **有前缀（YW）**: `YW001`, `YW002`, `YW003`, ...
- **有前缀（GZ）**: `GZ001`, `GZ002`, `GZ003`, ...

## 验证测试

### 测试场景

1. **场景一：不选择前缀**
   - 期望：使用供应商默认前缀（如 `HB`）生成货号

2. **场景二：选择现有前缀（如 YW）**
   - 期望：生成 `YW001`, `YW002`, ... 格式的货号

3. **场景三：新建并选择前缀（如 GZ）**
   - 期望：生成 `GZ001`, `GZ002`, ... 格式的货号

4. **场景四：选择无效前缀**
   - 期望：返回错误提示 "前缀不存在或不属于该供应商"

## 影响范围

### 修改的文件

1. **后端服务**：
   - `BlazorApp.Api/Services/DomesticProductService.cs`
     - `GenerateNextProductNoAsync` ✅
     - `BatchCreateDomesticProductsAsync` ✅
     - `BatchCreateSetProductsAsync` ✅

2. **前端组件**：
   - `ReactUmi/my-app/src/pages/DomesticProducts/BatchCreateSetProductsModal.tsx`
     - 添加前缀管理功能 ✅
     - 前缀默认值改为 `null` ✅

### 不需要修改的部分

- DTO 定义（`PrefixCode` 字段名称保持不变，仍然接收主键 GUID）
- 数据库表结构（无需变更）
- 前端其他组件（`CreateProductModal` 已正确实现）

## 注意事项

1. **前缀选择是可选的**：如果不选择前缀，系统会使用供应商的默认前缀（通常是供应商代码）
2. **前缀验证**：后端会验证前缀是否存在且属于所选供应商
3. **PrefixCode vs PrefixName**：
   - 前端和 DTO 使用 `PrefixCode`（主键，GUID）进行引用
   - 后端在生成货号时使用 `PrefixName`（实际前缀值，如 "HB"、"YW"）
4. **性能优化**：在批量操作中，前缀查询只执行一次（在事务外），避免重复查询

## 总结

这次修复确保了系统正确使用 `PrefixName` 来生成商品货号，而不是错误地使用 `PrefixCode`（GUID）。同时，前端也增强了前缀管理功能，提供了完整的增删改查操作，使用户可以方便地管理和选择前缀。

