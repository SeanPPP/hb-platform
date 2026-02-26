# Tab切换焦点泄露问题 - 完整分析与解决方案

## 📋 问题总结

**症状**: 打开货柜明细页(ContainerDetail.razor)后，使用tab切换到其他页面时应用崩溃，即使关闭货柜明细页，问题依然持续存在。

**错误信息**:
```
TypeError: Cannot read properties of null (reading 'removeChild')
```

**浏览器警告**:
```
Blocked aria-hidden on an element because its descendant retained focus.
Element with focus: <button.ant-btn ant-btn-link ant-btn-sm>
Ancestor with aria-hidden: <div.tab-content ant-tabs-tabpane ant-tabs-tabpane-hidden>
```

## 🔍 问题根源分析

### 根本原因
1. **焦点泄露(Focus Leak)**: ContainerDetail页面中的某些交互元素保持了焦点
2. **Aria-hidden冲突**: Tab切换时，被隐藏的tab面板设置`aria-hidden="true"`，但内部元素仍持有焦点
3. **DOM清理失败**: Blazor在清理隐藏面板的DOM时，父节点引用为null，导致`removeChild`操作失败

### 触发点
ContainerDetail.razor第945-956行的自动聚焦代码：
```javascript
await JSRuntime.InvokeVoidAsync("eval", @"
    (function() {
        const inputs = document.querySelectorAll('.name-edit-input .ant-input');
        if (inputs.length > 0) {
            const lastInput = inputs[inputs.length - 1];
            lastInput.focus();
            lastInput.select();
        }
    })();
");
```

## ⚠️ 已尝试的修复方案

### 方案1: 组件Dispose时清除焦点 (❌ 失败)
**位置**: `ContainerDetail.razor` Dispose方法

**实现**:
```csharp
public void Dispose()
{
    try
    {
        Task.Run(async () =>
        {
            await JSRuntime.InvokeVoidAsync("eval", @"
                (function() {
                    if (document.activeElement && document.activeElement !== document.body) {
                        document.activeElement.blur();
                    }
                })();
            ");
        });
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "清除焦点失败");
    }
}
```

**失败原因**: Dispose触发太晚，当Blazor尝试移除DOM节点时，焦点还未清除

---

### 方案2: 全局Tab点击监听器 (❌ 部分失败)
**位置**: `ModernTabLayout.razor` OnAfterRenderAsync方法

**实现**:
```javascript
document.addEventListener('click', function(e) {
    const isTabClick = e.target.closest('.ant-tabs-tab') || 
                      e.target.closest('.ant-tabs-tab-close') ||
                      e.target.closest('.ant-tabs-tab-btn');
    
    if (isTabClick) {
        if (document.activeElement && document.activeElement !== document.body) {
            document.activeElement.blur();
            document.body.focus();
        }
    }
}, true);
```

**失败原因**: 即使使用捕获阶段(true)，焦点清除可能在AntBlazor的Tab组件设置`aria-hidden`之后才执行

---

### 方案3: Mutation Observer监听aria-hidden (❌ 部分失败)
**位置**: `ModernTabLayout.razor` OnAfterRenderAsync方法

**实现**:
```javascript
const observer = new MutationObserver(function(mutations) {
    mutations.forEach(function(mutation) {
        if (mutation.attributeName === 'aria-hidden' && mutation.target) {
            const target = mutation.target;
            if (target.getAttribute('aria-hidden') === 'true') {
                // 清除焦点
                const focusedInside = target.querySelector(':focus');
                if (focusedInside) {
                    focusedInside.blur();
                    document.body.focus();
                }
                if (document.activeElement && target.contains(document.activeElement)) {
                    document.activeElement.blur();
                    document.body.focus();
                }
            }
        }
    });
});
```

**测试结果**: 
- ✅ 成功检测到焦点并清除了部分元素
- ❌ 仍然有其他元素在保持焦点，导致错误依然发生

**控制台日志证据**:
```
[Fix] Hidden panel focus cleared: JSHandle@node
TypeError: Cannot read properties of null (reading 'removeChild')
```

## 🛠️ 推荐的解决方案

### 最终方案: 多层防御 + Blazor级修复

#### 1. 禁用ContainerDetail的自动聚焦功能
**修改**: `ContainerDetail.razor` 第945-962行

注释掉EnterEditMode中的自动聚焦代码，或添加条件判断:
```csharp
private async Task EnterEditMode(YiwuContainerDetailDto detail, string fieldName)
{
    var cellKey = $"{detail.DetailCode}_{fieldName}";
    editingCells.Add(cellKey);
    currentEditingCell = cellKey;
    StateHasChanged();
    
    // ❌ 暂时禁用自动聚焦，避免焦点泄露问题
    // await Task.Delay(100);
    // try
    // {
    //     await JSRuntime.InvokeVoidAsync("eval", @"...聚焦代码...");
    // }
    // catch (Exception ex)
    // {
    //     Logger.LogWarning(ex, "聚焦输入框失败");
    // }
}
```

#### 2. 在Tab组件层面阻止隐藏含焦点的面板
**修改**: `ModernTabLayout.razor`

在Tab切换前强制清除所有焦点:
```javascript
// 在tab切换事件的最早阶段执行
document.addEventListener('mousedown', function(e) {
    const isTabClick = e.target.closest('.ant-tabs-tab');
    if (isTabClick) {
        // 强制清除所有焦点
        document.querySelectorAll('.ant-tabs-tabpane').forEach(function(panel) {
            if (panel.contains(document.activeElement)) {
                document.activeElement.blur();
                document.body.focus();
            }
        });
    }
}, true); // 捕获阶段，比click更早
```

#### 3. 使用CSS禁用隐藏面板的交互
**添加**: 全局CSS规则

```css
/* 防止隐藏的tab面板中的元素获取焦点 */
.ant-tabs-tabpane[aria-hidden="true"] * {
    pointer-events: none !important;
}

.ant-tabs-tabpane[aria-hidden="true"] input,
.ant-tabs-tabpane[aria-hidden="true"] button,
.ant-tabs-tabpane[aria-hidden="true"] select,
.ant-tabs-tabpane[aria-hidden="true"] textarea {
    tab-index: -1 !important;
}
```

#### 4. 实现Tab切换前的清理钩子
**新增**: `ModernTabLayout.razor`

```csharp
private async Task OnTabChange(string activeKey)
{
    // 在tab切换前清理焦点
    try
    {
        await JSRuntime.InvokeVoidAsync("eval", @"
            (function() {
                if (document.activeElement && document.activeElement !== document.body) {
                    console.log('[Cleanup] Clearing focus before tab switch');
                    document.activeElement.blur();
                    document.body.focus();
                }
            })();
        ");
    }
    catch (Exception ex)
    {
        Logger.LogWarning(ex, "Tab切换前清理焦点失败");
    }
}
```

## 📊 测试验证

### 测试步骤
1. 导航到货柜管理页面
2. 打开任意货柜的明细页
3. 点击"货柜管理"tab切换回列表页
4. 验证是否出现"An unhandled error has occurred"

### 预期结果
- ❌ 当前状态: 错误仍然发生
- ✅ 修复后: Tab切换正常，无错误信息

## 🔧 临时解决方案

如果上述修复仍不能完全解决问题，可以考虑以下临时方案：

### 方案A: 使用独立路由而非Tab
将货柜明细页改为独立路由页面，而不是在Tab中打开

### 方案B: 延迟隐藏tab面板
修改AntBlazor的Tab组件，在隐藏面板前增加100ms延迟，确保焦点清除完成

### 方案C: 使用Dialog替代Tab面板
将货柜明细页改为Modal/Dialog形式展示

## 📝 相关文件

- `BlazorApp/Pages/Container/ContainerDetail.razor` - 货柜明细页(焦点泄露源头)
- `BlazorApp/Layout/ModernTabLayout.razor` - Tab布局组件(焦点清理实现)
- `docs/Tab-Focus-Leak-Fix.md` - 初步修复文档

## 🐛 已知问题

1. MutationObserver虽然能检测到部分焦点并清除，但无法捕获所有保持焦点的元素
2. Blazor的虚拟DOM更新机制可能在焦点清除前就开始移除节点
3. AntBlazor的Table组件内部可能有多个可聚焦元素，难以全部追踪

## 💡 后续建议

1. **联系AntBlazor团队**: 这可能是AntBlazor Tab组件的已知问题
2. **升级AntBlazor版本**: 检查是否有新版本修复了此问题
3. **重构ContainerDetail**: 减少页面复杂度，避免过多交互元素
4. **使用React/Vue的解决方案**: 参考其他框架如何处理类似问题

---

**诊断时间**: 2025-10-03  
**诊断工具**: Chrome DevTools MCP  
**Blazor版本**: .NET 9.0  
**AntDesign.Blazor版本**: (待确认)

