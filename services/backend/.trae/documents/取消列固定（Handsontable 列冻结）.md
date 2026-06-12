**问题与目标**

* 当前页面使用 Handsontable 的 `fixedColumnsLeft={2}` 冻结了左侧两列：ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx:412。

* 目标：取消列固定，不再冻结任何列。

**实现方案**

* 编辑 `index.tsx`，移除 `fixedColumnsLeft={2}` 配置（或设置为 `0`），不影响其他表格功能。

* 保留现有的筛选、排序、编辑与保存逻辑，避免引入副作用。

**验证**

* 页面刷新后，左侧不再固定，表格可水平滚动且所有列一起移动。

* 控制台无新增错误，原有请求与数据加载正常。

**可选增强**

* 后续如需灵活控制，可添加一个切换开关，将冻结列数保存在状态中（默认 0），按需开启或关闭。

