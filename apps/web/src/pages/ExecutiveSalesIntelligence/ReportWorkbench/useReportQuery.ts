import { useEffect, useRef, useState } from 'react'

export interface ReportSnapshot<T> {
  data: T
  statisticStatus?: string
  statisticMessage?: string | null
  statisticUpdatedAt?: string | null
  cacheVersion?: string | null
}
export interface ReportQueryState<T> {
  key: string
  snapshot?: ReportSnapshot<T>
  loading: boolean
  slow: boolean
  error?: string
  cached?: boolean
  durationMs?: number
}

export function isCompleteSnapshot(snapshot: ReportSnapshot<unknown>): boolean {
  return !snapshot.statisticStatus || snapshot.statisticStatus.toLowerCase() === 'fresh'
}

/** 每个面板独立加载；缓存留在组件内且查询键包含用户范围，不跨登录共享数据。 */
export function useReportQuery<T>(key: string, loader: (signal: AbortSignal) => Promise<ReportSnapshot<T>>,
  { active = true, enabled = true, refresh = 0, metricId }: { active?: boolean; enabled?: boolean; refresh?: number; metricId: string }) {
  const [state, setState] = useState<ReportQueryState<T>>({ key, loading: enabled, slow: false })
  const loaderRef = useRef(loader)
  loaderRef.current = loader
  const cache = useRef(new Map<string, { expires: number; value: ReportSnapshot<T> }>())
  const previousRefresh = useRef(refresh)
  useEffect(() => {
    if (!active || !enabled) return
    const force = previousRefresh.current !== refresh
    previousRefresh.current = refresh
    const cached = cache.current.get(key)
    if (!force && cached && cached.expires > Date.now()) {
      setState({ key, snapshot: cached.value, loading: false, slow: false, cached: true, durationMs: 0 })
      return
    }
    const controller = new AbortController()
    const started = performance.now()
    let disposed = false
    let retryTimer = 0
    let firstFrame = 0
    let secondFrame = 0
    let attempts = 0
    setState({ key, loading: true, slow: false })
    const slowTimer = window.setTimeout(() => {
      if (!disposed) setState(current => ({ ...current, slow: true }))
    }, 3000)
    const timeout = window.setTimeout(() => controller.abort(), 12000)
    const load = async () => {
      retryTimer = 0
      try {
        const snapshot = await loaderRef.current(controller.signal)
        if (disposed) return
        if (!isCompleteSnapshot(snapshot)) {
          const failed = snapshot.statisticStatus?.toLowerCase() === 'failed'
          if (!failed && attempts < 4 && performance.now() - started < 8000) {
            setState(current => ({ ...current, key, snapshot, loading: true }))
            retryTimer = window.setTimeout(load, [250, 500, 1000, 2000][attempts++])
            return
          }
          setState({ key, snapshot, loading: false, slow: false,
            error: snapshot.statisticMessage || '统计尚未准备完成，请稍后刷新 / Statistics are not ready.' })
          return
        }
        cache.current.delete(key)
        cache.current.set(key, { expires: Date.now() + 30000, value: snapshot })
        if (cache.current.size > 48) cache.current.delete(cache.current.keys().next().value!)
        setState({ key, snapshot, loading: false, slow: false, durationMs: performance.now() - started })
        // 两帧后记录“查询开始到已绘制数据”；不把骨架屏或 Pending 空包算作数据。
        firstFrame = requestAnimationFrame(() => {
          secondFrame = requestAnimationFrame(() => {
            if (disposed) return
            performance.measure(`hb-report:${metricId}:data-painted`, { start: started, end: performance.now() })
          })
        })
      } catch (error) {
        if (!disposed) setState({ key, loading: false, slow: false,
          error: controller.signal.aborted ? '查询超时，请刷新重试 / Request timed out.' : error instanceof Error ? error.message : '加载失败 / Unable to load' })
      } finally {
        if (!retryTimer) { window.clearTimeout(timeout); window.clearTimeout(slowTimer) }
      }
    }
    void load()
    return () => {
      disposed = true
      controller.abort()
      window.clearTimeout(retryTimer)
      window.clearTimeout(timeout)
      window.clearTimeout(slowTimer)
      cancelAnimationFrame(firstFrame)
      cancelAnimationFrame(secondFrame)
    }
  }, [key, active, enabled, refresh, metricId])
  // React effect 运行前的一帧也不能把旧范围的数据贴在新筛选条件下。
  const current = state.key === key ? state : { key, loading: enabled, slow: false }
  return { ...current, loading: enabled && current.loading,
    data: enabled && current.snapshot && isCompleteSnapshot(current.snapshot) && !current.error ? current.snapshot.data : undefined }
}
