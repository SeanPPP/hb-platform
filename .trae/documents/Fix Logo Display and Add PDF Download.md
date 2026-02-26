# 发票页面改进计划

我将修复 Logo 显示问题并实现 PDF 导出下载功能。

## 1. 修复 Logo 显示
用户反馈 "logo显示不对"。这可能是因为导入路径问题，或者 Webpack 配置导致 SVG 被作为组件而非 URL 导入。

*   **操作**: 将 Logo 文件放入 `public` 目录，通过绝对路径引用，避免 Webpack 打包问题。
*   **具体步骤**:
    1.  在 `public` 目录下创建 `logo.svg` (或者如果您有具体图片文件，请再次上传，目前我将使用一个更清晰的文本 SVG 作为占位)。
    2.  修改 `Invoice/index.tsx`，将 `img src` 指向 `/logo.svg`。

## 2. 实现 PDF 导出下载
用户需要直接 "下载 PDF" 而不仅仅是打印。

*   **方案**: 使用 `html2canvas` 和 `jspdf` 库在客户端生成 PDF。
*   **具体步骤**:
    1.  安装依赖: `pnpm add html2canvas jspdf`。
    2.  在 `Invoice/index.tsx` 中引入这两个库。
    3.  添加 "Download PDF" 按钮。
    4.  实现 `handleDownloadPDF` 函数：
        *   将发票容器 (`#invoice-container`) 渲染为 Canvas。
        *   将 Canvas 转换为图片数据。
        *   创建 A4 尺寸的 PDF 文档并将图片写入。
        *   触发下载。

## 执行步骤
1.  **创建 Logo**: 在 `public/logo.svg` 写入 SVG 文件。
2.  **安装依赖**: 运行 `pnpm add html2canvas jspdf`。
3.  **更新组件**:
    *   修改 Logo 引用路径。
    *   实现 PDF 下载逻辑。
    *   添加下载按钮。

## 验证
*   确认 Logo 是否正常显示。
*   点击下载按钮，确认 PDF 文件是否生成并包含完整发票内容。
