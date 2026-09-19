// 分店价格更新通知：X-Price-Notification 响应头的解析与提示文案拼装（纯函数，仓库商品页与监控页共用）。

export const PRICE_NOTIFICATION_HEADER = 'x-price-notification'
export const PRICE_UPDATE_TASKS_PATH = '/warehouse/products/price-update-tasks'

export interface PriceNotificationSummary {
  productCount: number
  needsPriceUpdateStores: number
  labelOnlyStores: number
  cancelledStores: number
  skippedSpecialStores: number
  hasAny: boolean
}

export type PriceNotificationTone = 'success' | 'info' | 'warning'

export interface PriceNotificationView {
  tone: PriceNotificationTone
  title: string
  /** 标题下方的补充说明行（不含标签）。 */
  lines: string[]
  tags: Array<{ kind: 'priceUpdate' | 'labelOnly'; text: string }>
  /** 只有真正产生了分店通知才值得跳去看执行情况。 */
  showTasksLink: boolean
}

export interface PriceNotificationPreview {
  affectedStores: number
  skippedSpecialStores: number
}

function toCount(value: unknown): number {
  const parsed = typeof value === 'number' ? value : Number(value)
  return Number.isFinite(parsed) && parsed > 0 ? Math.floor(parsed) : 0
}

/** 把已解析的对象（响应头 JSON 或批量任务快照里的 priceNotification）规范成汇总；不是对象则返回 null。 */
export function normalizePriceNotificationSummary(raw: unknown): PriceNotificationSummary | null {
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) return null
  const record = raw as Record<string, unknown>
  const needsPriceUpdateStores = toCount(record.needsPriceUpdateStores ?? record.NeedsPriceUpdateStores)
  const labelOnlyStores = toCount(record.labelOnlyStores ?? record.LabelOnlyStores)
  const cancelledStores = toCount(record.cancelledStores ?? record.CancelledStores)
  return {
    productCount: toCount(record.productCount ?? record.ProductCount),
    needsPriceUpdateStores,
    labelOnlyStores,
    cancelledStores,
    skippedSpecialStores: toCount(record.skippedSpecialStores ?? record.SkippedSpecialStores),
    // hasAny 以数字为准重新推导，避免头里的布尔值与计数不一致时误报。
    hasAny: needsPriceUpdateStores > 0 || labelOnlyStores > 0 || cancelledStores > 0,
  }
}

/** 头不存在或解析失败一律返回 null（静默忽略，不打扰保存流程）。 */
export function parsePriceNotificationHeader(raw: string | null | undefined): PriceNotificationSummary | null {
  if (!raw || !raw.trim()) return null
  try {
    return normalizePriceNotificationSummary(JSON.parse(raw))
  } catch {
    return null
  }
}

/**
 * 一次保存可能连发多个请求（先普通保存、再建议折扣）。
 * 汇总不能相加（会重复计数），始终以最后一个带头的响应为准；不带头的响应不覆盖之前的结果。
 */
export function createPriceNotificationCapture() {
  let latest: PriceNotificationSummary | null = null
  return {
    onResponse(response: { headers: { get(name: string): string | null } }) {
      const parsed = parsePriceNotificationHeader(response.headers.get(PRICE_NOTIFICATION_HEADER))
      if (parsed) latest = parsed
    },
    accept(summary: PriceNotificationSummary | null | undefined) {
      if (summary) latest = summary
    },
    getSummary() {
      return latest
    },
  }
}

type Translate = (key: string, options?: Record<string, unknown>) => string

export function buildPriceNotificationView(
  summary: PriceNotificationSummary | null | undefined,
  t: Translate,
): PriceNotificationView | null {
  if (!summary) return null
  const notified = summary.needsPriceUpdateStores + summary.labelOnlyStores
  const lines: string[] = []
  const tags: PriceNotificationView['tags'] = []

  if (notified === 0 && summary.cancelledStores === 0) {
    return {
      tone: 'info',
      title: t('warehouse.priceNotification.noneTitle'),
      lines: [t('warehouse.priceNotification.noneDescription')],
      tags,
      showTasksLink: false,
    }
  }

  if (summary.needsPriceUpdateStores > 0) {
    tags.push({ kind: 'priceUpdate', text: t('warehouse.priceNotification.needsPriceUpdate', { count: summary.needsPriceUpdateStores }) })
  }
  if (summary.labelOnlyStores > 0) {
    tags.push({ kind: 'labelOnly', text: t('warehouse.priceNotification.labelOnly', { count: summary.labelOnlyStores }) })
  }
  if (summary.skippedSpecialStores > 0) {
    lines.push(t('warehouse.priceNotification.skippedSpecial', { count: summary.skippedSpecialStores }))
  }

  if (notified === 0) {
    return {
      tone: 'warning',
      title: t('warehouse.priceNotification.cancelledTitle', { count: summary.cancelledStores }),
      lines,
      tags,
      showTasksLink: false,
    }
  }

  if (summary.cancelledStores > 0) {
    lines.push(t('warehouse.priceNotification.cancelledAlso', { count: summary.cancelledStores }))
  }
  return {
    tone: 'success',
    title: t('warehouse.priceNotification.sentTitle'),
    lines,
    tags,
    showTasksLink: true,
  }
}

/** 保存前预告文案；N=0 返回 null（不显示）。只改建议折扣时分店收到的是「需改价」而非「待换标签」。 */
export function buildPriceNotificationPreviewText(
  preview: PriceNotificationPreview | null | undefined,
  retailPriceChanged: boolean,
  t: Translate,
): string | null {
  if (!preview || preview.affectedStores <= 0) return null
  const base = t(
    retailPriceChanged ? 'warehouse.priceNotification.previewRetail' : 'warehouse.priceNotification.previewDiscountOnly',
    { count: preview.affectedStores },
  )
  const skipped = preview.skippedSpecialStores > 0
    ? t('warehouse.priceNotification.previewSkipped', { count: preview.skippedSpecialStores })
    : ''
  return `${base}${skipped}${t('warehouse.priceNotification.previewEnd')}`
}

/** 表单里的「建议折扣 %」(0~100，留空 = 未设置) → 接口的减免比例 (0~1 / null)。 */
export function suggestedDiscountPercentToRate(percent: number | null | undefined): number | null {
  if (percent === null || percent === undefined || !Number.isFinite(percent)) return null
  return Math.round(Math.min(100, Math.max(0, percent)) * 100) / 10000
}

export function suggestedDiscountRateToPercent(rate: number | null | undefined): number | null {
  if (rate === null || rate === undefined || !Number.isFinite(rate)) return null
  return Math.round(rate * 10000) / 100
}

/** 建议折后价：零售价或折扣未知时返回 null（只读展示 "--"）。 */
export function computeSuggestedDiscountedPrice(
  retailPrice: number | null | undefined,
  percent: number | null | undefined,
): number | null {
  const rate = suggestedDiscountPercentToRate(percent)
  if (retailPrice === null || retailPrice === undefined || !Number.isFinite(retailPrice) || rate === null) return null
  return Math.round(retailPrice * (1 - rate) * 100) / 100
}

export function buildPriceUpdateTasksLink(params: {
  tab?: 'by-store' | 'by-product' | 'tasks'
  keyword?: string
  storeCode?: string
  hqSyncFailedOnly?: boolean
}): string {
  const search = new URLSearchParams()
  if (params.tab) search.set('tab', params.tab)
  if (params.keyword?.trim()) search.set('keyword', params.keyword.trim())
  if (params.storeCode?.trim()) search.set('storeCode', params.storeCode.trim())
  if (params.hqSyncFailedOnly) search.set('hqSyncFailedOnly', 'true')
  const query = search.toString()
  return query ? `${PRICE_UPDATE_TASKS_PATH}?${query}` : PRICE_UPDATE_TASKS_PATH
}
