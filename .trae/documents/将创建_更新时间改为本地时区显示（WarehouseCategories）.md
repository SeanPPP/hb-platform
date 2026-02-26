## 原因分析
- 该页使用 `antd Table`，`createdAt` 与 `updatedAt` 两列当前未做 `render` 格式化，直接显示后端原始时间字符串。
- 项目其它列表页已统一用 `dayjs` 做显示格式化（如用户/角色/门店等），默认按浏览器本地时区输出。

## 实施方案
- 在 `src/pages/WarehouseCategories/index.tsx` 引入 `dayjs`。
- 为“创建日期”和“更新日期”两列增加 `render`，使用 `dayjs(value).format('YYYY-MM-DD HH:mm:ss')` 显示为本地时区。
- 为空值时返回 `null`，避免显示 `Invalid Date`。
- 保持现有 `sorter: true` 与服务端排序逻辑不变，仅影响显示层，不改动请求与字段。

## 代码改动要点
- 增加导入：`import dayjs from 'dayjs'`
- 修改两列：
  - `createdAt`：`render: (v) => v ? dayjs(v).format('YYYY-MM-DD HH:mm:ss) : null`
  - `updatedAt`：`render: (v) => v ? dayjs(v).format('YYYY-MM-DD HH:mm:ss) : null`

## 验证方式
- 启动前端后，进入仓库分类页，检查两列：应显示为本地时区的“YYYY-MM-DD HH:mm:ss”。
- 切换浏览器系统时区（如从 UTC 到 GMT+8）验证显示随本地时区变化。
- 交叉对比其它已格式化页面（如用户管理、角色管理），确保一致性。

## 注意事项
- 无需引入 `utc/timezone` 插件：项目当前未使用，`dayjs` 对带 `Z` 的 ISO 字符串会自动按本地时区渲染。
- 不修改后端字段与 API 请求，仅前端显示格式化。