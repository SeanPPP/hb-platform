# 修复定价策略页面的控制台警告

**原因分析**
1.  **Duplicate Key / Key Spreading Warning**: 在 `Form.List` 的渲染循环中，代码使用了 `<Form.Item {...field} ... />`。由于 `field` 对象包含 `key` 属性，展开操作 (`...field`) 将同一个 `key` 传递给了每一行中的所有 `Form.Item` 兄弟组件，导致 React 报 "duplicate keys" 错误。
2.  **Modal `destroyOnClose` Warning**: Ant Design 提示 `destroyOnClose` 已废弃（在某些新版本中），建议使用 `destroyOnHidden`。
3.  **Form Instance Disconnected Warning**: 由于 Modal 设置了 `destroyOnClose`，当 Modal 关闭时 Form 组件被卸载。此时如果在打开 Modal 前调用 `form.setFieldsValue`，会警告 Form 实例未连接。

**修改方案**

1.  **修复 Key 冲突**
    *   在 `PricingStrategies/index.tsx` 的 `Form.List` 循环中，不要使用 `{...field}`。
    *   改为手动传递 `name={[field.name, 'propertyName']}`，或者解构 `field` 排除 `key` 后再传递剩余属性。
    *   明确 `key` 只应该在最外层的容器（这里是 `<Space>`）上设置，内部的 `Form.Item` 不需要也不能设置相同的 `key`。

2.  **修复 Modal 与 Form 生命周期问题**
    *   将 Modal 的 `destroyOnClose` 属性替换为 `destroyOnClose={false}` 或者直接移除（默认即为 false），并添加 `forceRender={true}`。
    *   这样 Form 实例会一直存在，避免 "Instance created by useForm is not connected" 警告。
    *   由于代码中已经在打开时手动调用了 `resetFields()`，这也能保证表单状态的正确重置。

**执行步骤**
1.  修改 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\PosAdmin\PricingStrategies\index.tsx`。
    *   移除 `Form.Item` 上的 `{...field}` 展开。
    *   移除 Modal 的 `destroyOnClose`，添加 `forceRender`。

无需修改其他文件。此修复仅涉及前端页面逻辑优化。