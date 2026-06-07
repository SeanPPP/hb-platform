import { useEffect, useState } from 'react'

export const MOBILE_BREAKPOINT = 767
export const PHONE_LANDSCAPE_MAX_HEIGHT = 500

export type MobileViewportSnapshot = {
  width: number
  height: number
  coarsePointer: boolean
}

export function resolveIsMobileViewport({ width, height, coarsePointer }: MobileViewportSnapshot) {
  if (width <= MOBILE_BREAKPOINT) {
    return true
  }

  return coarsePointer && height <= PHONE_LANDSCAPE_MAX_HEIGHT
}

function readMobileViewportSnapshot(): MobileViewportSnapshot {
  const viewport = window.visualViewport

  return {
    width: viewport?.width ?? window.innerWidth,
    height: viewport?.height ?? window.innerHeight,
    coarsePointer: window.matchMedia('(pointer: coarse)').matches,
  }
}

export function useIsMobile() {
  const [isMobile, setIsMobile] = useState(() => {
    if (typeof window === 'undefined') {
      return false
    }

    return resolveIsMobileViewport(readMobileViewportSnapshot())
  })

  useEffect(() => {
    let frameId: number | null = null

    const update = () => {
      frameId = null
      const next = resolveIsMobileViewport(readMobileViewportSnapshot())
      setIsMobile((current) => (current === next ? current : next))
    }

    // 横竖屏和地址栏收起会连续触发布局事件，合并到下一帧避免反复重算。
    const scheduleUpdate = () => {
      if (frameId !== null) {
        return
      }
      frameId = window.requestAnimationFrame(update)
    }

    const mediaQueries = [
      window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT}px)`),
      window.matchMedia(`(max-height: ${PHONE_LANDSCAPE_MAX_HEIGHT}px)`),
      window.matchMedia('(pointer: coarse)'),
    ]

    mediaQueries.forEach((mql) => mql.addEventListener('change', scheduleUpdate))
    window.addEventListener('resize', scheduleUpdate)
    window.addEventListener('orientationchange', scheduleUpdate)
    window.visualViewport?.addEventListener('resize', scheduleUpdate)
    scheduleUpdate()

    return () => {
      if (frameId !== null) {
        window.cancelAnimationFrame(frameId)
      }
      mediaQueries.forEach((mql) => mql.removeEventListener('change', scheduleUpdate))
      window.removeEventListener('resize', scheduleUpdate)
      window.removeEventListener('orientationchange', scheduleUpdate)
      window.visualViewport?.removeEventListener('resize', scheduleUpdate)
    }
  }, [])

  return isMobile
}
