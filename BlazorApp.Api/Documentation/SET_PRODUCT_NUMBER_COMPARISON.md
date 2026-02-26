# 套装商品货号格式对比

## 概述

系统中存在两种套装商品货号生成方式，本文档说明两者的区别和使用场景。

## 方案一：通用套装格式（现有）

### 实现位置
- **Helper**: `ItemNumberHelper.GenerateSetItemNumber`
- **格式**: `{基础货号}-{序号}`

### 示例
```
基础货号: HB001

生成的套装货号:
HB001-01
HB001-02
HB001-03
...
HB001-10
```

### 特点
- ✅ 简洁明了
- ✅ 节省字符长度
- ❌ 无法区分套装类型
- ❌ 不同套装规格会混在一起

### 适用场景
- 只有一种套装规格的商品
- 不需要区分套装类型的场景
- 货号长度有限制的情况

## 方案二：带类型标识格式（新增）

### 实现位置
- **Service**: `DomesticProductService.GenerateNextSetProductNoAsync`
- **格式**: `{基础货号}-S{套装类型}-{序号}`

### 示例
```
基础货号: HB001

套10:
HB001-S10-01
HB001-S10-02
...
HB001-S10-10

套15:
HB001-S15-01
HB001-S15-02
...
HB001-S15-15

套20:
HB001-S20-01
HB001-S20-02
...
HB001-S20-20
```

### 特点
- ✅ 清晰标识套装类型
- ✅ 不同套装规格不会冲突
- ✅ 便于筛选和查询
- ❌ 货号较长

### 适用场景
- 同一商品有多种套装规格（套10、套15等）
- 需要区分套装类型的场景
- 批量创建不同规格套装商品

## 推荐使用

### 对于批量创建套装商品功能
**推荐使用方案二**（带类型标识格式）

原因：
1. **需求明确**: 用户明确要求区分套10、套15
2. **避免冲突**: 同一商品可能同时有套10和套15
3. **便于管理**: 从货号就能看出套装类型
4. **查询方便**: 可以按套装类型筛选

### 示例对比

#### 场景：商品A同时有套10和套15

**方案一（可能混淆）**：
```
HB001-01  ← 这是套10的第1个？还是套15的第1个？
HB001-02
...
HB001-25  ← 无法区分
```

**方案二（清晰明确）**：
```
HB001-S10-01  ← 套10的第1个
HB001-S10-02
...
HB001-S10-10  ← 套10的第10个
HB001-S15-01  ← 套15的第1个
HB001-S15-02
...
HB001-S15-15  ← 套15的第15个
```

## 兼容性

两种方案可以共存，互不影响：
- 旧的套装商品可以继续使用方案一
- 新的批量创建功能使用方案二
- 系统可以同时支持两种格式

## 实现建议

### 如果要统一使用方案二

可以在 `ItemNumberHelper` 中添加新方法：

```csharp
/// <summary>
/// 生成带类型标识的套装商品货号
/// </summary>
public static string GenerateSetItemNumberWithType(
    string baseItemNumber, 
    int setType, 
    int setIndex)
{
    if (string.IsNullOrWhiteSpace(baseItemNumber))
        throw new ArgumentException("基础商品货号不能为空");
    
    if (setType < 1 || setType > 50)
        throw new ArgumentException("套装类型必须在1-50之间");
    
    if (setIndex < 1 || setIndex > setType)
        throw new ArgumentException($"套装序号必须在1-{setType}之间");
    
    return $"{baseItemNumber}-S{setType}-{setIndex:D2}";
}
```

### 如果要统一使用方案一

修改 `BatchCreateSetProductsAsync` 中的调用：

```csharp
// 获取该商品已有的套装货号
var existingSetNumbers = await db.Queryable<DomesticSetProduct>()
    .Where(sp => sp.ProductCode == productCode)
    .Select(sp => sp.SetProductNo)
    .ToListAsync();

// 使用现有的 Helper 方法生成
var setProductNo = ItemNumberHelper.GenerateSetItemNumber(
    productNo, 
    existingSetNumbers
);
```

## 结论

**建议保持当前实现**（方案二），因为：
1. 符合原始需求
2. 格式更清晰
3. 避免潜在冲突
4. 便于后续维护

如果未来需要简化格式，可以通过配置开关在两种方案间切换。

