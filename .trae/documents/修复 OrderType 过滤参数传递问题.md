## 修复计划

### 问题
OrderType 过滤器的值没有正确传递到后端 API，导致筛选功能不生效。

### 原因
1. DatePicker.RangePicker 没有绑定 `onChange` 事件和 state，日期值没有被正确管理
2. 虽然代码中传递了 `orderType: filterOrderType`，但需要确保在所有情况下都正确传递

### 修复步骤

1. **在 index.tsx 中添加日期范围 state**
   - 添加 `filterDateRange` state
   - 为 DatePicker.RangePicker 添加 `onChange` 事件处理
   - 修改 `handleReset` 清除日期范围

2. **确保 request 函数使用正确的参数**
   - 从 `filterDateRange` state 获取日期范围（而不是从 params）
   - 保持其他过滤参数从 state 获取

### 修改文件
- `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\PosmSalesOrders\index.tsx`