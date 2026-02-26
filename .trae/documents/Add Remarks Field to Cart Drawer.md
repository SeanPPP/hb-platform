**用户需求**:
在购物车中添加备注区域。

**分析**:
1.  **位置**: `CartDrawer.tsx`。
2.  **当前实现**: 目前只有一个固定的 `remarks: 'Submitted from Web'` 在 `handleSubmit` 中。
3.  **目标**: 允许用户输入备注，并在提交时发送。

**解决方案**:
1.  **UI**:
    *   在 Drawer 的 `footer` 或 `body` 底部（List 下方）添加一个 `TextArea` 用于输入备注。
    *   考虑到 Footer 已经很拥挤，且备注是订单级别的，放在 Footer 的上方（即作为 Footer 的一部分，或者 Body 的最后一部分）比较合适。
    *   通常放在 Footer 的最上面（Price Summary 上方）或者 Drawer 内容的底部。放在 Footer 区域比较显眼，方便用户在提交前填写。

2.  **State**:
    *   添加 `remarks` 状态 (`const [remarks, setRemarks] = useState('')`)。

3.  **Submit**:
    *   修改 `handleSubmit`，将 `remarks` 状态传递给 `submitOrder`。

**详细步骤**:
1.  引入 `Input` 组件 (`const { TextArea } = Input;`)。
2.  添加 `useState` 来管理备注内容。
3.  在 Footer 区域添加 `TextArea`。
4.  更新 `submitOrder` 调用。

**Refined Plan**:
Add `remarks` state and `TextArea` component to `CartDrawer`.

```typescript
const [remarks, setRemarks] = useState('');

// ...

const handleSubmit = async () => {
    // ...
    await submitOrder({ storeCode: cart.storeCode, remarks });
    // ...
};

// ...
footer={
  <div style={{ textAlign: 'right' }}>
     <div style={{ marginBottom: 12 }}>
        <Input.TextArea 
           placeholder="Order Remarks (Optional)" 
           value={remarks}
           onChange={e => setRemarks(e.target.value)}
           rows={2}
           style={{ marginBottom: 12 }}
        />
        <div style={{ display: 'flex', ... }}>...</div>
     </div>
     ...
  </div>
}
```

Wait, `Input` needs to be imported from 'antd'.

