interface InvoiceTableScrollRestoreOptions {
  requestFrame: (callback: () => void) => number
  cancelFrame: (frameId: number) => void
  restore: () => void
}

export function getNextInvoiceTableScrollTop(
  active: boolean,
  savedScrollTop: number,
  eventScrollTop: number,
) {
  // KeepAlive 隐藏表格时可能派发 scrollTop=0；隐藏期必须保留用户最后看到的位置。
  return active ? eventScrollTop : savedScrollTop
}

export function shouldRestoreInvoiceTableScroll(wasActive: boolean, active: boolean) {
  return !wasActive && active
}

export function scheduleInvoiceTableScrollRestore({
  requestFrame,
  cancelFrame,
  restore,
}: InvoiceTableScrollRestoreOptions) {
  let pendingFrameId: number | null = requestFrame(() => {
    // 第一帧等待 KeepAlive 节点恢复布局，第二帧再恢复 AntD 表体滚动位置。
    pendingFrameId = requestFrame(() => {
      pendingFrameId = null
      restore()
    })
  })

  return () => {
    if (pendingFrameId !== null) {
      cancelFrame(pendingFrameId)
      pendingFrameId = null
    }
  }
}
