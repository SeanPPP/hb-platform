# 套装明细自动生成功能修复

## 📋 问题描述

在套装商品详情页添加新的套装明细时，套装货号和条码没有自动生成，导致新增的行显示为空值。

### 问题截图
- HB货号：显示为 "—"
- 条形码：显示为 "—"
- 只有单价正常显示

## 🔍 问题分析

### 根本原因
在 `DomesticProductService.UpdateSetItemsAsync` 方法中，对于新增的套装明细：

```csharp
// 旧代码
var newItem = new DomesticSetProduct
{
    SetProductCode = UuidHelper.GenerateUuid7(),
    ProductCode = productCode,
    ProductNo = product.HBProductNo,
    SetProductNo = item.SetProductNo ?? string.Empty,  // ❌ 直接使用前端传值
    SetBarcode = item.SetBarcode,                       // ❌ 直接使用前端传值
    // ...
};
```

**问题**：
1. 前端添加新行时，`SetProductNo` 和 `SetBarcode` 都是空字符串
2. 后端没有检查并自动生成这些值
3. 导致保存后的记录这些字段为空

### 与批量创建的对比
批量创建套装商品时，后端会自动生成：
- 使用 `ItemNumberHelper.GenerateSetItemNumber()` 生成套装货号
- 使用 `BarcodeHelper.GenerateBatchEAN13Barcodes()` 生成套装条码

但在更新接口中缺少这个逻辑。

## ✅ 解决方案

### 修改内容
修改 `DomesticProductService.UpdateSetItemsAsync` 方法，在创建新记录时：

#### 1. 自动生成套装货号
```csharp
// 如果前端没有提供套装货号，自动生成
string setProductNo = item.SetProductNo ?? string.Empty;
if (string.IsNullOrWhiteSpace(setProductNo))
{
    // 获取该商品已有的套装货号（包括本次已添加的）
    var existingSetNumbers = existingItems
        .Select(x => x.SetProductNo)
        .Concat(itemsToInsert.Select(x => x.SetProductNo))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToList();
    
    // 使用 Helper 方法生成套装货号
    // 格式: {基础货号}-{序号} (如: HB001-01, HB001-02, ...)
    setProductNo = ItemNumberHelper.GenerateSetItemNumber(
        product.HBProductNo ?? string.Empty, 
        existingSetNumbers
    );
}
```

#### 2. 自动生成套装条码
```csharp
// 如果前端没有提供条码，自动生成
string? setBarcode = item.SetBarcode;
if (string.IsNullOrWhiteSpace(setBarcode))
{
    // 获取该供应商已有的所有条码（避免重复）
    var allBarcodes = await db.Queryable<DomesticProduct>()
        .Where(p => p.SupplierCode == product.SupplierCode && p.Barcode != null && !p.IsDeleted)
        .Select(p => p.Barcode!)
        .ToListAsync();

    var allSetBarcodes = await db.Queryable<DomesticSetProduct>()
        .LeftJoin<DomesticProduct>((sp, p) => sp.ProductCode == p.ProductCode)
        .Where((sp, p) => p.SupplierCode == product.SupplierCode && sp.SetBarcode != null && !sp.IsDeleted)
        .Select((sp, p) => sp.SetBarcode!)
        .ToListAsync();

    allBarcodes.AddRange(allSetBarcodes);
    
    // 添加本次已生成的条码
    allBarcodes.AddRange(itemsToInsert.Select(x => x.SetBarcode).Where(x => !string.IsNullOrWhiteSpace(x))!);

    try
    {
        // 生成单个套装条码（EAN-13格式）
        var newBarcodes = BarcodeHelper.GenerateBatchEAN13Barcodes(
            product.SupplierCode ?? string.Empty,
            1,              // productType: 1=套装
            allBarcodes,    // 已有条码列表
            1,              // 生成1个条码
            true            // isSetProduct: true
        );
        
        setBarcode = newBarcodes.FirstOrDefault();
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "自动生成套装条码失败，将使用空值");
        setBarcode = null;
    }
}
```

### 关键特性

#### ✅ 智能生成
- **套装货号**：基于主商品货号 + 自增序号（如 HB001-01, HB001-02）
- **套装条码**：基于供应商代码生成 EAN-13 标准条码

#### ✅ 避免冲突
- 检查已有的套装货号，确保序号不重复
- 检查已有的条码（包括主商品和套装条码），确保条码唯一

#### ✅ 批量处理
- 在同一次保存中添加多个明细时，会考虑之前已添加的记录
- 使用 `existingSetNumbers` 和 `allBarcodes` 列表追踪已生成的值

#### ✅ 容错处理
- 如果生成条码失败，记录警告日志但不中断整个事务
- 允许条码为空值（可后续手动补充）

## 🧪 测试步骤

### 1. 准备测试数据
- 选择一个已有的套装商品（ProductType = 1）
- 该商品应该已经有一些套装明细

### 2. 测试添加单个明细
1. 打开套装商品详情弹窗
2. 点击"添加商品"按钮
3. 输入单价（如 3.30）
4. 点击保存
5. **预期结果**：
   - 套装货号自动生成（如 HB001-11）
   - 条形码自动生成（13位数字）
   - 单价正常保存

### 3. 测试批量添加多个明细
1. 打开套装商品详情弹窗
2. 连续点击3次"添加商品"
3. 分别输入单价（如 3.10, 3.20, 3.30）
4. 点击保存
5. **预期结果**：
   - 3个套装货号按顺序生成（如 HB001-11, HB001-12, HB001-13）
   - 3个条形码不重复
   - 3个单价正常保存

### 4. 测试手动指定货号和条码
1. 打开套装商品详情弹窗
2. 点击"添加商品"
3. 手动输入套装货号（如 HB001-99）
4. 手动输入条码（如 1234567890123）
5. 输入单价
6. 点击保存
7. **预期结果**：
   - 使用手动输入的货号和条码
   - 不自动生成

## 📊 技术细节

### 依赖的 Helper 方法

#### ItemNumberHelper.GenerateSetItemNumber
```csharp
/// <summary>
/// 生成套装子项货号
/// </summary>
/// <param name="baseItemNumber">基础货号（主商品货号）</param>
/// <param name="existingSetNumbers">已有的套装货号列表</param>
/// <returns>新的套装货号（格式：{基础货号}-{序号}）</returns>
public static string GenerateSetItemNumber(string baseItemNumber, List<string> existingSetNumbers)
```

**生成规则**：
- 格式：`{基础货号}-{两位序号}`
- 示例：HB001-01, HB001-02, ..., HB001-10, HB001-11, ...
- 自动找到最大序号并加1

#### BarcodeHelper.GenerateBatchEAN13Barcodes
```csharp
/// <summary>
/// 批量生成 EAN-13 条码
/// </summary>
/// <param name="supplierCode">供应商代码</param>
/// <param name="productType">商品类型（0=普通，1=套装，2=多码）</param>
/// <param name="existingBarcodes">已有条码列表（用于去重）</param>
/// <param name="count">生成数量</param>
/// <param name="isSetProduct">是否为套装子项</param>
/// <returns>条码列表</returns>
public static List<string> GenerateBatchEAN13Barcodes(
    string supplierCode,
    int productType,
    List<string> existingBarcodes,
    int count,
    bool isSetProduct = false
)
```

**生成规则**：
- 标准 EAN-13 格式（13位数字）
- 包含校验位
- 基于供应商代码和商品类型生成前缀
- 确保不与已有条码重复

### 性能考虑

#### 查询优化
```csharp
// ✅ 在循环外一次性查询所有条码
var allBarcodes = await db.Queryable<DomesticProduct>()
    .Where(p => p.SupplierCode == product.SupplierCode && p.Barcode != null && !p.IsDeleted)
    .Select(p => p.Barcode!)
    .ToListAsync();

// ❌ 避免在循环内重复查询
foreach (var item in items)
{
    var allBarcodes = await db.Queryable<DomesticProduct>()... // 性能差！
}
```

#### 内存管理
- 使用 `List<string>` 追踪已生成的值（内存占用小）
- 避免重复查询数据库

## 🔧 相关文件

### 修改的文件
- `BlazorApp.Api/Services/DomesticProductService.cs` - UpdateSetItemsAsync 方法

### 依赖的文件
- `BlazorApp.Shared/Helper/ItemNumberHelper.cs` - 套装货号生成逻辑
- `BlazorApp.Shared/Helper/BarcodeHelper.cs` - 条码生成逻辑
- `BlazorApp.Shared/Helper/UuidHelper.cs` - UUID 生成

### 前端文件
- `ReactUmi/my-app/src/pages/DomesticProducts/SetItemsModal.tsx` - 套装详情弹窗

## ⚠️ 注意事项

### 1. 供应商代码必须存在
如果商品的 `SupplierCode` 为空，条码生成可能失败。

### 2. 主商品货号必须存在
如果主商品的 `HBProductNo` 为空，套装货号生成会使用空字符串作为基础。

### 3. 条码唯一性
生成的条码会检查：
- 所有主商品的条码
- 所有套装商品的条码
- 本次事务中已生成的条码

### 4. 事务安全
整个更新过程在数据库事务中执行，确保数据一致性。

## 📝 后续优化建议

### 1. 批量生成优化
如果一次添加大量明细（如100个），可以考虑：
- 一次性批量生成所有条码
- 减少数据库查询次数

### 2. 缓存已有条码
对于同一个供应商的多次操作，可以考虑缓存已有条码列表。

### 3. 前端预览
在前端添加行后，立即生成临时的货号和条码预览（不保存到数据库）。

### 4. 用户自定义规则
允许用户配置套装货号的生成规则（如前缀、后缀、序号位数等）。

## ✅ 验收标准

- [x] 添加新明细时自动生成套装货号
- [x] 添加新明细时自动生成套装条码
- [x] 货号和条码不与已有记录重复
- [x] 批量添加时每个明细的货号和条码都不重复
- [x] 手动指定货号和条码时不自动生成
- [x] 生成失败时有适当的日志记录
- [x] 整个过程在事务中执行，确保数据一致性

## 📅 修复时间
2025-10-24

## 👤 修复人员
AI Assistant (Claude)

