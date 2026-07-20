import { useEffect, useState } from 'react'
import type { PreorderActivationItem } from '../../types/preorder'

export const PREORDER_MOBILE_BREAKPOINT = 720
export type PreorderRenderMode = 'desktop' | 'mobile'

export function getPreorderRenderMode(matchesMobileBreakpoint: boolean): PreorderRenderMode {
  return matchesMobileBreakpoint ? 'mobile' : 'desktop'
}

export function canStartPreorderSubmission(isEditable: boolean, selectedCount: number) {
  // autosave 是否正在执行不影响按钮可达性；提交函数会等待同一队列的当前 Promise。
  return isEditable && selectedCount > 0
}

export function usePreorderRenderMode() {
  const query = `(max-width: ${PREORDER_MOBILE_BREAKPOINT}px)`
  const [mode, setMode] = useState<PreorderRenderMode>(() => {
    if (typeof window === 'undefined') return 'desktop'
    return getPreorderRenderMode(window.matchMedia(query).matches)
  })

  useEffect(() => {
    const mediaQuery = window.matchMedia(query)
    const update = () => setMode(getPreorderRenderMode(mediaQuery.matches))
    mediaQuery.addEventListener('change', update)
    update()
    return () => mediaQuery.removeEventListener('change', update)
  }, [query])

  return mode
}

export interface PreorderItemsSummary {
  selectedCount: number
  totalQuantity: number
  totalImportAmount: number
}

export function summarizePreorderItems(
  items: PreorderActivationItem[],
  onVisit?: (item: PreorderActivationItem) => void,
): PreorderItemsSummary {
  return items.reduce<PreorderItemsSummary>((summary, item) => {
    onVisit?.(item)
    if (item.packCount > 0) summary.selectedCount += 1
    summary.totalQuantity += item.orderedQuantity
    summary.totalImportAmount += item.orderedQuantity * item.importPrice
    return summary
  }, { selectedCount: 0, totalQuantity: 0, totalImportAmount: 0 })
}
