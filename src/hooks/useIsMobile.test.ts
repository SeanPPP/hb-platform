import { resolveIsMobileViewport } from './useIsMobile'

function assertEqual(actual: boolean, expected: boolean, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

assertEqual(
  resolveIsMobileViewport({ width: 390, height: 844, coarsePointer: true }),
  true,
  '手机竖屏应使用移动布局',
)

assertEqual(
  resolveIsMobileViewport({ width: 844, height: 390, coarsePointer: true }),
  true,
  '手机横屏应继续使用移动布局，避免切换布局壳后刷新页面',
)

assertEqual(
  resolveIsMobileViewport({ width: 932, height: 430, coarsePointer: true }),
  true,
  '大屏手机横屏应继续使用移动布局',
)

assertEqual(
  resolveIsMobileViewport({ width: 700, height: 900, coarsePointer: false }),
  true,
  '窄屏桌面窗口应保持原有移动布局行为',
)

assertEqual(
  resolveIsMobileViewport({ width: 1200, height: 800, coarsePointer: false }),
  false,
  '普通桌面视口应使用桌面布局',
)

assertEqual(
  resolveIsMobileViewport({ width: 1024, height: 768, coarsePointer: true }),
  false,
  '平板横屏不应被手机横屏规则强制切到移动布局',
)

console.log('useIsMobile.test: ok')
