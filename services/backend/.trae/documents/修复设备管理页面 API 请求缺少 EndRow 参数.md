## 修复计划

### 问题
设备管理页面（DeviceManagement）在调用 `/api/react/v1/device-registration/grid` API 时，请求参数缺少 `EndRow` 字段，导致后端返回 400 错误："The request field is required."

### 解决方案
修改 `ReactUmi/my-app/src/pages/SystemSettings/DeviceManagement/index.tsx` 文件：

在 `requestParams` 对象中添加 `EndRow` 字段计算逻辑：
```javascript
EndRow: ((params.current || 1) - 1) * (params.pageSize || 20) + (params.pageSize || 20)
```

### 具体修改
- 文件：`index.tsx` 第 230-238 行
- 在 `requestParams` 对象中添加 `EndRow` 属性
- 保持其他参数不变