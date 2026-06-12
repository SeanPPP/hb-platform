## 修复国际化缺失问题

在两个国际化文件中添加 `'common.download'` 键：

1. **修改 `zh-CN.ts`**：在通用（common）区域添加 `'common.download': '下载',`
2. **修改 `en-US.ts`**：在通用（common）区域添加 `'common.download': 'Download',`

这样 PDF 预览模态框中的下载按钮就能正确显示中文或英文了。