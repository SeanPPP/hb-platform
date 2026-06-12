# 图片URL重复问题修复指南

## 问题描述

在系统中发现了图片URL重复拼接的问题，例如：
```
错误的URL:
https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/YW200/https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/YW200/MA006-4.jpg

正确的URL:
https://hb-sales-2019-1300114625.cos.ap-singapore.myqcloud.com/YW200/MA006-4.jpg
```

## 问题根源

问题出现在以下几个位置，当`HB货号`字段被错误地赋值为完整URL而不是简单的货号时，系统会将URL拼接两次：

1. **DomesticProductMappingProfile.cs** (第94-98行)
2. **DomesticProductService.cs** (第1441-1448行, 第1781-1789行, 第2218-2226行)
3. **DataSyncService.cs** (第3652-3661行, 第3712-3722行)

## 解决方案

### 1. 预防措施（已实施）

在所有生成图片URL的地方添加了检查，确保`HB货号`不是完整URL：

```csharp
// 确保HBProductNo不是完整的URL，避免重复拼接
if (string.IsNullOrWhiteSpace(product.ProductImage) 
    && !string.IsNullOrWhiteSpace(product.HBProductNo)
    && !product.HBProductNo.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
    && !product.HBProductNo.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    product.ProductImage = $"https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/{product.HBProductNo}.jpg";
}
```

### 2. 辅助工具类

创建了`ImageUrlHelper`工具类（`BlazorApp.Api/Utils/ImageUrlHelper.cs`），提供以下功能：

- **IsUrl(string?)**: 检查字符串是否为URL
- **FixDuplicateUrl(string?)**: 修复重复的URL
- **GenerateImageUrl(string?)**: 从货号安全生成图片URL
- **ExtractProductNoFromUrl(string?)**: 从URL提取货号
- **ValidateAndFixImageUrl(string?, string?)**: 验证并修复图片URL

### 3. 数据修复API端点

创建了管理员专用的API端点来修复数据库中已存在的重复URL：

**端点**: `POST /api/v1/DomesticProducts/fix-duplicate-image-urls`

**权限**: 仅限Admin角色

**参数**:
- `dryRun` (可选，默认为true): 是否仅模拟运行，不实际修改数据

## 使用方法

### 步骤1：模拟运行（推荐先执行）

使用Postman或其他API工具调用：

```http
POST /api/v1/DomesticProducts/fix-duplicate-image-urls?dryRun=true
Authorization: Bearer {YOUR_ADMIN_TOKEN}
```

**响应示例**:
```json
{
  "success": true,
  "message": "模拟运行完成：扫描 1000 个商品，发现 5 个重复URL",
  "data": {
    "totalScanned": 1000,
    "problemsFound": 5,
    "successfullyFixed": 5,
    "failedToFix": 0,
    "isDryRun": true,
    "details": [
      {
        "productCode": "01234567-89ab-cdef-0123-456789abcdef",
        "hbProductNo": "MA006-4",
        "productName": "测试商品",
        "originalImageUrl": "https://domain.com/YW200/https://domain.com/YW200/MA006-4.jpg",
        "fixedImageUrl": "https://domain.com/YW200/MA006-4.jpg",
        "isSuccess": true,
        "errorMessage": null
      }
    ]
  }
}
```

### 步骤2：实际执行修复

确认模拟运行结果无误后，执行实际修复：

```http
POST /api/v1/DomesticProducts/fix-duplicate-image-urls?dryRun=false
Authorization: Bearer {YOUR_ADMIN_TOKEN}
```

**响应示例**:
```json
{
  "success": true,
  "message": "修复完成：扫描 1000 个商品，发现 5 个重复URL，成功修复 5 个，失败 0 个",
  "data": {
    "totalScanned": 1000,
    "problemsFound": 5,
    "successfullyFixed": 5,
    "failedToFix": 0,
    "isDryRun": false,
    "details": [...]
  }
}
```

## 验证修复结果

修复完成后，可以通过以下方式验证：

1. **检查数据库**:
```sql
SELECT ProductCode, HBProductNo, ProductImage
FROM DomesticProduct
WHERE ProductImage LIKE '%http%http%' OR ProductImage LIKE '%https%https%'
```

2. **查看前端显示**:
打开货柜明细页面（ContainerDetail.razor），检查商品图片是否正常显示。

## 注意事项

1. ⚠️ 此操作会修改数据库中的商品图片URL，请务必先执行模拟运行（dryRun=true）
2. ⚠️ 建议在执行实际修复前备份数据库
3. ⚠️ 仅Admin角色可以执行此操作
4. ⚠️ 执行过程中会记录详细的日志，可在服务器日志中查看

## 后续维护

- 定期检查是否有新的重复URL产生
- 确保在数据导入/同步时遵循新的URL生成规则
- 如果发现HB货号字段被错误赋值为URL，需要在数据源头修正

## 技术支持

如有问题，请联系HB Platform开发团队。

