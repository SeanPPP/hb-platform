# React批量新建商品功能 - 后端实现总结

## 📋 概述

本文档总结了为React前端批量新建商品功能实现的后端代码。

---

## 🗂️ 文件清单

### 1. Model层

#### 新建文件

| 文件路径 | 说明 |
|----------|------|
| `BlazorApp.Shared/Models/HBweb/DomesticProductCreationLog.cs` | 国内商品创建记录表Model |

**关键特性**:
- 使用UUID7作为主键
- 记录商品创建的关键信息（供应商、货号、条码等）
- 支持批次号，标识同一批次创建的商品
- 包含创建方式字段（Batch/Single/Import）

---

### 2. DTO层

#### 新建文件

| 文件路径 | 说明 |
|----------|------|
| `BlazorApp.Shared/DTOs/GridRequestDto.cs` | Grid数据请求/响应DTO |

**包含DTO**:
- `GridRequestDto`: Grid数据请求
- `FilterModelDto`: 筛选模型
- `SortModelDto`: 排序模型
- `GridResponseDto<T>`: Grid数据响应

---

### 3. Controller层

#### 新建文件

| 文件路径 | 说明 | 路由前缀 |
|----------|------|----------|
| `BlazorApp.Api/Controllers/React/ReactDomesticProductsController.cs` | 商品管理API | `/api/react/domestic-products` |
| `BlazorApp.Api/Controllers/React/ReactSuppliersController.cs` | 供应商API | `/api/react/suppliers` |
| `BlazorApp.Api/Controllers/React/ReactProductPrefixCodesController.cs` | 前缀管理API | `/api/react/product-prefix-codes` |

---

### 4. 数据库迁移

#### 新建文件

| 文件路径 | 说明 |
|----------|------|
| `BlazorApp.Api/Data/Migrations/20251022_CreateProductCreationLog.sql` | 创建记录表迁移脚本 |

**迁移内容**:
- 创建 `DomesticProductCreationLog` 表
- 创建4个索引（SupplierCode, BatchNumber, CreatedAt, CreationType）
- 创建统计视图 `V_ProductCreationStat`
- 添加表注释和字段注释

---

## 📊 数据库设计

### DomesticProductCreationLog 表结构

```sql
CREATE TABLE DomesticProductCreationLog (
    -- 主键
    LogId NVARCHAR(50) PRIMARY KEY,
    
    -- 关联字段
    ProductCode NVARCHAR(50) NOT NULL,
    SupplierCode NVARCHAR(50) NOT NULL,
    
    -- 基本信息
    SupplierName NVARCHAR(200),
    HBProductNo NVARCHAR(50) NOT NULL,
    Barcode NVARCHAR(50),
    ProductName NVARCHAR(200),
    
    -- 前缀信息
    PrefixCode NVARCHAR(50),
    PrefixName NVARCHAR(10),
    
    -- 创建方式和批次
    CreationType NVARCHAR(20) DEFAULT 'Batch',
    BatchNumber NVARCHAR(50),
    
    -- 审计字段
    Remark NVARCHAR(500),
    IsDeleted BIT DEFAULT 0,
    CreatedAt DATETIME2 DEFAULT GETDATE(),
    UpdatedAt DATETIME2,
    CreatedBy NVARCHAR(50),
    UpdatedBy NVARCHAR(50),
    
    -- 外键
    FOREIGN KEY (ProductCode) REFERENCES DomesticProduct(ProductCode) ON DELETE CASCADE,
    FOREIGN KEY (SupplierCode) REFERENCES ChinaSupplier(SupplierCode) ON DELETE NO ACTION
);
```

### 索引设计

| 索引名 | 列 | 说明 |
|--------|-----|------|
| IX_SupplierCode | SupplierCode | 按供应商查询 |
| IX_BatchNumber | BatchNumber | 按批次查询 |
| IX_CreatedAt | CreatedAt DESC | 按时间倒序查询 |
| IX_CreationType | CreationType | 按创建方式统计 |

---

## 🔌 API接口

### 1. 供应商API (ReactSuppliersController)

#### GET /api/react/suppliers/list
获取启用的供应商列表（用于下拉选择）

**权限**: Admin, WarehouseManager, User

**响应**:
```json
{
  "success": true,
  "data": [
    {
      "code": "SUP001",
      "name": "华邦供应商",
      "contactPerson": "张三",
      "phone": "13800138000"
    }
  ],
  "message": "获取供应商列表成功"
}
```

#### GET /api/react/suppliers/{supplierCode}
获取供应商详情

**权限**: Admin, WarehouseManager

---

### 2. 前缀API (ReactProductPrefixCodesController)

#### GET /api/react/product-prefix-codes/by-supplier/{supplierCode}
获取供应商的前缀列表

**权限**: Admin, WarehouseManager

**响应**:
```json
{
  "success": true,
  "data": [
    {
      "prefixCode": "018d8f...",
      "supplierCode": "SUP001",
      "prefixName": "HB",
      "prefixDescription": "华邦货号",
      "isActive": true,
      "sortOrder": 1
    }
  ],
  "message": "获取前缀列表成功"
}
```

#### POST /api/react/product-prefix-codes
创建前缀

**权限**: Admin

**请求**:
```json
{
  "supplierCode": "SUP001",
  "prefixName": "HB",
  "prefixDescription": "华邦货号",
  "sortOrder": 1,
  "isActive": true
}
```

#### PUT /api/react/product-prefix-codes/{prefixCode}
更新前缀

**权限**: Admin

#### DELETE /api/react/product-prefix-codes/{prefixCode}
删除前缀

**权限**: Admin

**错误码**:
- `PREFIX_IN_USE`: 前缀正在使用中，无法删除（返回409 Conflict）

---

### 3. 商品API (ReactDomesticProductsController)

#### POST /api/react/domestic-products/grid
获取商品列表（react-data-grid服务端分页）

**权限**: Admin, WarehouseManager

**请求**:
```json
{
  "startRow": 0,
  "endRow": 100,
  "pageSize": 100,
  "globalSearch": "搜索关键词",
  "filterModel": {
    "name": {
      "filterType": "text",
      "type": "contains",
      "filter": "商品"
    }
  },
  "sortModel": [
    {
      "colId": "createdAt",
      "sort": "desc"
    }
  ]
}
```

#### POST /api/react/domestic-products/batch-validate
批量验证商品数据

**权限**: Admin, WarehouseManager

**请求**:
```json
{
  "supplierCode": "SUP001",
  "prefixCode": "018d8f...",
  "products": [
    {
      "productName": "商品A",
      "domesticPrice": 10.50,
      "oemPrice": 15.00,
      "packingQuantity": 24,
      "unitVolume": 0.0123,
      "middlePackQuantity": 6
    }
  ]
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "validProducts": [/* 有效商品 */],
    "invalidProducts": [
      {
        "rowNumber": 2,
        "errors": {
          "productName": ["商品名称已存在"]
        }
      }
    ]
  },
  "message": "验证完成"
}
```

#### POST /api/react/domestic-products/batch-create ⭐
批量创建商品（核心接口）

**权限**: Admin

**请求**:
```json
{
  "supplierCode": "SUP001",
  "prefixCode": "018d8f...",
  "products": [
    {
      "productName": "商品A",
      "domesticPrice": 10.50,
      "oemPrice": 15.00,
      "packingQuantity": 24,
      "unitVolume": 0.0123,
      "middlePackQuantity": 6
    }
  ]
}
```

**响应**:
```json
{
  "success": true,
  "data": {
    "createdProducts": [/* 创建成功的商品 */],
    "failedProducts": [],
    "successCount": 1,
    "failureCount": 0,
    "errors": []
  },
  "message": "批量创建完成：成功1条，失败0条"
}
```

**创建日志逻辑**:
```csharp
// 生成批次号（用于标识同一批次创建的商品）
var batchNumber = UuidHelper.GenerateUuid7();

foreach (var item in dto.Products)
{
    // 1. 创建商品
    var product = new DomesticProduct { /* ... */ };
    await _db.Insertable(product).ExecuteCommandAsync();
    
    // 2. 创建记录日志
    var log = new DomesticProductCreationLog
    {
        LogId = UuidHelper.GenerateUuid7(),
        ProductCode = product.ProductCode,
        SupplierCode = dto.SupplierCode,
        SupplierName = supplier.SupplierName,
        HBProductNo = product.HBProductNo,
        Barcode = product.Barcode,
        ProductName = product.ProductName,
        PrefixCode = dto.PrefixCode,
        PrefixName = prefix?.PrefixName,
        CreationType = "Batch",
        BatchNumber = batchNumber, // 同一批次号
        CreatedBy = currentUser.Username,
        CreatedAt = DateTime.UtcNow
    };
    await _db.Insertable(log).ExecuteCommandAsync();
}
```

#### PUT /api/react/domestic-products/{productCode}
更新商品信息

**权限**: Admin

#### DELETE /api/react/domestic-products/batch-delete
批量删除商品

**权限**: Admin

#### GET /api/react/domestic-products/{productCode}/set-items
获取套装商品详情

**权限**: Admin, WarehouseManager

#### PUT /api/react/domestic-products/{productCode}/set-items
更新套装商品信息

**权限**: Admin

---

## 🔐 权限控制

### 角色定义
- **Admin**: 管理员,所有权限
- **WarehouseManager**: 仓库管理员,只读+部分编辑
- **User**: 普通用户,只读

### 权限矩阵

| 操作 | Admin | WarehouseManager | User |
|------|-------|------------------|------|
| 查看供应商 | ✓ | ✓ | ✓ |
| 查看前缀 | ✓ | ✓ | ✗ |
| 创建/编辑/删除前缀 | ✓ | ✗ | ✗ |
| 批量验证商品 | ✓ | ✓ | ✗ |
| 批量创建商品 | ✓ | ✗ | ✗ |
| 更新商品 | ✓ | ✗ | ✗ |
| 删除商品 | ✓ | ✗ | ✗ |

---

## 🔍 错误处理

### 错误码

| 错误码 | HTTP状态码 | 说明 |
|--------|-----------|------|
| VALIDATION_ERROR | 400 | 请求参数验证失败 |
| SUPPLIER_NOT_FOUND | 400 | 供应商不存在 |
| PREFIX_NOT_FOUND | 404 | 前缀不存在 |
| PREFIX_NAME_EXISTS | 409 | 前缀代码已存在 |
| PREFIX_IN_USE | 409 | 前缀正在使用中 |
| INTERNAL_SERVER_ERROR | 500 | 服务器内部错误 |

### 统一错误响应格式

```json
{
  "success": false,
  "message": "错误描述",
  "errorCode": "ERROR_CODE"
}
```

---

## 📝 实现步骤

### Service层需要实现的方法

以下方法需要在现有Service中实现：

#### IDomesticProductService

```csharp
// 1. Grid数据获取（已存在，需确认支持react-data-grid格式）
Task<GridResponseDto<DomesticProductDto>> GetGridDataAsync(GridRequestDto request);

// 2. 批量验证（新增）
Task<ApiResponse<BatchValidationResultDto>> BatchValidateProductsAsync(BatchCreateDomesticProductDto dto);

// 3. 批量创建（已存在，需添加创建日志逻辑）
Task<ApiResponse<BatchProductOperationResultDto>> BatchCreateDomesticProductsAsync(BatchCreateDomesticProductDto dto);

// 4. 批量删除（已存在）
Task<ApiResponse<object>> BatchDeleteAsync(List<string> productCodes);

// 5. 获取套装商品详情（已存在）
Task<ApiResponse<List<DomesticSetProductDto>>> GetSetItemsAsync(string productCode);

// 6. 更新套装商品信息（已存在）
Task<ApiResponse<object>> UpdateSetItemsAsync(string productCode, List<SetItemDto> items);
```

#### IDomesticSupplierService

```csharp
// 1. 获取启用的供应商列表（已存在）
Task<List<DomesticSupplierDto>> GetActiveSupplierListAsync();

// 2. 根据供应商编码获取详情（已存在）
Task<DomesticSupplierDto?> GetSupplierByCodeAsync(string supplierCode);
```

#### IProductPrefixCodeService

```csharp
// 1. 根据供应商编码获取前缀列表（已存在）
Task<ApiResponse<List<ProductPrefixCodeDto>>> GetPrefixesBySupplierCodeAsync(string supplierCode);

// 2. 创建前缀（已存在）
Task<ApiResponse<ProductPrefixCodeDto>> CreateProductPrefixCodeAsync(CreateProductPrefixCodeDto dto);

// 3. 更新前缀（已存在）
Task<ApiResponse<ProductPrefixCodeDto>> UpdateProductPrefixCodeAsync(string prefixCode, UpdateProductPrefixCodeDto dto);

// 4. 删除前缀（已存在）
Task<ApiResponse<object>> DeleteProductPrefixCodeAsync(string prefixCode);
```

---

## 🚀 部署步骤

### 1. 运行数据库迁移

```bash
# 连接到数据库
sqlcmd -S <服务器> -d <数据库> -U <用户名> -P <密码>

# 执行迁移脚本
:r BlazorApp.Api/Data/Migrations/20251022_CreateProductCreationLog.sql
GO
```

### 2. 编译项目

```bash
dotnet build BlazorApp.Api
dotnet build BlazorApp.Shared
```

### 3. 运行测试（可选）

```bash
dotnet test
```

### 4. 发布

```bash
dotnet publish BlazorApp.Api -c Release -o ./publish
```

---

## ✅ 测试清单

### 单元测试
- [ ] 批量验证逻辑测试
- [ ] 批量创建逻辑测试
- [ ] 创建日志记录测试
- [ ] 前缀CRUD测试

### 集成测试
- [ ] 完整创建流程测试
- [ ] 权限验证测试
- [ ] 错误处理测试
- [ ] 外键约束测试

### 性能测试
- [ ] 100件商品创建性能测试
- [ ] 1000件商品创建性能测试
- [ ] Grid数据加载性能测试
- [ ] 索引性能测试

---

## 📖 参考文档

- [需求文档](../../ReactUmi/my-app/docs/CREATE_PRODUCT_REQUIREMENTS.md)
- [设计文档](../../ReactUmi/my-app/docs/CREATE_PRODUCT_DESIGN.md)
- [API实现文档](../../ReactUmi/my-app/docs/CREATE_PRODUCT_API_IMPLEMENTATION.md)

---

**文档版本**: v1.0  
**创建日期**: 2025-10-22  
**维护者**: AI Assistant  
**状态**: 已完成

