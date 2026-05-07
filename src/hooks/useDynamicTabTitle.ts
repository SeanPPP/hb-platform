import { useEffect, useRef } from 'react'
import { useLocation } from 'react-router-dom'
import { useTabsStore } from '../store/tabs'

export function useDynamicTabTitle(title?: string) {
  const location = useLocation()
  const updateTabTitle = useTabsStore((state) => state.updateTabTitle)
  const stableTabKeyRef = useRef(location.pathname)

  useEffect(() => {
    if (!title) {
      return
    }
    updateTabTitle(stableTabKeyRef.current, title)
  }, [title, updateTabTitle])
}
