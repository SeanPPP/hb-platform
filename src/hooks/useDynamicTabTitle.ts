import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'
import { useTabsStore } from '../store/tabs'

export function useDynamicTabTitle(title?: string) {
  const location = useLocation()
  const updateTabTitle = useTabsStore((state) => state.updateTabTitle)

  useEffect(() => {
    if (!title) {
      return
    }
    updateTabTitle(location.pathname, title)
  }, [location.pathname, title, updateTabTitle])
}
