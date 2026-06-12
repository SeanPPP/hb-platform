**原因分析**:
代码中 `src/pages/StoreOrder/index.tsx` 的布局高度被硬编码为 `80vh`，这导致了在大屏幕上底部出现留白，或者在不同分辨率下高度不匹配。

```typescript
<Layout style={{ height: '80vh', background: '#fff' }}>
```

`PageContainer` 通常会处理页头，剩余空间应该被内容占满。

**解决方案**:
1. 修改 `PageContainer` 的样式，移除 `height: 'calc(100vh - 56px)'`，因为 ProComponents 的 PageContainer 通常会自动处理布局。或者保留但确保内部容器能撑满。
2. 将 `Layout` 的 `height` 改为 `calc(100vh - 120px)` (大致减去头部高度) 或者 `100%` (如果父容器已经有固定高度)。
3. 更稳妥的做法是让 `Layout` 占满剩余空间。

考虑到 `PageContainer` 的默认行为，我们可以尝试将内部 `Layout` 的高度设置为 `calc(100vh - 110px)` (预估 Header 高度 + Padding)，或者直接使用 `height: '100%'` 并确保父级 `PageContainer` 的 `childrenContentStyle` 被正确设置。

根据 Ant Design Pro 的常见实践，我们可以这样修改：

1. `PageContainer` 设置 `fixedHeader` (如果需要)。
2. 内部 `Layout` 设置 `height: 'calc(100vh - 100px)'` 左右，或者更精确的计算。

鉴于用户反馈“高度不够，底部还有留白”，说明 `80vh` 太小了。

**建议修改**:
将 `Layout` 的高度改为 `calc(100vh - 110px)`。这里的 `110px` 大概是 Ant Design Pro 顶部导航栏 + PageContainer Header 的高度。

或者，更简单粗暴一点，设为 `height: '85vh'` 或 `90vh`，但 `calc` 更准确。

如果不确定 Header 的确切高度，可以使用 flex 布局让其自动填充，但这依赖于 `PageContainer` 的实现细节。

让我们先尝试调整为 `calc(100vh - 120px)`，这是一个比较安全的也是常用的全屏高度计算方式（去除顶部导航栏）。

**Update**:
Actually, `PageContainer` with `header` usually takes up some space.
Let's try changing:
`style={{ height: '80vh', background: '#fff' }}`
to
`style={{ height: 'calc(100vh - 130px)', background: '#fff' }}`
(Assuming ~56px for ProLayout header and ~48-60px for PageHeader)

And remove the style on `PageContainer` itself if it's not needed, or update it to ensure it passes full height.

Let's modify `src/pages/StoreOrder/index.tsx`:

```typescript
// Line 81
<Layout style={{ height: 'calc(100vh - 130px)', background: '#fff' }}>
```

并且 `PageContainer` 上的 `style={{ height: 'calc(100vh - 56px)' }}` 实际上是作用在容器上的，可能没有生效或者被覆盖。

让我们只修改 `Layout` 的高度。

