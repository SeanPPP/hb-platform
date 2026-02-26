# 批量创建套装商品功能实现文档

## 功能概述

支持批量创建套装商品，统一配置套装规格和价格。每个商品将自动生成指定数量的套装明细（套10、套15等）。

## API 端点

```
POST /api/react/v1/domestic-products/batch-create-set-products
```

### 请求体示例

```json
{
  "supplierCode": "SUP001",
  "prefixCode": "HB",
  "setType": 10,
  "products": [
    {
      "productName": "圣诞礼品盒A",
      "englishProductName": "Christmas Gift Box A",
      "productSpecification": "盒",
      "productType": 1
    },
    {
      "productName": "圣诞礼品盒B",
      "englishProductName": "Christmas Gift Box B",
      "productSpecification": "盒",
      "productType": 1
    }
  ],
  "setPrices": [
    { "domesticPrice": 2.5, "importPrice": 3.0, "oemPrice": 2.8 },
    { "domesticPrice": 2.99, "importPrice": 3.5, "oemPrice": 3.2 },
    { "domesticPrice": 3.5, "importPrice": 4.0, "oemPrice": 3.7 },
    { "domesticPrice": 3.99, "importPrice": 4.5, "oemPrice": 4.2 },
    { "domesticPrice": 4.5, "importPrice": 5.0, "oemPrice": 4.7 },
    { "domesticPrice": 4.99, "importPrice": 5.5, "oemPrice": 5.2 },
    { "domesticPrice": 5.5, "importPrice": 6.0, "oemPrice": 5.7 },
    { "domesticPrice": 5.5, "importPrice": 6.0, "oemPrice": 5.7 },
    { "domesticPrice": 5.99, "importPrice": 6.5, "oemPrice": 6.2 },
    { "domesticPrice": 5.99, "importPrice": 6.5, "oemPrice": 6.2 }
  ]
}
```

### 响应体示例

```json
{
  "success": true,
  "data": {
    "createdProducts": [...],
    "failedProducts": [],
    "successCount": 2,
    "failureCount": 0,
    "totalSetItems": 20,
    "errors": []
  },
  "message": "批量创建完成：成功2个商品，共20个套装明细"
}
```

## Service 层实现逻辑

### 方法签名

```csharp
public async Task<ApiResponse<BatchCreateSetProductsResultDto>> BatchCreateSetProductsAsync(BatchCreateSetProductsDto dto)
```

### 实现步骤

#### 1. 数据验证

```csharp
// 验证供应商是否存在
var supplier = await _dbContext.ChinaSuppliers
    .FirstOrDefaultAsync(s => s.SupplierCode == dto.SupplierCode);

if (supplier == null)
{
    return ApiResponse<BatchCreateSetProductsResultDto>.Fail("供应商不存在");
}

// 验证套装价格数量是否匹配
if (dto.SetPrices.Count != dto.SetType)
{
    return ApiResponse<BatchCreateSetProductsResultDto>.Fail(
        $"套装价格数量({dto.SetPrices.Count})与套装规格({dto.SetType})不匹配"
    );
}
```

#### 2. 遍历商品列表，批量创建

```csharp
var result = new BatchCreateSetProductsResultDto();
var createdProducts = new List<DomesticProductDto>();
var errors = new List<string>();
var totalSetItems = 0;

using var transaction = await _dbContext.Database.BeginTransactionAsync();

try
{
    foreach (var product in dto.Products)
    {
        try
        {
            // 2.1 生成商品货号
            var productNo = await GenerateNextProductNoAsync(dto.SupplierCode, dto.PrefixCode);
            
            // 2.2 生成条形码
            var barcode = await GenerateProductBarcodeAsync(dto.SupplierCode, 1); // 1=套装商品
            
            // 2.3 创建主商品
            var domesticProduct = new DomesticProduct
            {
                ProductCode = Guid.NewGuid().ToString(),
                SupplierCode = dto.SupplierCode,
                ProductName = product.ProductName,
                EnglishProductName = product.EnglishProductName,
                ProductSpecification = product.ProductSpecification,
                HBProductNo = productNo.Data,
                Barcode = barcode.Data,
                ProductType = 1, // 套装商品
                IsActive = true,
                CreatedAt = DateTime.Now,
                CreatedBy = _currentUser.Username
            };
            
            await _dbContext.DomesticProducts.AddAsync(domesticProduct);
            await _dbContext.SaveChangesAsync();
            
            // 2.4 创建套装明细
            var setProducts = new List<DomesticSetProduct>();
            
            for (int i = 0; i < dto.SetType; i++)
            {
                var setPriceItem = dto.SetPrices[i];
                
                // 生成套装货号：主商品货号 + "-S" + SetType + "-" + 序号
                var setProductNo = $"{productNo.Data}-S{dto.SetType}-{(i + 1).ToString().PadLeft(2, '0')}";
                
                // 生成套装条码
                var setBarcode = await GenerateProductBarcodeAsync(dto.SupplierCode, 1);
                
                var setProduct = new DomesticSetProduct
                {
                    SetProductCode = Guid.NewGuid().ToString(),
                    ProductCode = domesticProduct.ProductCode,
                    ProductNo = productNo.Data,
                    SetProductNo = setProductNo,
                    SetBarcode = setBarcode.Data,
                    DomesticPrice = setPriceItem.DomesticPrice,
                    ImportPrice = setPriceItem.ImportPrice,
                    OEMPrice = setPriceItem.OEMPrice,
                    IsDeleted = false,
                    CreatedAt = DateTime.Now,
                    CreatedBy = _currentUser.Username
                };
                
                setProducts.Add(setProduct);
            }
            
            await _dbContext.DomesticSetProducts.AddRangeAsync(setProducts);
            await _dbContext.SaveChangesAsync();
            
            // 2.5 记录创建日志
            var creationLog = new DomesticProductCreationLog
            {
                LogId = Guid.NewGuid().ToString(),
                ProductCode = domesticProduct.ProductCode,
                SupplierCode = dto.SupplierCode,
                ProductName = product.ProductName,
                HBProductNo = productNo.Data,
                ProductType = 1,
                SetQuantity = dto.SetType,
                CreatedBy = _currentUser.Username,
                CreatedAt = DateTime.Now,
                Status = "Success"
            };
            
            await _dbContext.DomesticProductCreationLogs.AddAsync(creationLog);
            await _dbContext.SaveChangesAsync();
            
            // 2.6 添加到成功列表
            var productDto = _mapper.Map<DomesticProductDto>(domesticProduct);
            createdProducts.Add(productDto);
            totalSetItems += dto.SetType;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建商品失败: {ProductName}", product.ProductName);
            errors.Add($"创建商品 '{product.ProductName}' 失败: {ex.Message}");
        }
    }
    
    // 3. 提交事务
    await transaction.CommitAsync();
    
    // 4. 构建返回结果
    result.CreatedProducts = createdProducts;
    result.SuccessCount = createdProducts.Count;
    result.FailureCount = dto.Products.Count - createdProducts.Count;
    result.TotalSetItems = totalSetItems;
    result.Errors = errors;
    
    return ApiResponse<BatchCreateSetProductsResultDto>.Success(result);
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    _logger.LogError(ex, "批量创建套装商品失败");
    return ApiResponse<BatchCreateSetProductsResultDto>.Fail($"批量创建失败: {ex.Message}");
}
```

## 数据库表结构

### DomesticProduct (主商品表)

```sql
CREATE TABLE DomesticProduct (
    ProductCode NVARCHAR(50) PRIMARY KEY,
    SupplierCode NVARCHAR(50) NOT NULL,
    ProductName NVARCHAR(200) NOT NULL,
    EnglishProductName NVARCHAR(500),
    ProductSpecification NVARCHAR(100),
    HBProductNo NVARCHAR(50),
    Barcode NVARCHAR(50),
    ProductType INT NOT NULL DEFAULT 0, -- 0:普通 1:套装 2:多码
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME NOT NULL,
    CreatedBy NVARCHAR(100),
    ...
)
```

### DomesticSetProduct (套装明细表)

```sql
CREATE TABLE DomesticSetProduct (
    SetProductCode NVARCHAR(50) PRIMARY KEY,
    ProductCode NVARCHAR(50) NOT NULL, -- 关联主商品
    ProductNo NVARCHAR(50), -- 主商品货号
    SetProductNo NVARCHAR(50), -- 套装货号
    SetBarcode NVARCHAR(50), -- 套装条码
    DomesticPrice DECIMAL(18,2),
    ImportPrice DECIMAL(18,2),
    OEMPrice DECIMAL(18,2),
    IsDeleted BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL,
    CreatedBy NVARCHAR(100),
    ...
    FOREIGN KEY (ProductCode) REFERENCES DomesticProduct(ProductCode)
)
```

### DomesticProductCreationLog (创建日志表)

```sql
CREATE TABLE DomesticProductCreationLog (
    LogId NVARCHAR(50) PRIMARY KEY,
    ProductCode NVARCHAR(50) NOT NULL,
    SupplierCode NVARCHAR(50) NOT NULL,
    ProductName NVARCHAR(200),
    HBProductNo NVARCHAR(50),
    ProductType INT,
    SetQuantity INT, -- 套装数量
    CreatedBy NVARCHAR(100),
    CreatedAt DATETIME NOT NULL,
    Status NVARCHAR(50), -- Success, Failed
    ErrorMessage NVARCHAR(MAX)
)
```

## 货号生成规则

### 主商品货号
- 格式：`{前缀}{供应商编码}{序列号}`
- 示例：`HB-SUP001-00001`
- 生成方法：`GenerateNextProductNoAsync(supplierCode, prefixCode)`

### 套装货号
- 格式：`{主商品货号}-{序号（两位数，前导0）}`
- 示例：`HB001-01`（第1个套装）
- 示例：`HB001-02`（第2个套装）
- 示例：`HB001-10`（第10个套装）
- 示例：`HB001-15`（第15个套装）
- 生成方法：`ItemNumberHelper.GenerateSetItemNumber(baseProductNo, existingSetNumbers)`

### 套装货号生成规则详解

#### 方法位置
- **文件**: `BlazorApp.Shared/Helper/ItemNumberHelper.cs`
- **方法**: `GenerateSetItemNumber`

#### 方法签名
```csharp
public static string GenerateSetItemNumber(
    string baseItemNumber,              // 基础商品货号
    List<string> existingSetItemNumbers // 现有套装货号列表
)
```

#### 生成逻辑
1. 查找该基础商品的所有现有套装货号
2. 解析并找到最大的序号
3. 生成下一个序号（最大序号 + 1）
4. 返回格式化的套装货号

#### 生成示例
```csharp
// 第一个套装
ItemNumberHelper.GenerateSetItemNumber("HB001", new List<string>())  
→ "HB001-01"

// 已有 HB001-01, HB001-02
ItemNumberHelper.GenerateSetItemNumber("HB001", new List<string> { "HB001-01", "HB001-02" })
→ "HB001-03"

// 批量生成套10
var existingNumbers = new List<string>();
for (int i = 0; i < 10; i++)
{
    var setNo = ItemNumberHelper.GenerateSetItemNumber("HB001", existingNumbers);
    existingNumbers.Add(setNo);
    // 生成: HB001-01, HB001-02, ..., HB001-10
}
```

#### 特点
- ✅ **自动递增**: 自动查找并生成下一个可用序号
- ✅ **格式统一**: 所有套装货号使用相同格式
- ✅ **避免冲突**: 检查现有货号，确保不重复
- ✅ **简洁明了**: 格式简单，易于识别

## 错误处理

1. **供应商不存在**
   - 返回400错误，提示供应商不存在

2. **套装价格数量不匹配**
   - 返回400错误，提示价格数量与套装规格不匹配

3. **商品创建失败**
   - 单个商品创建失败不影响其他商品
   - 记录错误信息到errors数组
   - 失败的商品添加到failedProducts数组

4. **事务回滚**
   - 如果整个操作失败，回滚所有已创建的数据
   - 返回500错误，提示批量创建失败

## 性能优化

1. **批量插入**
   - 使用`AddRangeAsync`批量插入套装明细

2. **事务处理**
   - 使用数据库事务确保数据一致性
   - 失败时自动回滚

3. **异步处理**
   - 所有数据库操作使用异步方法
   - 避免阻塞线程

## 测试用例

### 测试1：正常创建
- 2个商品 × 套10 = 20个套装明细
- 预期：全部成功创建

### 测试2：套装价格数量不匹配
- 2个商品，套10规格，但只提供5个价格
- 预期：返回400错误

### 测试3：供应商不存在
- 使用不存在的供应商编码
- 预期：返回400错误

### 测试4：部分商品创建失败
- 3个商品，其中1个商品名称过长导致失败
- 预期：成功2个，失败1个，failureCount=1

## 前端集成

### 调用示例

```typescript
import { batchCreateSetProducts } from '@/services/domesticProduct';

const result = await batchCreateSetProducts({
  supplierCode: 'SUP001',
  prefixCode: 'HB',
  setType: 10,
  products: [
    {
      productName: '圣诞礼品盒A',
      englishProductName: 'Christmas Gift Box A',
      productSpecification: '盒',
      productType: 1,
    },
  ],
  setPrices: [
    { domesticPrice: 2.5 },
    { domesticPrice: 2.99 },
    // ... 共10个
  ],
});

if (result.success) {
  message.success(`创建成功：${result.data.successCount}个商品`);
} else {
  message.error(result.message);
}
```

## 实现清单

- [x] DTO定义 (DomesticProductDtos.cs)
- [x] 控制器端点 (ReactDomesticProductsController.cs)
- [x] Service接口定义 (IDomesticProductService.cs)
- [ ] Service实现 (DomesticProductService.cs) - **待实现**
- [x] 前端类型定义 (services/domesticProduct.ts)
- [x] 前端React组件 (BatchCreateSetProductsModal.tsx)
- [ ] 数据库迁移脚本 - **待实现**（如果需要新表或字段）

## 下一步

1. 在 `DomesticProductService.cs` 中实现 `BatchCreateSetProductsAsync` 方法
2. 测试API端点
3. 在主页面集成新组件
4. 编写单元测试

