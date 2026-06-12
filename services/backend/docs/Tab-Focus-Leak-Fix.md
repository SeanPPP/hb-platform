# Tab 切换焦点泄露问题修复

## 问题描述

当打开货柜明细页（ContainerDetail.razor）后，再使用tab切换其他页面时，会出现以下错误：

```
TypeError: Cannot read properties of null (reading 'removeChild')
```

即使关闭货柜明细页后，问题仍然存在，导致tab切换功能完全失效。

## 问题根源

### 1. 焦点泄露（Focus Leak）

通过Chrome DevTools诊断发现，货柜明细页面中有元素保持了焦点，而当tab切换时，该元素的父容器被设置为 `aria-hidden`，导致以下警告：

```
Blocked aria-hidden on an element because its descendant retained focus.
Element with focus: <button.ant-btn ant-btn-link ant-btn-sm>
Ancestor with aria-hidden: <div.tab-content ant-tabs-tabpane ant-tabs-tabpane-hidden>
```

### 2. DOM清理失败

当Blazor尝试清理被隐藏的tab面板的DOM时，由于焦点仍然保持在被隐藏的元素上，导致：
- 父节点引用变为 `null`
- `removeChild` 操作失败
- 抛出 `TypeError: Cannot read properties of null (reading 'removeChild')`

### 3. 问题触发点

在 `ContainerDetail.razor` 中，有一个自动聚焦功能，用于在添加新行时聚焦到输入框：

```javascript
const inputs = document.querySelectorAll('.name-edit-input .ant-input');
if (inputs.length > 0) {
    const lastInput = inputs[inputs.length - 1];
    lastInput.focus();  // 这个焦点没有在组件销毁时清理
}
```

## 修复方案

### 方案 1: 全局Tab切换监听器（推荐）

在 `ModernTabLayout.razor` 中添加全局的tab切换监听器，在任何tab切换时自动清除焦点：

**位置**: `BlazorApp/Layout/ModernTabLayout.razor`  
**修改方法**: `OnAfterRenderAsync`

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await IconService.CreateFromIconfontCN("//at.alicdn.com/t/font_2735473_hi62ezq5579.js");
        
        // 添加全局tab切换监听器，解决焦点泄露问题
        await JSRuntime.InvokeVoidAsync("eval", @"
            (function() {
                // 监听tab点击事件，清除当前焦点
                document.addEventListener('click', function(e) {
                    // 检查是否点击了tab按钮或关闭按钮
                    const isTabClick = e.target.closest('.ant-tabs-tab') || 
                                      e.target.closest('.ant-tabs-tab-close') ||
                                      e.target.closest('.ant-tabs-tab-btn');
                    
                    if (isTabClick) {
                        // 延迟清除焦点，确保在tab切换前执行
                        setTimeout(function() {
                            if (document.activeElement && document.activeElement !== document.body) {
                                console.log('Tab switch: clearing focus from', document.activeElement);
                                document.activeElement.blur();
                            }
                        }, 10);
                    }
                }, true);
                
                console.log('Tab focus cleanup listener installed');
            })();
        ");
        
        StateHasChanged();
    }
}
```

**优点**:
- 全局生效，解决所有页面的焦点泄露问题
- 在tab切换前主动清除焦点
- 不依赖单个组件的Dispose逻辑

### 方案 2: 组件级别焦点清理（辅助）

在 `ContainerDetail.razor` 的 `Dispose` 方法中添加焦点清理：

**位置**: `BlazorApp/Pages/Container/ContainerDetail.razor`  
**修改方法**: `Dispose`

```csharp
public void Dispose()
{
    isDisposed = true;
    
    // 清除焦点，防止tab切换时出现焦点泄露问题
    try
    {
        Task.Run(async () =>
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("eval", @"
                    (function() {
                        // 移除当前页面中所有可能保持焦点的元素
                        if (document.activeElement && document.activeElement !== document.body) {
                            console.log('Clearing focus from:', document.activeElement);
                            document.activeElement.blur();
                        }
                    })();
                ");
            }
            catch
            {
                // 忽略错误，组件可能已经销毁
            }
        });
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "清除焦点失败");
    }
    
    Logger.LogInformation("ContainerDetail组件已销毁，ContainerCode: {ContainerCode}", ContainerCode);
}
```

**优点**:
- 作为备用方案，在组件销毁时清理焦点
- 即使全局监听器失效，仍有保护

## 测试步骤

1. **重新编译应用**:
   ```bash
   cd BlazorApp
   dotnet build
   ```

2. **启动应用并测试**:
   - 打开浏览器开发者工具（F12）
   - 导航到货柜管理页面
   - 点击任意货柜的"查看"按钮，进入货柜明细页
   - 检查控制台，应该看到 `Tab focus cleanup listener installed`
   - 尝试切换到其他tab（如点击"货柜管理"tab）
   - 检查控制台，应该看到 `Tab switch: clearing focus from ...`
   - 确认没有 `TypeError` 错误
   - 确认tab切换正常工作

3. **验证修复**:
   - ✅ 不再出现 `Cannot read properties of null (reading 'removeChild')` 错误
   - ✅ 不再出现 `Blocked aria-hidden` 警告
   - ✅ Tab切换功能恢复正常
   - ✅ 关闭货柜明细页后，其他tab仍然可以正常切换

## 最佳实践

为避免类似问题，建议遵循以下最佳实践：

1. **焦点管理**: 
   - 在组件销毁时（Dispose）主动清除焦点
   - 避免在隐藏的元素上保持焦点

2. **无障碍性（A11y）**:
   - 不在设置了 `aria-hidden` 的元素内保持焦点
   - 使用 `inert` 属性替代 `aria-hidden`（如果可能）

3. **Tab组件**:
   - 在tab切换时主动清除焦点
   - 使用全局监听器统一处理

4. **调试技巧**:
   - 使用Chrome DevTools检查控制台错误
   - 检查元素的aria属性和焦点状态
   - 监控DOM变更和焦点变化

## 相关资源

- [WAI-ARIA aria-hidden规范](https://w3c.github.io/aria/#aria-hidden)
- [HTML inert属性](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/inert)
- [Focus管理最佳实践](https://developer.mozilla.org/en-US/docs/Web/Accessibility/Keyboard-navigable_JavaScript_widgets)

## 修复日期

2025-10-03

## 修复人员

AI Assistant (Claude Sonnet 4.5)

