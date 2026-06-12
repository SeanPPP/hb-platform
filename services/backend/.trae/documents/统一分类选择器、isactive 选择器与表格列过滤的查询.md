分析原因
- 目前分类 TreeSelect 改变时仅携带 categoryGuids；isactive 改变时仅携带 isactive；表格列过滤仅在 Table.onChange 中携带 mappedFilters。导致不同入口发起的查询条件不一致，未“统一在一起查”。

调整方案（仅前端 index.tsx）：
1) 新增集中管理的 tableFilters 状态：Record<string, string[]>，用于保存表格列过滤条件。
2) 修改 loadTable：始终以 tableFilters 为基础，统一合并 isActiveFilter（非 all 时追加 { isactive: [isActiveFilter] }），再叠加调用方传入的 extra.filters。请求体 filters 字段始终是该合并结果。
3) 修改 Table.onChange：提取 filters 存入 tableFilters，同时保持分页与排序参数；然后调用 loadTable，使其带上 tableFilters + isActiveFilter + 分类。
4) 修改分类 TreeSelect.onChange：更新分类后，调用 loadTable，并确保带上当前 tableFilters 与 isActiveFilter（无需手动拼，loadTable 内部统一合并）。
5) 修改 isactive Select.onChange：更新 isActiveFilter 后，调用 loadTable，并确保带上当前 tableFilters 与分类（同上）。
6) 可选增强：为各列设置 filteredValue=tableFilters[k]，让筛选 UI 在分类/isactive切换时保持显示一致；如不需要持久化 UI，可省略。

验证点
- isactive=all 时不过滤；=1 仅返回上架；=0 仅返回下架。
- 分类选择（含子类）与表格列过滤、isactive 同时生效，分页/排序正常。
- 字符串包含匹配行为维持现状（后端已实现）。

请确认是否按此方案实施。确认后我将按步骤更新 index.tsx 并联调验证。