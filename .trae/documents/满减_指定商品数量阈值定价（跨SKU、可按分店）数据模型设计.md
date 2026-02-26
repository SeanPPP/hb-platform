## 需求理解与原则
- 满减/固定组合价（Mix&Match）：指定一组商品，在同一门店，当购物车中该组商品累计件数达到门槛N时，N件按固定总价M结算。
- 生效时间窗口：支持起止时间；仅在窗口内生效。
- 排他：同一门店、同一时间窗口内若标记排他的促销冲突，仅能应用一个（按优先级选择）。
- 门店范围：可指定多个分店（PromotionStore）。
- 简约模型：按您建议，仅建3张表：Promotion、PromotionProduct、PromotionStore。

## 数据库/实体设计（SqlSugar）
- Promotion（促销主表）
  - `Id:string` 主键
  - `Name:string(100)` 名称
  - `Description:string(500)?` 说明（可空）
  - `EffectiveStart:DateTime` 生效开始
  - `EffectiveEnd:DateTime` 生效结束
  - `IsEnabled:bool` 是否启用
  - `IsExclusive:bool` 是否排他
  - `Priority:int` 排他冲突时选择优先级（越大越先）
  - `ApplyQuantity:int` 组合门槛N（必须≥2）
  - `FixedPrice:decimal(18,4)` 组合固定总价M
  - `MaxApplicationsPerOrder:int?` 每单最多应用次数（可空=不限制）
  - 导航：`Products: List<PromotionProduct>`、`Stores: List<PromotionStore>`（Sugar `[Navigate]` 标注）
- PromotionProduct（促销商品表）
  - `Id:string` 主键
  - `PromotionId:string` 外键
  - `ProductCode:string(50)` 商品编码（与现有 `Product.ProductCode` 对齐）
  - `UnitWeight:int` 该商品在组合中计数权重（默认1，满足“达到指定数据量”可扩展）
- PromotionStore（促销门店表）
  - `Id:string` 主键
  - `PromotionId:string` 外键
  - `StoreCode:string(50)` 门店编码
- 索引/约束
  - Promotion：`(IsEnabled, EffectiveStart, EffectiveEnd)` 组合索引，优化检索。
  - PromotionProduct：`(PromotionId, ProductCode)` 唯一，避免重复商品。
  - PromotionStore：`(PromotionId, StoreCode)` 唯一，避免重复门店。
  - 排他约束通过服务层校验：在同一`StoreCode`与时间窗口重叠且`IsExclusive=true`时，禁止启用第二个，或按`Priority`只允许最高优先级启用。

## 应用规则/冲突处理
- 命中判定
  - 门店在 `PromotionStore`，促销启用且当前时间在`[EffectiveStart, EffectiveEnd]`。
  - 购物车中该促销的商品累计“加权件数”达到 `ApplyQuantity`。
- 选择策略（排他）
  - 在同一门店所有命中的促销中，如果存在任意`IsExclusive=true`，仅选择`Priority`最高的排他促销；否则可并行应用多个非排他促销（如后续需要）。
- 计价与分摊
  - 每满足一次门槛（bundle），对 `ApplyQuantity` 件商品执行固定总价 `FixedPrice`；可重复应用 `floor(totalCount / ApplyQuantity)` 次，受 `MaxApplicationsPerOrder` 限制。
  - 分摊策略：按商品原价占比进行等比例降价，使该N件合计为M（避免出现负价/异常小数）。
  - 余量处理：不足N件的剩余按原价结算。

## 服务/接口设计
- 新增实体文件（保持现有风格，`string`主键，`[SugarTable]`）：
  - `BlazorApp.Shared\Models\HBweb\Promotion.cs`
  - `BlazorApp.Shared\Models\HBweb\PromotionProduct.cs`
  - `BlazorApp.Shared\Models\HBweb\PromotionStore.cs`
- React服务与控制器（对齐现有命名与React目录）
  - `BlazorApp.Api\Services\React\PromotionReactService.cs`
    - CRUD：创建/更新/启用/停用；同步 Products/Stores 子表；启用时进行排他窗口校验。
    - `EvaluateAsync(storeCode, cartItems)`：返回可应用促销与分摊后的行级价格调整。
  - `BlazorApp.Api\Controllers\React\ReactPromotionsController.cs`
    - `GET /api/react/v1/promotions` 列表/分页
    - `POST /api/react/v1/promotions` 创建
    - `PUT /api/react/v1/promotions/{id}` 更新
    - `POST /api/react/v1/promotions/{id}/enable` 启用（含排他校验）
    - `POST /api/react/v1/promotions/evaluate` 评估购物车（入参：`storeCode`、`items:[{productCode, qty, unitPrice}]`）
- 评估逻辑伪代码
  - 取当前门店可用促销：`IsEnabled=true && now∈[Start,End] && store匹配`
  - 过滤购物车中商品命中集合，计算加权件数。
  - 计算可应用次数 `bundles = min(floor(count/ApplyQuantity), MaxApplicationsPerOrder??∞)`
  - 若存在排他促销：仅保留`Priority`最高的一个；否则都保留。
  - 对每个bundle选取N件（默认按加入顺序或按价高优先，配置项可扩展），执行固定价分摊，生成调整明细。

## 与现有定价的关系
- `StoreRetailPrice`/自动定价（`AutoPricingService`）继续提供“基础单价”。促销仅在下单/购物车阶段做“订单级总价调整”。
- 不修改基础单价表结构，避免与`IsAutoPricing`、心理价处理冲突。
- 优先级：当促销生效时，订单中相关商品按促销价分摊后结算；其他商品仍按基础单价。

## 验证与迁移
- 数据迁移：新增三表建表脚本或由 SqlSugar CodeFirst 迁移（保持`string`主键，服务层用`UuidHelper.GenerateUuid7()`生成）。
- 单元测试：
  - 门店/时间命中与不命中
  - 排他冲突与`Priority`选择
  - 组合价分摊正确性（边界：高价/低价、部分件数、MaxApplications 限制）
- 性能：
  - 索引覆盖促销查找；评估在服务层一次性拉取当期门店促销后内存计算，避免多次IO。

## 前端类型与交互（对齐既有React模式）
- `types/promotion.ts`：与DTO对齐（Promotion、PromotionProduct、PromotionStore）
- 列表/编辑页：与 PricingStrategy 管理一致；支持门店选择、商品选择、时间窗口、排他与优先级。
- 结算页调用 `evaluate` 接口获取促销分摊后的行项目价格。

## 下一步
- 按上述方案创建三张表实体与React服务/控制器，并接入评估接口；随后提供建表/示例数据与测试用例。