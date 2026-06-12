我将修改发票页面的 PDF 下载功能，确保生成的 PDF 每一页都包含页头（Logo、公司信息等）。

### 执行计划

1.  **修改 `Invoice/index.tsx` 的页面结构**:
    *   将页头相关的元素（Logo、公司信息、发票详情、客户信息）包裹在一个新的 `div` 中，ID 为 `invoice-header`。
    *   将主体内容相关的元素（商品表格、底部合计）包裹在另一个 `div` 中，ID 为 `invoice-body`。
    *   这样做是为了让 `html2canvas` 可以分别截取“页头”和“内容”。

2.  **重写 `handleDownloadPDF` 函数**:
    *   **步骤 1**: 使用 `html2canvas` 分别截取 `invoice-header` 和 `invoice-body`，得到两张图片。
    *   **步骤 2**: 初始化 PDF (A4 大小)。
    *   **步骤 3**: 计算页头图片在 PDF 中的高度 (`headerHeight`)。
    *   **步骤 4**: 计算每页剩余给内容的高度 (`contentAvailableHeight = A4高度 - headerHeight - 边距`)。
    *   **步骤 5**: 循环处理内容图片 (`bodyImg`)：
        *   使用临时的 Canvas 裁剪出当前页能放下的内容部分。
        *   如果不是第一页，调用 `pdf.addPage()`。
        *   在每页顶部绘制 **页头图片**。
        *   在页头下方绘制 **裁剪后的内容图片**。
        *   更新偏移量，直到内容处理完毕。

### 验证方式
*   代码修改完成后，我会检查 `handleDownloadPDF` 的逻辑，确保循环分页时正确调用了 `addImage` 来重复添加页头。

这个方案能在保留现有样式（图片版 PDF）的基础上，解决续页没有页头的问题。
