我将在后台首页的欢迎语中，将 "欢迎使用 Umi Max ！" 改为 "欢迎使用 Hot Bargain ！"。

### 计划
1.  **定位文件**: 在 `src/components/Guide/Guide.tsx` 中找到了欢迎语组件。
2.  **修改文本**: 将 "欢迎使用 <strong>{name}</strong> ！" 改为 "欢迎使用 <strong>Hot Bargain</strong> ！"。

### 实施步骤
1.  **修改 `src/components/Guide/Guide.tsx`**:
    *   将欢迎语中的 `{name}` 替换为硬编码的 "Hot Bargain"。