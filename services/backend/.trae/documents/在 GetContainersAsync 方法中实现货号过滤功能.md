在 `ContainerReactService.GetContainersAsync` 方法中添加货号过滤逻辑：

1. 在日期过滤和状态过滤之后、排序之前添加货号过滤逻辑
2. 当 `ItemNumberFilter` 不为空时：
   - 查询 `ContainerDetail` 表，通过 `LeftJoin` 关联 `DomesticProduct` 表
   - 筛选出 `DomesticProduct.HBProductNo` 包含过滤值的明细记录
   - 获取这些明细对应的 `ContainerCode` 列表
   - 使用 `Where` 条件过滤货柜查询，只返回包含匹配商品的货柜
   - 如果没有找到匹配的货柜，返回空结果

参考 `YiwuContainerService`（L80-103）的现有实现模式。