import { readMobileViewportSnapshot, resolveIsMobileViewport } from './useIsMobile'

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

const originalWindow = Object.getOwnPropertyDescriptor(globalThis, 'window')
const originalDocument = Object.getOwnPropertyDescriptor(globalThis, 'document')
const layoutViewport = { clientWidth: 1024, clientHeight: 768 }
const visualViewport = { width: 1024, height: 768 }
const browserWindow = {
  innerWidth: 1024,
  innerHeight: 768,
  visualViewport,
  matchMedia: () => ({ matches: true }),
}

try {
  Object.defineProperty(globalThis, 'window', { configurable: true, value: browserWindow })
  Object.defineProperty(globalThis, 'document', {
    configurable: true,
    value: { documentElement: layoutViewport },
  })

  // 重放 iPad 键盘弹出、收起及输入框自动放大；只改变可见区域，页面壳必须保持。
  for (const viewport of [
    { width: 1024, height: 768 },
    { width: 1024, height: 430 },
    { width: 680, height: 280 },
    { width: 1024, height: 768 },
  ]) {
    Object.assign(visualViewport, viewport)
    assertEqual(
      resolveIsMobileViewport(readMobileViewportSnapshot()),
      false,
      `平板可见区域 ${viewport.width}×${viewport.height} 变化时不应重建手机布局`,
    )
  }

  Object.assign(layoutViewport, { clientWidth: 844, clientHeight: 390 })
  assertEqual(
    resolveIsMobileViewport(readMobileViewportSnapshot()),
    true,
    '真实手机横屏布局视口应继续使用移动布局',
  )

  Object.assign(layoutViewport, { clientWidth: 700, clientHeight: 900 })
  assertEqual(
    resolveIsMobileViewport(readMobileViewportSnapshot()),
    true,
    '平板分屏或窗口真正变窄时应切换移动布局',
  )

  Object.assign(layoutViewport, { clientWidth: 1024, clientHeight: 768 })
  assertEqual(
    resolveIsMobileViewport(readMobileViewportSnapshot()),
    false,
    '退出分屏后应恢复桌面布局',
  )

  Object.assign(layoutViewport, { clientWidth: 0, clientHeight: 0 })
  assertEqual(
    resolveIsMobileViewport(readMobileViewportSnapshot()),
    false,
    '根元素尺寸暂不可用时应回退到窗口布局尺寸',
  )
} finally {
  for (const [name, descriptor] of [
    ['window', originalWindow],
    ['document', originalDocument],
  ] as const) {
    if (descriptor) Object.defineProperty(globalThis, name, descriptor)
    else Reflect.deleteProperty(globalThis, name)
  }
}

console.log('useIsMobile.test: ok')
