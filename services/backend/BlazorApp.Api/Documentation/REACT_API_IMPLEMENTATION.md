# React AG Grid 专用 API 接口实现文档

## 📋 概述

本文档说明为 React 项目的 AG Grid Community 表格组件专门实现的后端 API 接口。这些接口**不影响现有的 Blazor 项目接口**，是单独新增的。

---

## ✅ 已实现的接口

### 1. 获取 AG Grid 表格数据（服务端模式）

**接口地址**: `POST /api/v1/domestic-products/grid`

**功能说明**:
- 支持服务端过滤（文本、数字、下拉多选）
- 支持服务端排序
- 支持服务端分页
- 自动处理 AG Grid 传递的 filterModel 和 sortModel

**请求示例**:
```json
{
  "startRow": 0,
  "endRow": 50,
  "pageSize": 50,
  "filterModel": {
    "name": {
      "filterType": "text",
      "type": "contains",
      "filter": "玩具"
    },
    "domesticPrice": {
      "filterType": "number",
      "type": "greaterThan",
      "filter": 10
    },
    "productType": {
      "filterType": "set",
      "values": ["普通商品", "套装商品"]
    }
  },
  "sortModel": [
    {
      "colId": "domesticPrice",
      "sort": "desc"
    }
  ]
}
```

**响应示例**:
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "id": 1,
        "productCode": "PRD001",
        "supplierCode": "SUP001",
        "supplierName": "桌妮商贸",
        "productName": "玩具车模型",
        "productNameEn": "Toy Car Model",
        "hbProductNo": "HB12345",
        "barcode": "6901234567890",
        "specifications": "10x5x3cm",
        "productType": 0,
        "domesticPrice": 12.50,
        "labelPrice": 15.00,
        "importPrice": 18.00,
        "packingQuantity": 100,
        "unitVolume": 0.0015,
        "unitGrossWeight": 0.05,
        "packingSize": "10x5x3",
        "material": "塑料",
        "remarks": "畅销商品",
        "isActive": true,
        "createdAt": "2025-01-20T10:00:00",
        "updatedAt": "2025-01-21T15:30:00"
      }
    ],
    "total": 1234
  },
  "message": null
}
```

**支持的过滤器类型**:

#### 文本过滤器 (filterType: "text")
- `equals` - 等于
- `notEqual` - 不等于
- `contains` - 包含
- `notContains` - 不包含
- `startsWith` - 开始于
- `endsWith` - 结束于
- `blank` - 为空
- `notBlank` - 不为空

**支持的文本字段**:
- `supplierCode` - 供应商编码
- `supplierName` - 供应商名称
- `name` - 商品名称
- `nameEn` - 英文名称
- `itemNumber` - HB货号
- `barcode` - 条形码
- `specs` - 商品规格
- `material` - 材质
- `remark` - 备注
- `packingSize` - 包装尺寸

#### 数字过滤器 (filterType: "number")
- `equals` - 等于
- `notEqual` - 不等于
- `lessThan` - 小于
- `lessThanOrEqual` - 小于等于
- `greaterThan` - 大于
- `greaterThanOrEqual` - 大于等于
- `inRange` - 介于（需要提供 filterTo）

**支持的数字字段**:
- `domesticPrice` - 国内价格
- `labelPrice` - 零售价
- `importPrice` - 进口价格
- `packingQty` - 装箱数
- `volume` - 单件体积
- `grossWeight` - 单件毛重

#### 集合过滤器 (filterType: "set")
**支持的字段**:
- `productType` - 商品类型（多选）
  - 可选值: `["普通商品", "套装商品", "贴牌商品", "进口商品"]`

---

### 2. 批量删除商品

**接口地址**: `DELETE /api/v1/domestic-products/batch-delete`

**功能说明**:
- 批量软删除商品
- 通过商品 ID 列表删除

**请求示例**:
```json
{
  "ids": [1, 2, 3, 4, 5]
}
```

**响应示例**:
```json
{
  "success": true,
  "message": "成功删除 5 件商品"
}
```

---

### 3. 获取套装商品子项列表

**接口地址**: `GET /api/v1/domestic-products/{id}/set-items`

**功能说明**:
- 获取指定套装商品包含的所有子项
- 返回子项的详细信息（商品名称、货号、条码、数量、单价等）

**请求示例**:
```
GET /api/v1/domestic-products/123/set-items
```

**响应示例**:
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "setProductCode": "SET001",
      "componentProductCode": "PRD001",
      "productId": 10,
      "productName": "玩具车模型",
      "itemNumber": "HB12345",
      "barcode": "6901234567890",
      "quantity": 5,
      "unitPrice": 12.50,
      "createdAt": "2025-01-20T10:00:00"
    },
    {
      "id": 2,
      "setProductCode": "SET001",
      "componentProductCode": "PRD002",
      "productId": 11,
      "productName": "积木套装",
      "itemNumber": "HB12346",
      "barcode": "6901234567891",
      "quantity": 3,
      "unitPrice": 25.00,
      "createdAt": "2025-01-20T10:00:00"
    }
  ],
  "message": null
}
```

---

### 4. 更新套装商品子项

**接口地址**: `PUT /api/v1/domestic-products/{id}/set-items`

**功能说明**:
- 更新套装商品的子项列表
- 采用"全量替换"策略：删除旧数据，插入新数据
- 使用事务保证数据一致性

**请求示例**:
```json
{
  "items": [
    {
      "productId": 10,
      "quantity": 5
    },
    {
      "productId": 11,
      "quantity": 3
    }
  ]
}
```

**响应示例**:
```json
{
  "success": true,
  "message": "保存成功"
}
```

---

## 📁 新增的文件

### 1. DTO 类

#### `AgGridRequestDto.cs`
```csharp
// AG Grid 请求相关的 DTO
- AgGridRequestDto          // 主请求类
- AgGridFilterModel         // 过滤器模型
- AgGridSortModel           // 排序模型
- AgGridResponseDto<T>      // 响应类
- AgGridDataDto<T>          // 数据包装类
```

#### `BatchDeleteRequestDto.cs`
```csharp
// 批量删除请求 DTO
public class BatchDeleteRequestDto
{
    public List<int> Ids { get; set; }
}
```

#### `UpdateSetItemsRequestDto.cs`
```csharp
// 更新套装商品子项请求 DTO
public class UpdateSetItemsRequestDto
{
    public List<SetItemUpdateDto> Items { get; set; }
}

public class SetItemUpdateDto
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
```

### 2. Controller 方法

在 `DomesticProductsController.cs` 中新增：
- `GetGridData` - AG Grid 数据查询
- `BatchDelete` - 批量删除
- `GetSetItems` - 获取套装子项
- `UpdateSetItems` - 更新套装子项

### 3. Service 方法

在 `DomesticProductService.cs` 中新增：
- `GetGridDataAsync` - AG Grid 数据查询实现
- `ApplyAgGridFilters` - 应用过滤器
- `ApplyTextFilter` - 应用文本过滤器
- `ApplyNumberFilter` - 应用数字过滤器
- `ApplySetFilter` - 应用集合过滤器
- `ApplyAgGridSorts` - 应用排序
- `BatchDeleteAsync` - 批量删除实现
- `GetSetItemsAsync` - 获取套装子项实现
- `UpdateSetItemsAsync` - 更新套装子项实现

---

## 🔒 权限要求

所有接口都需要用户登录（`[Authorize]`）：

| 接口 | 角色要求 |
|------|----------|
| `POST /api/v1/domestic-products/grid` | Admin, WarehouseManager |
| `DELETE /api/v1/domestic-products/batch-delete` | Admin |
| `GET /api/v1/domestic-products/{id}/set-items` | Admin, WarehouseManager |
| `PUT /api/v1/domestic-products/{id}/set-items` | Admin |

---

## 🔧 技术实现细节

### 1. 过滤器转 SQL

**示例 1: 文本过滤 - "包含"**
```csharp
// 前端传递
{
  "name": {
    "filterType": "text",
    "type": "contains",
    "filter": "玩具"
  }
}

// 后端转换为
query.Where(p => !SqlFunc.IsNullOrEmpty(p.ProductName) 
    && p.ProductName.Contains("玩具"))
```

**示例 2: 数字过滤 - "大于"**
```csharp
// 前端传递
{
  "domesticPrice": {
    "filterType": "number",
    "type": "greaterThan",
    "filter": 10
  }
}

// 后端转换为
query.Where(p => p.DomesticPrice > 10)
```

**示例 3: 数字过滤 - "介于"**
```csharp
// 前端传递
{
  "domesticPrice": {
    "filterType": "number",
    "type": "inRange",
    "filter": 10,
    "filterTo": 50
  }
}

// 后端转换为
query.Where(p => p.DomesticPrice >= 10 && p.DomesticPrice <= 50)
```

**示例 4: 集合过滤 - "商品类型"**
```csharp
// 前端传递
{
  "productType": {
    "filterType": "set",
    "values": ["普通商品", "套装商品"]
  }
}

// 后端转换为
query.Where(p => typeNames.Contains(p.ProductType.ToString()))
```

### 2. 排序转 SQL

```csharp
// 前端传递
[
  {
    "colId": "domesticPrice",
    "sort": "desc"
  }
]

// 后端转换为
query.OrderBy(p => p.DomesticPrice, OrderByType.Desc)
```

### 3. 分页实现

```csharp
// 前端传递
{
  "startRow": 0,
  "endRow": 50,
  "pageSize": 50
}

// 后端实现
query.Skip(0).Take(50)
```

---

## 📊 性能优化

### 1. 使用左连接查询供应商信息
```csharp
var query = db.Queryable<DomesticProduct>()
    .LeftJoin<ChinaSupplier>((p, s) => p.SupplierCode == s.Code)
    .Where(p => !p.IsDeleted);
```

### 2. 只查询需要的字段
```csharp
.Select((p, s) => new DomesticProductDto
{
    Id = p.Id,
    ProductCode = p.ProductCode,
    // ... 只选择需要的字段
})
```

### 3. 使用异步查询
```csharp
await query.ToListAsync();
await query.CountAsync();
```

### 4. 使用软删除过滤
```csharp
.Where(p => !p.IsDeleted)
```

---

## 🧪 测试示例

### 测试 1: 基础查询
```bash
curl -X POST http://localhost:5001/api/v1/domestic-products/grid \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "startRow": 0,
    "endRow": 10,
    "pageSize": 10,
    "filterModel": {},
    "sortModel": []
  }'
```

### 测试 2: 文本过滤
```bash
curl -X POST http://localhost:5001/api/v1/domestic-products/grid \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "startRow": 0,
    "endRow": 10,
    "pageSize": 10,
    "filterModel": {
      "name": {
        "filterType": "text",
        "type": "contains",
        "filter": "玩具"
      }
    },
    "sortModel": []
  }'
```

### 测试 3: 批量删除
```bash
curl -X DELETE http://localhost:5001/api/v1/domestic-products/batch-delete \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{
    "ids": [1, 2, 3]
  }'
```

---

## ⚠️ 注意事项

### 1. 与现有接口的关系
- ✅ **完全独立** - 新接口不影响现有 Blazor 项目的接口
- ✅ **路由不冲突** - 使用不同的路由路径（`/grid`, `/batch-delete`, `/{id}/set-items`）
- ✅ **兼容性** - 使用相同的数据模型和服务层

### 2. 数据一致性
- 使用软删除（`IsDeleted = true`）而非物理删除
- 套装子项更新使用事务保证数据一致性
- 所有更新操作都记录 `UpdatedAt` 时间戳

### 3. 权限控制
- 所有接口都需要登录
- 删除操作仅 Admin 可用
- 查询操作 Admin 和 WarehouseManager 可用

---

## 📝 前后端字段映射

| React 前端字段 | 后端 DTO 字段 | 数据库字段 | 说明 |
|---------------|--------------|-----------|------|
| `id` | `Id` | `Id` | 商品ID（主键） |
| `supplierCode` | `SupplierCode` | `SupplierCode` | 供应商编码 |
| `supplierName` | `SupplierName` | - | 供应商名称（关联查询） |
| `name` | `ProductName` | `ProductName` | 商品名称 |
| `nameEn` | `ProductNameEn` | `ProductNameEn` | 英文名称 |
| `itemNumber` | `HBProductNo` | `HBProductNo` | HB货号 |
| `barcode` | `Barcode` | `Barcode` | 条形码 |
| `specs` | `Specifications` | `Specifications` | 商品规格 |
| `productType` | `ProductType` | `ProductType` | 商品类型（枚举） |
| `domesticPrice` | `DomesticPrice` | `DomesticPrice` | 国内价格 |
| `labelPrice` | `LabelPrice` | `LabelPrice` | 零售价 |
| `importPrice` | `ImportPrice` | `ImportPrice` | 进口价格 |
| `packingQty` | `PackingQuantity` | `PackingQuantity` | 装箱数 |
| `volume` | `UnitVolume` | `UnitVolume` | 单件体积 |
| `grossWeight` | `UnitGrossWeight` | `UnitGrossWeight` | 单件毛重 |
| `packingSize` | `PackingSize` | `PackingSize` | 包装尺寸 |
| `material` | `Material` | `Material` | 材质 |
| `remark` | `Remarks` | `Remarks` | 备注 |

---

## ✅ 实现完成清单

- ✅ AG Grid 服务端数据查询接口
- ✅ 文本过滤器（7种操作符）
- ✅ 数字过滤器（7种操作符）
- ✅ 集合过滤器（下拉多选）
- ✅ 排序功能
- ✅ 分页功能
- ✅ 批量删除接口
- ✅ 套装商品子项查询接口
- ✅ 套装商品子项更新接口
- ✅ 权限控制
- ✅ 错误处理
- ✅ 日志记录
- ✅ 事务支持（套装子项更新）

---

## 🧩 多码套装商品（ProductSetCode）React 接口

### 列表与网格数据

**接口地址**: `POST /api/react/v1/product-set-codes/grid`

**字段**:

| 字段 | 说明 |
|------|------|
| `supplierName` | 供应商名称（本地供应商） |
| `itemNumber` | 商品货号（Product.ItemNumber） |
| `barcode` | 主条码（Product.Barcode） |
| `setItemNumber` | 套装货号（ProductSetCode.SetItemNumber） |
| `setBarcode` | 套装条码（ProductSetCode.SetBarcode） |
| `setPurchasePrice` | 进货价 |
| `setRetailPrice` | 零售价 |
| `updatedAt` | 更新日期 |
| `updatedBy` | 更新人 |

**过滤/排序/分页**: 复用 `GridRequestDto` 与 `GridResponseDto<T>`

### 批量操作

**批量启用/禁用**: `PUT /api/react/v1/product-set-codes/batch-status`

请求体：`{ "ids": string[], "isActive": boolean }`

**批量更新价格**: `PUT /api/react/v1/product-set-codes/batch-prices`

请求体：`{ "items": [{ "id": string, "setPurchasePrice"?: number, "setRetailPrice"?: number }] }`

**批量删除**: `DELETE /api/react/v1/product-set-codes/batch-delete`

请求体：`{ "ids": string[] }`

### 权限

| 接口 | 角色要求 |
|------|----------|
| `POST /api/react/v1/product-set-codes/grid` | Admin, WarehouseManager |
| `PUT /api/react/v1/product-set-codes/batch-*` | Admin |

### 字段来源

| 前端字段 | 模型路径 |
|----------|----------|
| `updatedAt`, `updatedBy` | BlazorApp.Shared/Models/HBweb/BaseEntity.cs |
| `itemNumber`, `barcode`, `localSupplierCode` | BlazorApp.Shared/Models/HBweb/Product.cs |
| `setItemNumber`, `setBarcode`, `setPurchasePrice`, `setRetailPrice`, `isActive` | BlazorApp.Shared/Models/HBweb/ProductSetCode.cs |
| `supplierName` | BlazorApp.Shared/Models/HBweb/LocalSupplier.cs |

---

## 🚀 后续扩展建议

### 1. 批量更新接口
```csharp
[HttpPut("batch-update")]
public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateRequestDto request)
```

### 2. Excel 导入接口
```csharp
[HttpPost("import")]
public async Task<IActionResult> ImportExcel([FromForm] IFormFile file)
```

### 3. 快速搜索接口（跨字段搜索）
```csharp
[HttpGet("quick-search")]
public async Task<IActionResult> QuickSearch([FromQuery] string keyword)
```

### 4. 统计接口
```csharp
[HttpGet("statistics")]
public async Task<IActionResult> GetStatistics()
```

---

**文档版本**: v1.0.0  
**创建时间**: 2025-01-21  
**状态**: ✅ 已完成并测试通过
