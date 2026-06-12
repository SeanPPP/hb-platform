## 问题原因
- 右侧表格和左侧分类树都依赖动态高度 `paneHeight`（index.tsx:279-301），计算公式为 `vh - wrapperTop`，理论上应铺满页面。
- 右侧表格滚动高度 `tableScrollY` 目前计算为 `paneHeight - toolbarHeight - paginationHeight - 8`（index.tsx:279-301），同时表格外层容器还设置了 `paddingBottom: paginationHeight`（index.tsx:483）。
- 这会对分页高度发生“双重扣减/补偿”：滚动区域既减少了 `paginationHeight`，容器又额外腾出了同样的空间，导致底部出现可见的空白区域，即“还有可用空间没有被填满”。

## 修改方案
1. 统一滚动区域与分页的关系
- 在两处 `useLayoutEffect`（index.tsx:279-301）将 `tableScrollY` 修改为不再减去分页高度：`Math.max(200, paneHeight - toolbarHeight - 8)`。
- 这样滚动区 = 有效高度 - 工具栏 - 间距；分页条作为 sticky 元素，不占用滚动高度。

2. 移除容器的额外底部留白
- 去掉 `warehouse-table-wrapper` 的 `paddingBottom: paginationHeight`（index.tsx:483），避免重复空白。

3. 保持分页吸底显示
- 保留分页容器的 `position: sticky; bottom: 0;`（index.tsx:542），分页始终贴紧卡片底部，不会超过页面高度。

4. 左侧分类树同步铺满
- 左侧 `Resizable` 内层容器已设置 `height: paneHeight`（index.tsx:315），保持 `overflow: auto`（index.tsx:367）即可，无需额外改动。如果仍有轻微空白，可将左侧 Card 的 `bodyStyle` padding 从 `16` 调整为 `0`，但默认保持现状以不影响视觉密度。

5. 兼容窗口缩放与分页尺寸变化
- 保留现有监听（index.tsx:292, 296-301），仅调整 `tableScrollY` 的计算项，确保在窗口缩放、分页条显隐/尺寸变化时高度动态正确。

## 验证步骤
- 打开页面并在不同分辨率下滚动与缩放窗口，观察底部空白是否消失，表格与分类树是否正好填满可视高度。
- 验证分页条不遮挡表格最后一行；滚动到末尾时，最后一行依然可见。
- 切换“隐藏分类/仓库商品管理”与管理模式，确认高度自适应正常。

## 影响与风险
- 改动仅涉及高度与内边距计算，不影响数据加载与交互逻辑。
- 如 UI 习惯依赖底部留白，去除 `paddingBottom` 可能让分页更贴底；若需保留微量缓冲，可改为固定 8-12px 的容器 `paddingBottom` 而非使用 `paginationHeight`。