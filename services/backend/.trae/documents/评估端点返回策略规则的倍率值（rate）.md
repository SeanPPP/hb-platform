## 目标
- 将评估接口返回的 `rate` 改为“策略规则计算得到的倍率”，即来自算法（线性/指数/阶梯）计算的值，而非 `retailPrice / purchasePrice` 的比值。

## 实施
- 在 `AutoPricingService` 新增 `CalculateRate(purchasePrice, strategy)`，复用现有算法逻辑并直接返回倍率；`CalculateRetailPrice` 调用该方法并保持零售价兜底与两位小数。
- 修改评估端点 `POST /api/react/v1/pricing-strategies/evaluate`：
  - 使用 `CalculateRate(...)` 作为返回的 `rate`
  - `retailPrice` 仍调用 `CalculateRetailPrice(...)`，确保最低不低于进货价并四舍五入
  - 规则命中信息保持不变

## 验证
- 构建后端确保通过
- 前端“策略测试”区域显示的上浮率为策略规则倍率；零售价与倍率匹配且不低于进货价。

## 说明
- 该变更不影响现有策略 CRUD 与列表逻辑；仅评估端点的 `rate` 含义更准确、可直接用于业务展示与调试。