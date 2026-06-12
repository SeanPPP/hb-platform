## 目标
- 取消后端订单列表中的 totalquantity 排序能力，使传入 SortBy=totalquantity 时不再按总数量排序，回退到默认排序（日期+单号）。

## 修改点
- 在 StoreOrderReactService.GetOrderListAsync 的排序 switch 中删除 case "totalquantity" 分支，不再构造针对总数量的子查询排序。
- 保留返回 DTO 中的 TotalQuantity 字段，不影响前端展示，仅移除排序能力。

## 验证
- 编译通过。
- 传入 { sortBy: "totalquantity" } 时，结果应按默认的 OrderDate DESC, OrderNo DESC 排序。

## 范围
- 仅后端服务方法，接口与返回结构不变。