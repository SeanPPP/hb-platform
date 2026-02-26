我将修改浏览器标签页（favicon）的图标，从 umi 默认图标改为 HB logo。

### 计划
1.  **查找 favicon 配置位置**: 检查 `public` 文件夹或 `index.html` 中的 favicon 配置。
2.  **准备 HB logo 图标**: 使用与 StoreLayout 中相同的 HB logo 设计（紫色渐变背景 + HB 文字）。
3.  **替换 favicon**: 将默认的 umi favicon 替换为 HB logo。

### 实施步骤
1.  **查找现有 favicon**: 检查 `public/favicon.ico` 或 `public/favicon.png` 是否存在。
2.  **创建 HB logo favicon**: 如果需要，创建新的 favicon 文件（可以使用 SVG 或 PNG 格式）。
3.  **更新 HTML**: 修改 `index.html` 中的 favicon 链接。