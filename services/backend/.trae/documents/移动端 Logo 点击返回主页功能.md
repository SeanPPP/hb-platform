我将在移动端 header 的 logo 上添加点击返回主页的功能。

### 计划
1.  **添加点击事件**: 给移动端 logo 的外层 div (`.mobileLogo`) 添加 `onClick` 事件处理器。
2.  **添加光标样式**: 添加 `style={{ cursor: 'pointer' }}` 使鼠标悬停时显示手型光标。
3.  **跳转逻辑**: 点击时使用 `history.push('/store')` 跳转到商店主页。

### 实施步骤
1.  **修改 `src/layouts/StoreLayout.tsx`**:
    *   在 `.mobileLogo` div 上添加 `onClick={() => history.push('/store')}`。
    *   添加 `style={{ cursor: 'pointer' }}`。