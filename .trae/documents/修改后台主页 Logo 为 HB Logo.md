我将在后台主页左上角实现 HB logo。

### 计划
1.  **定位配置**: 在 `src/app.ts` 中找到了 `layout` 配置。
2.  **修改 Logo 路径**: 当前配置为 `logo: 'https://img.alicdn.com/tfs/TB1YHEpwUT1gK0jSZFhXXaAtVXa-28-27.svg'` (Ant Design 默认 Logo)。我将把它修改为 `/favicon.svg` (HB logo)。

### 实施步骤
1.  **修改 `src/app.ts`**:
    *   在 `layout` 导出函数中，将 `logo` 属性的值改为 `/favicon.svg`。

这样，后台管理界面的左上角将显示与浏览器标签页一致的 HB logo。