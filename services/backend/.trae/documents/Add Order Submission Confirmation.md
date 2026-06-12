**用户需求**:
在提交订单前，需要进行二次确认。

**分析**:
1.  **位置**: `CartDrawer.tsx` 中的 "Submit Order" 按钮。
2.  **当前实现**: 直接绑定了 `handleSubmit` 函数。
3.  **解决方案**: 使用 Ant Design 的 `Popconfirm` 组件包裹 "Submit Order" 按钮，或者在 `handleSubmit` 中添加 `Modal.confirm`。

考虑到 `Popconfirm` 在 Drawer 底部可能会有样式遮挡或体验问题，且提交订单是一个重要操作，使用 `Modal.confirm` 可能更正式，但 `Popconfirm` 更轻量。Ant Design Drawer Footer 中的 Popconfirm 通常工作良好。

**方案选择**:
使用 `Popconfirm` 包裹 "Submit Order" 按钮。

**代码修改**:
在 `CartDrawer.tsx` 中：
1. 引入 `Popconfirm` (已引入)。
2. 将 "Submit Order" 按钮包裹在 `Popconfirm` 中。
3. 设置确认提示文案，例如 "Are you sure you want to submit this order?"。

**Refined Plan**:
Modify `CartDrawer.tsx` to wrap the submit button with `Popconfirm`.

```typescript
           <Popconfirm
              title="Submit Order"
              description="Are you sure you want to submit this order?"
              onConfirm={handleSubmit}
              okText="Yes"
              cancelText="No"
              disabled={!cart || cart.items.length === 0}
           >
             <Button 
                type="primary" 
                size="large" 
                block 
                disabled={!cart || cart.items.length === 0}
             >
               Submit Order
             </Button>
           </Popconfirm>
```

Wait, if I wrap the disabled button, Popconfirm might not work or behave weirdly.
Better to put logic:
`disabled={!cart || cart.items.length === 0}` on Popconfirm? No, popconfirm doesn't have disabled usually in that way, or it just passes through.
Actually, if button is disabled, click won't trigger popconfirm.
So structure:

```typescript
<Popconfirm ... onConfirm={handleSubmit}>
  <Button ... disabled={...}>Submit</Button>
</Popconfirm>
```

When button is disabled, pointer-events usually none, so popconfirm won't show. That works.

Let's implement this.

