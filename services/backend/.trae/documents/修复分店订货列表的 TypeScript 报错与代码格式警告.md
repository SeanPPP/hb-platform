我将修复分店订货列表的 TypeScript 报错，并说明后端控制器的格式警告处理建议。

### 1. 修复前端 TS 报错（OrderList/index.tsx）
- 报错：Line 14 `useRef<ActionType>()` 应有 1 个参数，实际获得 0 个。
- 原因：`useRef<T>` 需要传入初始值。
- 修复：改为 `const actionRef = useRef<ActionType>(null);`

### 2. 后端 C# 控制器格式警告（ReactStoreOrderController.cs）
- 警告为缩进/换行的代码风格提示，不影响功能。
- 建议：统一格式化该文件，插入建议的换行与缩进，以消除警告（可在后续提交中一并处理）。

### 3. 验证
- 前端：确认 ProTable 的 `actionRef` 正常工作，调用 `reloadAndRest` 不再触发类型错误。
- 后端：仅为风格警告，不阻塞编译。