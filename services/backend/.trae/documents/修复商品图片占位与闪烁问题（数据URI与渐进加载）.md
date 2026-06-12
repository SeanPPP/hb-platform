## 原因分析
- 当前占位路径 `'/node_modules/@umijs/preset-umi/assets/umi.png'` 不是公开静态目录，构建/部署时可能不可访问，导致占位也加载失败。
- 先给 `img.src` 赋值为无效真实地址，浏览器先显示裂图，随后 `onerror` 才回退，占位生效前出现闪烁。

## 改进方案
1. 使用内联 Data URI 作为占位图
- 采用嵌入式 SVG 的 Data URI（轻量、可用性最高），避免任何静态路径问题。
- 定义 `const FALLBACK_IMAGE_URL = 'data:image/svg+xml;utf8,<svg ...>...</svg>'`。

2. 渐进式加载真实图片以消除闪烁
- 渲染器创建 `<img>` 时，初始 `src` 设为占位图。
- 若存在 `productImage`：
  - 创建 `const loader = new Image()` 并设置 `loader.src = productImage`；
  - `loader.onload` 时将表格单元内 `<img>` 的 `src` 切换为真实图片；
  - `loader.onerror` 保持占位图，完全避免裂图闪烁。
- 继续保留 `loading='lazy'` 与 `decoding='async'`。

3. 细节优化
- 明确固定显示区域尺寸（宽/高），避免布局跳动。
- 对空字符串/无效 URL 做去除空白与简单校验：仅当以 `http(s)://` 或 `/` 开头时尝试加载。

## 前端改动点
- 仅修改 `ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx` 中图片列的 renderer 与占位常量。

## 验证
- 无图数据行：直接显示占位图。
- 错误 URL：不出现裂图图标，保持占位。
- 正确 URL：先占位，图片加载完成后平滑替换。

如确认，立即按以上方案更新并回归验证。