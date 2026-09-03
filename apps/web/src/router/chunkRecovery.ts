import { reportRuntimeError } from '../utils/centerLogClient'

export const CHUNK_RELOAD_STORAGE_KEY = 'hb-web:chunk-reload-at'
export const CHUNK_RELOAD_WINDOW_MS = 5 * 60 * 1000

interface TimestampStorage {
  getItem: (key: string) => string | null
  setItem: (key: string, value: string) => void
}

interface VitePreloadErrorEvent extends Event {
  payload?: unknown
}

export function claimChunkReload(storage: TimestampStorage, now = Date.now()) {
  try {
    const previousValue = storage.getItem(CHUNK_RELOAD_STORAGE_KEY)
    const previousReloadAt = previousValue === null ? Number.NaN : Number(previousValue)

    if (Number.isFinite(previousReloadAt) && now - previousReloadAt < CHUNK_RELOAD_WINDOW_MS) {
      return false
    }

    storage.setItem(CHUNK_RELOAD_STORAGE_KEY, String(now))
    return true
  } catch {
    // sessionStorage 不可用时禁止自动刷新，避免在受限浏览器环境形成刷新循环。
    return false
  }
}

export function registerChunkPreloadRecovery() {
  window.addEventListener('vite:preloadError', (rawEvent) => {
    const event = rawEvent as VitePreloadErrorEvent
    reportRuntimeError('unhandledrejection', event.payload ?? 'Vite preload failed', {
      pathname: window.location.pathname,
      runtimeBoundary: 'vite-preload-error',
    })

    let shouldReload = false
    try {
      shouldReload = claimChunkReload(window.sessionStorage)
    } catch {
      shouldReload = false
    }

    if (!shouldReload) {
      return
    }

    // 只拦截首次 Vite 预加载失败；后续失败继续抛给路由错误边界。
    event.preventDefault()
    window.location.reload()
  })
}
