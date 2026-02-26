# 货柜管理系统模型文档

## 概述

本文档描述了HBweb系统中的货柜管理模型，包括货柜主表(Container)和货柜明细表(ContainerDetail)。这些模型参考了HQ数据库的货柜表字段结构，并结合HBweb系统的业务需求进行了优化设计。

## 模型结构

### 1. Container（货柜主表）

**表名**: `Container`  
**主键**: `ContainerCode` (string, UUID7格式)  
**继承**: `BaseEntity`

#### 主要字段

| 字段名 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| ContainerCode | string | 货柜编码（主键，UUID7） | 01234567-89ab-cdef... |
| ContainerNumber | string | 货柜编号（业务编号） | HG2024001 |
| LoadingDate | DateTime? | 装柜日期 | 2024-01-15 |
| EstimatedArrivalDate | DateTime? | 预计到岸日期 | 2024-02-15 |
| ActualArrivalDate | DateTime? | 实际到货日期 | 2024-02-18 |
| TotalPieces | decimal? | 合计件数 | 1200.50 |
| TotalQuantity | decimal? | 合计数量 | 25000.00 |
| TotalAmount | decimal? | 合计金额（人民币） | 158000.00 |
| TotalVolume | decimal? | 总体积（立方米） | 65.250 |
| CostFloatRate | decimal? | 成本浮率 | 1.0500 |
| ExchangeRate | decimal? | 汇率 | 7.2500 |
| ShippingFee | decimal? | 运费 | 8500.00 |
| Status | int? | 货柜状态 | 2 |
| Remarks | string? | 备注 | 特殊要求... |
| Remarks2 | string? | 备注2 | 内部记录... |

#### 状态枚举

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | 草稿 | Draft |
| 1 | 已确认 | Confirmed |
| 2 | 已装柜 | Loaded |
| 3 | 运输中 | Shipping |
| 4 | 已到港 | Arrived |
| 5 | 已清关 | Cleared |
| 6 | 已完成 | Completed |
| 7 | 已取消 | Cancelled |

#### 计算属性

- `StatusDisplayName`: 状态的中文显示名称
- `IsCompleted`: 是否已完成（状态=6）
- `IsEditable`: 是否可编辑（状态=0或1）
- `ShippingDays`: 运输天数（装柜到到货）
- `LoadingRate`: 装载率（实际体积/标准容量×100%）

### 2. ContainerDetail（货柜明细表）

**表名**: `ContainerDetail`  
**主键**: `DetailCode` (string, UUID7格式)  
**外键**: `ContainerCode` → Container.ContainerCode  
**继承**: `BaseEntity`

#### 主要字段

| 字段名 | 类型 | 说明 | 示例 |
|--------|------|------|------|
| DetailCode | string | 明细编码（主键，UUID7） | 01234567-89ab-cdef... |
| ContainerCode | string | 货柜编码（外键） | 01234567-89ab-cdef... |
| ProductCode | string? | 商品编码（外键） | 01234567-89ab-cdef... |
| LoadingType | string? | 装柜类型 | 单品/套装/混装/散装 |
| MixedGroupCode | string? | 混装组编码 | MIX001 |
| ProductType | string? | 商品类型 | 普通商品/套装商品 |
| SetQuantity | decimal? | 套装数量 | 2.5 |
| LoadingPieces | decimal? | 装柜件数 | 100.0 |
| LoadingQuantity | decimal? | 装柜数量 | 2400.0 |
| DomesticPrice | decimal? | 国内价格 | 65.50 |
| AdjustmentRate | decimal? | 调整浮率 | 1.0500 |
| ImportPrice | decimal? | 进口价格 | 89.00 |
| OEMPrice | decimal? | 贴牌价格 | 78.00 |
| PackingQuantity | decimal? | 单件装箱数 | 24.0 |
| UnitVolume | decimal? | 单件体积 | 0.025 |
| TotalAmount | decimal? | 合计装柜金额 | 157200.00 |
| TotalVolume | decimal? | 合计装柜体积 | 60.000 |
| TransportCost | decimal? | 运输成本 | 350.00 |
| Status | int? | 明细状态 | 2 |
| Remarks | string? | 备注 | 特殊处理... |

#### 明细状态枚举

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | 正常 | Normal |
| 1 | 已确认 | Confirmed |
| 2 | 已装柜 | Loaded |
| 3 | 已到货 | Arrived |
| 4 | 已入库 | Stored |
| 5 | 异常 | Exception |
| 6 | 已取消 | Cancelled |

#### 计算属性

- `StatusDisplayName`: 状态的中文显示名称
- `ActualUnitPrice`: 实际单价（考虑调整浮率）
- `CalculatedTotalAmount`: 计算总金额（数量×实际单价）
- `CalculatedTotalVolume`: 计算总体积（数量×单件体积）
- `ProfitRate`: 利润率（基于进口价格）
- `HasException`: 是否存在异常
- `PackageBoxes`: 包装箱数（向上取整）

#### 业务方法

- `UpdateCalculatedFields()`: 更新计算字段
- `ValidateData()`: 验证数据完整性

## 导航属性

### Container
- `Details`: List<ContainerDetail> - 货柜明细列表（一对多）

### ContainerDetail
- `Container`: Container? - 所属货柜（多对一）
- `Product`: DomesticProduct? - 关联商品（多对一）

## 设计特点

### 1. 继承BaseEntity
- 自动包含创建时间、更新时间、创建者、更新者等审计字段
- 支持软删除功能

### 2. UUID7主键
- 使用UUID7格式确保全局唯一性
- 支持分布式系统和数据同步

### 3. 精确的数值类型
- 金额字段：2位小数精度
- 体积字段：3位小数精度
- 浮率字段：4位小数精度

### 4. 丰富的计算属性
- 提供业务逻辑相关的计算字段
- 简化前端显示和业务处理

### 5. 完善的导航关系
- 支持SqlSugar的导航属性
- 便于数据查询和关联操作

### 6. 状态管理
- 明确的状态流转
- 支持业务流程控制

### 7. 数据验证
- 内置数据验证方法
- 确保数据完整性和一致性

## 使用示例

### 创建货柜
```csharp
var container = new Container
{
    ContainerNumber = "HG2024001",
    LoadingDate = DateTime.Now,
    EstimatedArrivalDate = DateTime.Now.AddDays(30),
    Status = 0 // 草稿状态
};
```

### 添加货柜明细
```csharp
var detail = new ContainerDetail
{
    ContainerCode = container.ContainerCode,
    ProductCode = "product-uuid",
    LoadingQuantity = 1000m,
    DomesticPrice = 65.50m,
    UnitVolume = 0.025m
};
detail.UpdateCalculatedFields(); // 自动计算总金额和总体积
```

### 数据验证
```csharp
var (isValid, errors) = detail.ValidateData();
if (!isValid)
{
    foreach (var error in errors)
    {
        Console.WriteLine($"验证错误: {error}");
    }
}
```

## 后续开发建议

1. **创建对应的DTO类** - 用于API数据传输
2. **实现业务服务类** - 处理货柜相关的业务逻辑
3. **创建API控制器** - 提供RESTful API接口
4. **开发前端页面** - 货柜管理界面
5. **添加数据同步** - 与HQ系统的数据同步机制

## 注意事项

1. 所有的decimal字段都支持null值，需要在业务逻辑中进行空值检查
2. 状态字段使用int类型，建议创建对应的枚举类来管理状态值
3. 导航属性在使用时需要通过SqlSugar的Include方法来加载关联数据
4. UUID7主键生成依赖UuidHelper.GenerateUuid7()方法，需要确保该方法可用
5. 计算属性不会自动更新，需要在数据变更时手动调用UpdateCalculatedFields()方法
