我发现您实际使用的是 Umi 的全局布局配置（`src/app.tsx`），而不是 `SystemLayout.tsx`。因此之前的修改没有生效。

**分析结果**：
1. 您的项目使用了 Umi ProLayout，布局逻辑在 `src/app.tsx` 的 `rightContentRender` 中定义。
2. 这里的代码特征（蓝色头像背景、黑色文字）与您的截图完全一致。
3. 之前修改的 `SystemLayout.tsx` 可能未被当前页面使用。

**修改计划**：
1.  **修改 `src/app.tsx`**：在 `rightContentRender` 函数中，给用户名的 `span` 标签添加一个类名 `className: 'header-user-name'`。
2.  **修改 `src/global.css`**：添加 CSS 媒体查询，在屏幕宽度小于 768px（移动端/窄屏）时，隐藏 `.header-user-name`。

这样可以确保在小屏幕或移动端模式下，只显示头像，隐藏用户名。