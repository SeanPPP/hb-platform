/**
 * 门店促销海报（Web 批量打印）纯逻辑：不依赖 React / antd / 请求封装，便于 esbuild + node 直接单测。
 * 海报内容全部是英文、PDF 由后端生成；这里负责默认值归一化、草稿构建、行级校验、页数估算与请求体构建。
 * 口径与后端 PromoPosterRequestParser、移动端 promo-posters/logic 保持一致。
 */

export const PROMO_POSTER_KINDS = ['special', 'multibuy', 'new', 'clearance'] as const
export const PROMO_POSTER_STYLES = ['classic', 'modern'] as const
export const PROMO_POSTER_SIZES = ['A4', 'A5', 'A6', 'A7'] as const

export type PromoPosterKind = (typeof PROMO_POSTER_KINDS)[number]
export type PromoPosterStyle = (typeof PROMO_POSTER_STYLES)[number]
export type PromoPosterSize = (typeof PROMO_POSTER_SIZES)[number]

/** 后端单次最多生成 200 张。 */
export const PROMO_POSTER_MAX_COUNT = 200
/** 英文品名上限（与后端 MaxTitleLength 一致）。 */
export const PROMO_POSTER_TITLE_MAX_LENGTH = 80
/** 价格上限：与移动端一致取四位整数，再大海报数字会被压得过小（后端硬上限 99999.99）。 */
export const PROMO_POSTER_PRICE_MAX = 9999.99
/** 读取默认值的并发数：同时最多 6 个请求，避免选中 200 个商品时瞬间打满后端。 */
export const PROMO_POSTER_DEFAULTS_CONCURRENCY = 6

/** 拼到 A4 时每页可放的张数（A4 每页 1、A5 2、A6 4、A7 8）。 */
export const PROMO_POSTER_PER_A4: Record<PromoPosterSize, number> = {
  A4: 1,
  A5: 2,
  A6: 4,
  A7: 8,
}

/** 默认类型优先级（也是下拉展示顺序）：特价 > 清仓 > 多件价 > 新品（新品始终可用，兜底）。 */
export const PROMO_POSTER_KIND_PRIORITY: readonly PromoPosterKind[] = ['special', 'clearance', 'multibuy', 'new']

export interface PromoPosterMultiBuyOffer {
  promotionId: string
  name: string
  applyQuantity: number
  fixedPrice: number
  /** 原始时间字符串（如 2026-09-19T00:00:00），构建请求时只取日期部分。 */
  effectiveStart: string
  effectiveEnd: string
  productsCount: number
}

export interface PromoPosterDefaults {
  productCode: string
  itemNumber: string
  productName: string
  englishName: string
  /** 建议印在海报上的英文名；为空表示没有可打印的英文名，需要手填。 */
  posterTitle: string
  retailPrice: number | null
  discountRate: number | null
  discountedPrice: number | null
  clearancePrice: number | null
  multiBuyOffers: PromoPosterMultiBuyOffer[]
  canSpecial: boolean
  canMultiBuy: boolean
  canClearance: boolean
}

/** 选中商品的基础信息（来自价格列表行，用于在默认值返回前先展示行）。 */
export interface PromoPosterProduct {
  productCode: string
  productName?: string
  itemNumber?: string
}

/** 每行可编辑的海报草稿；价格来自 InputNumber，空值为 null。 */
export interface PromoPosterDraft {
  kind: PromoPosterKind
  title: string
  /** special: 现价；clearance: 清仓价；new: 售价；multibuy: 组合价。 */
  price: number | null
  /** special / clearance 的原价（划线价）。 */
  wasPrice: number | null
  /** multibuy 选中的促销。 */
  offerId: string | null
}

/** POST promo-posters/pdf 单张海报；按类型只带后端需要的字段。 */
export interface PromoPosterItemRequest {
  kind: PromoPosterKind
  style: PromoPosterStyle
  size: PromoPosterSize
  productCode: string
  itemNumber?: string
  title: string
  price: number
  wasPrice?: number
  quantity?: number
  unitPrice?: number
  mixAndMatch?: boolean
  validFrom?: string
  validTo?: string
  inStoreSince?: string
}

export interface PromoPosterPdfRequest {
  storeCode: string
  impose: boolean
  showLogo: boolean
  posters: PromoPosterItemRequest[]
}

// ---------------------------------------------------------------- 通用工具

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

/** 同时兼容 camelCase / PascalCase 字段名。 */
function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) return raw[key]
  }
  return undefined
}

function asString(value: unknown) {
  if (typeof value === 'string') return value
  if (typeof value === 'number' && Number.isFinite(value)) return String(value)
  return ''
}

function asNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'string' && !value.trim()) return null
  const parsed = typeof value === 'string' ? Number(value) : value
  return typeof parsed === 'number' && Number.isFinite(parsed) ? parsed : null
}

function asBoolean(value: unknown) {
  if (typeof value === 'boolean') return value
  if (typeof value === 'number') return value !== 0
  if (typeof value === 'string') return ['true', '1'].includes(value.trim().toLowerCase())
  return false
}

export function roundMoney(value: number) {
  return Math.round(value * 100) / 100
}

/** 金额比较按分：浮点误差不应触发「价格不一致」提示。 */
function sameMoney(a: number, b: number) {
  return Math.abs(a - b) < 0.005
}

function pad2(value: number) {
  return String(value).padStart(2, '0')
}

function isValidDateOnly(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return false
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const date = new Date(year, month - 1, day)
  return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day
}

/** 促销起止时间（如 2026-09-19T00:00:00）只取日期部分；无法识别时返回 undefined。 */
export function toDateOnly(value: string | null | undefined) {
  const head = (value ?? '').trim().slice(0, 10)
  return isValidDateOnly(head) ? head : undefined
}

/** 页面展示用金额：$9.09。 */
export function formatPosterMoney(value: number) {
  return `$${roundMoney(value).toFixed(2)}`
}

// ---------------------------------------------------------------- defaults 归一化

function normalizeOffer(raw: unknown): PromoPosterMultiBuyOffer | null {
  const record = asRecord(raw)
  if (!record) return null
  const promotionId = asString(pick(record, 'promotionId', 'PromotionId')).trim()
  const applyQuantity = Math.trunc(asNullableNumber(pick(record, 'applyQuantity', 'ApplyQuantity')) ?? 0)
  const fixedPrice = asNullableNumber(pick(record, 'fixedPrice', 'FixedPrice')) ?? 0
  // 件数 < 2 或组合价 ≤ 0 的促销印不成「N for $X」，直接丢弃（后端也做了同样过滤）。
  if (!promotionId || applyQuantity < 2 || fixedPrice <= 0) return null
  return {
    promotionId,
    name: asString(pick(record, 'name', 'Name')).trim(),
    applyQuantity,
    fixedPrice: roundMoney(fixedPrice),
    effectiveStart: asString(pick(record, 'effectiveStart', 'EffectiveStart')),
    effectiveEnd: asString(pick(record, 'effectiveEnd', 'EffectiveEnd')),
    productsCount: Math.max(0, Math.trunc(asNullableNumber(pick(record, 'productsCount', 'ProductsCount')) ?? 0)),
  }
}

/** defaults 接口 data → 前端结构；缺商品编码视为无效返回 null。 */
export function normalizePromoPosterDefaults(raw: unknown): PromoPosterDefaults | null {
  const record = asRecord(raw)
  if (!record) return null
  const productCode = asString(pick(record, 'productCode', 'ProductCode')).trim()
  if (!productCode) return null
  const offersRaw = pick(record, 'multiBuyOffers', 'MultiBuyOffers')
  const multiBuyOffers = Array.isArray(offersRaw)
    ? offersRaw.map(normalizeOffer).filter((offer): offer is PromoPosterMultiBuyOffer => offer !== null)
    : []
  const positive = (value: unknown) => {
    const parsed = asNullableNumber(value)
    return parsed !== null && parsed > 0 ? roundMoney(parsed) : null
  }
  return {
    productCode,
    itemNumber: asString(pick(record, 'itemNumber', 'ItemNumber')).trim(),
    productName: asString(pick(record, 'productName', 'ProductName')).trim(),
    englishName: asString(pick(record, 'englishName', 'EnglishName')).trim(),
    posterTitle: asString(pick(record, 'posterTitle', 'PosterTitle')).trim(),
    retailPrice: positive(pick(record, 'retailPrice', 'RetailPrice')),
    discountRate: asNullableNumber(pick(record, 'discountRate', 'DiscountRate')),
    discountedPrice: positive(pick(record, 'discountedPrice', 'DiscountedPrice')),
    clearancePrice: positive(pick(record, 'clearancePrice', 'ClearancePrice')),
    multiBuyOffers,
    canSpecial: asBoolean(pick(record, 'canSpecial', 'CanSpecial')),
    canMultiBuy: asBoolean(pick(record, 'canMultiBuy', 'CanMultiBuy')),
    canClearance: asBoolean(pick(record, 'canClearance', 'CanClearance')),
  }
}

// ---------------------------------------------------------------- 类型可用性与默认草稿

/** 特价和新品始终可用；多件价和清仓仍依赖门店促销数据。 */
export function resolvePosterKindAvailability(defaults: PromoPosterDefaults): Record<PromoPosterKind, boolean> {
  return {
    // Special 允许店员手填价格，即使当前没有系统折扣；默认类型仍由真实折扣单独决定。
    special: true,
    multibuy: defaults.canMultiBuy && defaults.multiBuyOffers.length > 0,
    new: true,
    clearance: defaults.canClearance,
  }
}

/** 有真实折扣时优先特价，否则按 清仓 > 多件价 > 新品 选择。 */
export function pickDefaultPosterKind(defaults: PromoPosterDefaults): PromoPosterKind {
  const availability = resolvePosterKindAvailability(defaults)
  const hasEffectiveDiscount = defaults.retailPrice !== null
    && defaults.discountedPrice !== null
    && defaults.discountedPrice < defaults.retailPrice
  const defaultPriority: readonly PromoPosterKind[] = hasEffectiveDiscount
    ? PROMO_POSTER_KIND_PRIORITY
    : ['clearance', 'multibuy', 'new']
  return defaultPriority.find((kind) => availability[kind]) ?? 'new'
}

/** 切换类型：按新类型重置价格与促销，品名保持店员当前输入。 */
export function applyPosterKind(
  draft: PromoPosterDraft,
  defaults: PromoPosterDefaults,
  kind: PromoPosterKind,
): PromoPosterDraft {
  switch (kind) {
    case 'special':
      {
        const hasEffectiveDiscount = defaults.retailPrice !== null
          && defaults.discountedPrice !== null
          && defaults.discountedPrice < defaults.retailPrice
        return {
          ...draft,
          kind,
          price: hasEffectiveDiscount ? defaults.discountedPrice : defaults.retailPrice,
          wasPrice: hasEffectiveDiscount ? defaults.retailPrice : null,
          offerId: null,
        }
      }
    case 'clearance':
      return { ...draft, kind, price: defaults.clearancePrice, wasPrice: defaults.retailPrice, offerId: null }
    case 'new':
      return { ...draft, kind, price: defaults.retailPrice, wasPrice: null, offerId: null }
    case 'multibuy':
      return applyMultiBuyOffer({ ...draft, kind, wasPrice: null }, defaults, defaults.multiBuyOffers[0]?.promotionId ?? null)
  }
}

/** 多件价换促销：组合价取该促销的 fixedPrice。 */
export function applyMultiBuyOffer(
  draft: PromoPosterDraft,
  defaults: PromoPosterDefaults,
  offerId: string | null,
): PromoPosterDraft {
  const offer = defaults.multiBuyOffers.find((item) => item.promotionId === offerId)
  return offer
    ? { ...draft, offerId: offer.promotionId, price: offer.fixedPrice }
    : { ...draft, offerId: null, price: null }
}

export function createPosterDraft(defaults: PromoPosterDefaults): PromoPosterDraft {
  const empty: PromoPosterDraft = { kind: 'new', title: defaults.posterTitle, price: null, wasPrice: null, offerId: null }
  return applyPosterKind(empty, defaults, pickDefaultPosterKind(defaults))
}

export function findPosterOffer(draft: Pick<PromoPosterDraft, 'offerId'>, defaults: PromoPosterDefaults) {
  return defaults.multiBuyOffers.find((offer) => offer.promotionId === draft.offerId) ?? null
}

// ---------------------------------------------------------------- 品名校验

/** 字体能印的额外排版标点：– — ‘ ’ “ ” × • …（× 本身在 Latin-1 范围内，列出便于对照需求）。 */
const EXTRA_PRINTABLE_CHARS = new Set(['–', '—', '‘', '’', '“', '”', '×', '•', '…'])

/**
 * 海报字体只覆盖拉丁字符：U+0020–U+024F（排除 U+007F–U+009F 控制字符）加少量常见标点。
 * 中文、emoji 等都印不出来（后端会整单拒绝），必须在提交前拦下并标红。
 */
export function isPrintablePosterChar(char: string) {
  if (EXTRA_PRINTABLE_CHARS.has(char)) return true
  const code = char.codePointAt(0) ?? 0
  if (code >= 0x20 && code <= 0x7e) return true
  return code >= 0xa0 && code <= 0x24f
}

/** 与后端 NormalizeTitle 一致：合并连续空白并去掉首尾空白。 */
export function normalizePosterTitle(title: string) {
  return title.replace(/\s+/g, ' ').trim()
}

/** 返回去重后的不可打印字符（按出现顺序，最多 8 个，避免整段中文塞满提示）。 */
export function findUnprintablePosterChars(title: string) {
  const invalid: string[] = []
  for (const char of Array.from(normalizePosterTitle(title))) {
    if (!isPrintablePosterChar(char) && !invalid.includes(char)) invalid.push(char)
  }
  return invalid.slice(0, 8)
}

// ---------------------------------------------------------------- 行级校验

export type PromoPosterIssue =
  | { code: 'kindUnavailable' }
  | { code: 'titleRequired' }
  | { code: 'titleUnprintable'; chars: string }
  | { code: 'titleTooLong'; max: number }
  | { code: 'priceRequired' }
  | { code: 'priceInvalid'; max: number }
  | { code: 'wasPriceRequired' }
  | { code: 'wasPriceInvalid'; max: number }
  | { code: 'wasPriceNotHigher' }
  | { code: 'offerRequired' }

function isValidPosterPrice(value: number) {
  return Number.isFinite(value) && value > 0 && roundMoney(value) <= PROMO_POSTER_PRICE_MAX
}

/** 行级校验：返回全部问题（空数组表示可生成）。字段口径与后端 PromoPosterRequestParser 对齐，并略严于后端。 */
export function validatePosterDraft(draft: PromoPosterDraft, defaults: PromoPosterDefaults): PromoPosterIssue[] {
  const issues: PromoPosterIssue[] = []
  if (!resolvePosterKindAvailability(defaults)[draft.kind]) issues.push({ code: 'kindUnavailable' })

  const title = normalizePosterTitle(draft.title)
  const unprintable = findUnprintablePosterChars(title)
  if (!title) issues.push({ code: 'titleRequired' })
  else if (unprintable.length > 0) issues.push({ code: 'titleUnprintable', chars: unprintable.join('') })
  else if (title.length > PROMO_POSTER_TITLE_MAX_LENGTH) issues.push({ code: 'titleTooLong', max: PROMO_POSTER_TITLE_MAX_LENGTH })

  const priceOk = draft.price !== null && isValidPosterPrice(draft.price)
  if (draft.price === null) issues.push({ code: 'priceRequired' })
  else if (!priceOk) issues.push({ code: 'priceInvalid', max: PROMO_POSTER_PRICE_MAX })

  if (draft.kind === 'special' || draft.kind === 'clearance') {
    // Special 可只印海报价；Clearance 仍必须有高于海报价的原价。
    if (draft.wasPrice === null && draft.kind === 'clearance') issues.push({ code: 'wasPriceRequired' })
    else if (draft.wasPrice !== null && !isValidPosterPrice(draft.wasPrice)) issues.push({ code: 'wasPriceInvalid', max: PROMO_POSTER_PRICE_MAX })
    else if (draft.wasPrice !== null && priceOk && roundMoney(draft.wasPrice) <= roundMoney(draft.price!)) issues.push({ code: 'wasPriceNotHigher' })
  }

  if (draft.kind === 'multibuy' && !findPosterOffer(draft, defaults)) issues.push({ code: 'offerRequired' })
  return issues
}

/**
 * 海报价与系统价不一致提示（不阻止生成）：特价现价 ≠ 本店折后价、清仓价 ≠ 已设清仓价。
 * 顾客按海报价期待结账，收银却按系统价，必须提醒。
 */
export function resolvePosterPriceMismatch(
  draft: Pick<PromoPosterDraft, 'kind' | 'price'>,
  defaults: Pick<PromoPosterDefaults, 'discountedPrice' | 'clearancePrice'>,
): { kind: 'special' | 'clearance'; expected: number } | null {
  if (draft.price === null || !Number.isFinite(draft.price)) return null
  if (draft.kind === 'special' && defaults.discountedPrice !== null && !sameMoney(draft.price, defaults.discountedPrice)) {
    return { kind: 'special', expected: defaults.discountedPrice }
  }
  if (draft.kind === 'clearance' && defaults.clearancePrice !== null && !sameMoney(draft.price, defaults.clearancePrice)) {
    return { kind: 'clearance', expected: defaults.clearancePrice }
  }
  return null
}

// ---------------------------------------------------------------- 行状态汇总

export type PromoPosterRowState =
  | { key: string; product: PromoPosterProduct; status: 'loading' }
  /** errorStatus 为 HTTP 状态码（网络异常为 null），页面据此给出 403 / 401 等友好提示。 */
  | { key: string; product: PromoPosterProduct; status: 'error'; errorMessage: string; errorStatus: number | null }
  | { key: string; product: PromoPosterProduct; status: 'ready'; defaults: PromoPosterDefaults; draft: PromoPosterDraft }

export function createLoadingPosterRow(product: PromoPosterProduct): PromoPosterRowState {
  return { key: product.productCode, product, status: 'loading' }
}

/** 选中商品去重（按商品编码）并截断到 200 个；返回被截掉的数量供提示。 */
export function preparePosterProducts(products: readonly PromoPosterProduct[]) {
  const seen = new Set<string>()
  const unique: PromoPosterProduct[] = []
  for (const product of products) {
    const code = product.productCode.trim()
    if (!code || seen.has(code)) continue
    seen.add(code)
    unique.push({ ...product, productCode: code })
  }
  return {
    products: unique.slice(0, PROMO_POSTER_MAX_COUNT),
    truncatedCount: Math.max(0, unique.length - PROMO_POSTER_MAX_COUNT),
  }
}

export interface PromoPosterBlockers {
  total: number
  loading: number
  failed: number
  invalid: number
}

/** 统计阻止「生成 PDF」的原因：有行在加载、读取失败、校验不通过或一行都没有。 */
export function summarizePosterBlockers(rows: readonly PromoPosterRowState[]): PromoPosterBlockers {
  const summary: PromoPosterBlockers = { total: rows.length, loading: 0, failed: 0, invalid: 0 }
  for (const row of rows) {
    if (row.status === 'loading') summary.loading += 1
    else if (row.status === 'error') summary.failed += 1
    else if (validatePosterDraft(row.draft, row.defaults).length > 0) summary.invalid += 1
  }
  return summary
}

export function canGeneratePosters(blockers: PromoPosterBlockers) {
  return blockers.total > 0 && blockers.loading === 0 && blockers.failed === 0 && blockers.invalid === 0
}

// ---------------------------------------------------------------- 页数与请求体

/** 预计页数：拼版时按每页容量向上取整（A4 每页 1 张）；不拼版每张一页。批量弹窗内所有海报同一尺寸。 */
export function countPosterPages(count: number, size: PromoPosterSize, impose: boolean) {
  if (count <= 0) return 0
  return impose ? Math.ceil(count / PROMO_POSTER_PER_A4[size]) : count
}

function stripUndefined<T extends object>(value: T): T {
  const result: Record<string, unknown> = {}
  for (const [key, item] of Object.entries(value)) {
    if (item !== undefined) result[key] = item
  }
  return result as T
}

/**
 * 草稿 → 单张海报请求（调用前需已通过 validatePosterDraft）：
 * - special: price=现价、wasPrice=原价（validFrom/To 可选，批量场景不填）
 * - clearance: price=清仓价、wasPrice=原价
 * - new: price=售价、inStoreSince=今天
 * - multibuy: price=组合价、quantity=件数、unitPrice=零售价、mixAndMatch=促销含多个商品、validFrom/To=促销起止日期
 */
export function buildPosterItemRequest(
  draft: PromoPosterDraft,
  defaults: PromoPosterDefaults,
  options: { style: PromoPosterStyle; size: PromoPosterSize; today: string; itemNumber?: string },
): PromoPosterItemRequest {
  const itemNumber = (defaults.itemNumber || options.itemNumber || '').trim()
  const base: PromoPosterItemRequest = {
    kind: draft.kind,
    style: options.style,
    size: options.size,
    productCode: defaults.productCode,
    itemNumber: itemNumber || undefined,
    title: normalizePosterTitle(draft.title),
    price: roundMoney(draft.price ?? 0),
  }
  switch (draft.kind) {
    case 'special':
    case 'clearance':
      return stripUndefined({ ...base, wasPrice: draft.wasPrice === null ? undefined : roundMoney(draft.wasPrice) })
    case 'new':
      return stripUndefined({ ...base, inStoreSince: options.today })
    case 'multibuy': {
      const offer = findPosterOffer(draft, defaults)
      return stripUndefined({
        ...base,
        quantity: offer?.applyQuantity,
        unitPrice: defaults.retailPrice ?? undefined,
        mixAndMatch: (offer?.productsCount ?? 0) > 1,
        validFrom: toDateOnly(offer?.effectiveStart),
        validTo: toDateOnly(offer?.effectiveEnd),
      })
    }
  }
}

/** 汇总所有就绪行生成 PDF 请求体；非 ready 行（理论上已被 canGeneratePosters 拦下）直接跳过。 */
export function buildPromoPosterPdfRequest(
  storeCode: string,
  rows: readonly PromoPosterRowState[],
  options: { style: PromoPosterStyle; size: PromoPosterSize; impose: boolean; today: string; showLogo?: boolean },
): PromoPosterPdfRequest {
  const posters: PromoPosterItemRequest[] = []
  for (const row of rows) {
    if (row.status !== 'ready') continue
    posters.push(buildPosterItemRequest(row.draft, row.defaults, { ...options, itemNumber: row.product.itemNumber }))
  }
  return { storeCode, impose: options.impose, showLogo: options.showLogo ?? true, posters }
}

// ---------------------------------------------------------------- 响应解析

/** 解析 Content-Disposition 文件名：优先 filename*（UTF-8 编码），再取 filename；解析不到返回 null。 */
export function parsePosterPdfFileName(contentDisposition: string | null | undefined) {
  if (!contentDisposition) return null
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition)?.[1]
  if (encoded) {
    try {
      return decodeURIComponent(encoded.trim()).replace(/[\\/]/g, '_')
    } catch {
      // 编码异常时回退到普通 filename
    }
  }
  const plain = /filename="?([^";]+)"?/i.exec(contentDisposition)?.[1]?.trim()
  return plain ? plain.replace(/[\\/]/g, '_') : null
}

/** 响应头 X-Poster-Page-Count；跨域未暴露或格式异常时返回 null，由调用方回退到本地估算。 */
export function parsePosterPageCount(value: string | null | undefined) {
  const parsed = Number((value ?? '').trim())
  return value && Number.isInteger(parsed) && parsed > 0 ? parsed : null
}

/** 本地兜底文件名：HB-Posters-20260919-1530.pdf（与后端命名一致）。 */
export function buildPosterPdfFallbackFileName(now: Date) {
  const date = `${now.getFullYear()}${pad2(now.getMonth() + 1)}${pad2(now.getDate())}`
  return `HB-Posters-${date}-${pad2(now.getHours())}${pad2(now.getMinutes())}.pdf`
}

// ---------------------------------------------------------------- 并发控制

/**
 * 以固定并发数依次处理任务（滑动窗口，不是整批等待）：任一任务完成立刻补下一个。
 * worker 需自行捕获异常；signal 取消后不再启动新任务，已发出的由 worker 自己按 signal 处理。
 */
export async function runWithConcurrency<T>(
  items: readonly T[],
  limit: number,
  worker: (item: T, index: number) => Promise<void>,
  signal?: AbortSignal,
) {
  let next = 0
  const laneCount = Math.max(1, Math.min(Math.trunc(limit) || 1, items.length))
  const lanes = Array.from({ length: laneCount }, async () => {
    while (next < items.length) {
      if (signal?.aborted) return
      const index = next
      next += 1
      await worker(items[index], index)
    }
  })
  await Promise.all(lanes)
}
