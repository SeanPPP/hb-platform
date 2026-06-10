import type { ContainerDetail, ContainerDetailQuery, ContainerMain, UpdateContainerDetailRequest } from '../../../types/container'
import type { PushProductsToHqItem, PushProductsToHqResult } from '../../../types/posProduct'

export type ContainerDetailTagFilter = 'all' | 'new' | 'existing' | 'noOemPrice' | 'abnormalImport' | 'active' | 'inactive'

export type ContainerDetailTagStats = Record<ContainerDetailTagFilter, number>
type ContainerDetailSelectableTagFilter = Exclude<ContainerDetailTagFilter, 'all'>
export type ContainerDetailProductTypeFilter = 'normal' | 'set' | 'setChild'
export type ContainerDetailNewProductFilter = 'new' | 'existing'
export type ContainerDetailMatchTypeFilter = 'productCode' | 'supplierItem' | 'unmatched'
export type ContainerDetailWarehouseStatusFilter = 'active' | 'inactive'
export type ContainerDetailSortOrder = 'ascend' | 'descend'
export type ContainerDetailSortField =
  | 'itemNumber'
  | 'barcode'
  | 'productName'
  | 'englishName'
  | 'productType'
  | 'newProduct'
  | 'matchType'
  | 'containerPieces'
  | 'containerQuantity'
  | 'domesticPrice'
  | 'floatRate'
  | 'transportCost'
  | 'warehouseImportPrice'
  | 'importPrice'
  | 'oemPrice'
  | 'warehouseStatus'
  | 'remark'

const containerDetailSortFields = new Set<string>([
  'itemNumber',
  'barcode',
  'productName',
  'englishName',
  'productType',
  'newProduct',
  'matchType',
  'containerPieces',
  'containerQuantity',
  'domesticPrice',
  'floatRate',
  'transportCost',
  'warehouseImportPrice',
  'importPrice',
  'oemPrice',
  'warehouseStatus',
  'remark',
])

const chineseTextPattern = /[\u4e00-\u9fff]/

export function containsChineseText(value?: string) {
  return Boolean(value && chineseTextPattern.test(value))
}

export function isValidContainerDetailEnglishTranslation(value?: string) {
  return Boolean(value?.trim()) && !containsChineseText(value)
}

export function isContainerDetailSortField(value: unknown): value is ContainerDetailSortField {
  return typeof value === 'string' && containerDetailSortFields.has(value)
}

export interface ContainerDetailNumberRangeFilter {
  min?: number
  max?: number
}

export interface ContainerDetailColumnFilters {
  itemNumber?: string
  barcode?: string
  productName?: string
  englishName?: string
  productTypes?: ContainerDetailProductTypeFilter[]
  newProductStates?: ContainerDetailNewProductFilter[]
  matchTypes?: ContainerDetailMatchTypeFilter[]
  containerPieces?: ContainerDetailNumberRangeFilter
  containerQuantity?: ContainerDetailNumberRangeFilter
  domesticPrice?: ContainerDetailNumberRangeFilter
  floatRate?: ContainerDetailNumberRangeFilter
  transportCost?: ContainerDetailNumberRangeFilter
  warehouseImportPrice?: ContainerDetailNumberRangeFilter
  importPrice?: ContainerDetailNumberRangeFilter
  oemPrice?: ContainerDetailNumberRangeFilter
  warehouseStatus?: ContainerDetailWarehouseStatusFilter[]
  remark?: string
}

export interface ContainerDetailSortState {
  field: ContainerDetailSortField
  order: ContainerDetailSortOrder
}

export function getContainerDetailProductName(row: ContainerDetail) {
  return row.商品名称 ?? row.商品信息?.商品名称
}

export function getContainerDetailCreateProductRowLabel(row: ContainerDetail) {
  return getContainerDetailItemNumber(row) ?? getContainerDetailProductCode(row) ?? row.hguid
}

export function findContainerDetailRowsMissingChineseName(rows: ContainerDetail[]) {
  return rows
    .filter((row) => row.是否新商品)
    .map((row) => {
      const productName = getContainerDetailProductName(row)?.trim() ?? ''
      return {
        hguid: row.hguid,
        label: getContainerDetailCreateProductRowLabel(row),
        productName,
        hasChineseName: containsChineseText(productName),
      }
    })
    // 创建仓库新商品依赖中文商品名，避免把英文名误当作可创建的中文名。
    .filter((row) => !row.hasChineseName)
    .map(({ hguid, label, productName }) => ({ hguid, label, productName }))
}

export function getContainerDetailEnglishName(row: ContainerDetail) {
  return row.英文名称 ?? row.商品信息?.英文名称
}

export function getContainerDetailTranslationSource(row: ContainerDetail) {
  const englishName = getContainerDetailEnglishName(row)
  if (containsChineseText(englishName)) return englishName
  return getContainerDetailProductName(row)
}

export function getContainerDetailItemNumber(row: ContainerDetail) {
  return row.商品信息?.货号?.trim() || undefined
}

export function getContainerDetailBarcode(row: ContainerDetail) {
  return row.商品信息?.条形码?.trim() || undefined
}

export function getContainerDetailProductCode(row: ContainerDetail) {
  return row.商品编码?.trim() || row.商品信息?.商品编码?.trim() || undefined
}

export function getContainerDetailMatchType(row: ContainerDetail): ContainerDetailMatchTypeFilter {
  const raw = row.matchType ?? row.MatchType
  const normalized = raw?.trim().toLowerCase()
  if (normalized === 'productcode' || normalized === 'product_code' || normalized === '商品编码') {
    return 'productCode'
  }
  if (
    normalized === 'supplieritem' ||
    normalized === 'supplier_item' ||
    normalized === 'item_number' ||
    normalized === 'itemnumber' ||
    normalized === '供应商编码+货号' ||
    normalized === '货号匹配'
  ) return 'supplierItem'
  if (normalized === 'unmatched' || normalized === '未匹配') return 'unmatched'
  return 'unmatched'
}

export function getContainerDetailProductTypeFilterKey(row: ContainerDetail): ContainerDetailProductTypeFilter {
  const type = row.商品类型 || row.商品信息?.商品类型 || '普通商品'
  if (type === '套装商品') return 'set'
  if (type === '套装子商品') return 'setChild'
  return 'normal'
}

export function getContainerDetailWarehouseStatusFilterKey(row: ContainerDetail): ContainerDetailWarehouseStatusFilter {
  return row.warehouseIsActive === true ? 'active' : 'inactive'
}

export function withContainerDetailEnglishName(row: ContainerDetail, englishName?: string): ContainerDetail {
  return {
    ...row,
    英文名称: englishName,
    商品信息: row.商品信息 ? { ...row.商品信息, 英文名称: englishName } : row.商品信息,
  }
}

export function mergeContainerDetailPatch(row: ContainerDetail, patch: Partial<ContainerDetail>): ContainerDetail {
  const next = { ...row, ...patch }
  const productInfoPatch: Partial<NonNullable<ContainerDetail['商品信息']>> = {}

  if ('英文名称' in patch) {
    productInfoPatch.英文名称 = patch.英文名称
  }
  if ('商品名称' in patch) {
    productInfoPatch.商品名称 = patch.商品名称
  }
  if ('单件装箱数' in patch) {
    productInfoPatch.单件装箱数 = patch.单件装箱数
  }
  if ('单件体积' in patch) {
    productInfoPatch.单件体积 = patch.单件体积
  }

  if (Object.keys(productInfoPatch).length > 0 && next.商品信息) {
    return { ...next, 商品信息: { ...next.商品信息, ...productInfoPatch } }
  }

  return next
}

export function buildContainerDetailSaveFailureKeys(rowKey: string, patch: object) {
  const fields = Object.keys(patch).filter((key) => key !== 'hguid').sort()
  if (!fields.length) {
    return [`${rowKey}:__row__`]
  }
  return fields.map((field) => `${rowKey}:${field}`)
}

export function matchesContainerDetailTagFilter(row: ContainerDetail, filter: ContainerDetailTagFilter) {
  if (filter === 'new') return Boolean(row.是否新商品)
  if (filter === 'existing') return !row.是否新商品
  if (filter === 'noOemPrice') return Boolean(row.是否新商品) && (!row.贴牌价格 || row.贴牌价格 <= 0)
  if (filter === 'abnormalImport') return !row.进口价格 || row.进口价格 <= 0
  if (filter === 'active') return row.warehouseIsActive === true
  if (filter === 'inactive') return row.warehouseIsActive !== true
  return true
}

const containerDetailTagFilterGroups: ContainerDetailSelectableTagFilter[][] = [
  ['new', 'existing'],
  ['noOemPrice', 'abnormalImport'],
  ['active', 'inactive'],
]

export function matchesContainerDetailSelectedTags(row: ContainerDetail, selectedTags: ContainerDetailTagFilter[]) {
  const selected = selectedTags.filter((tag): tag is ContainerDetailSelectableTagFilter => tag !== 'all')
  if (!selected.length) return true

  return containerDetailTagFilterGroups.every((group) => {
    const selectedInGroup = group.filter((tag) => selected.includes(tag))
    if (!selectedInGroup.length) return true
    // 同一类标签取并集，不同类标签再取交集，避免“新商品 + 已有商品”互相抵消。
    return selectedInGroup.some((tag) => matchesContainerDetailTagFilter(row, tag))
  })
}

export function buildContainerDetailTagStats(rows: ContainerDetail[]): ContainerDetailTagStats {
  const stats: ContainerDetailTagStats = {
    all: rows.length,
    new: 0,
    existing: 0,
    noOemPrice: 0,
    abnormalImport: 0,
    active: 0,
    inactive: 0,
  }

  rows.forEach((row) => {
    // 统计栏和标签过滤共用同一判断，避免数量与点击后的列表不一致。
    if (matchesContainerDetailTagFilter(row, 'new')) stats.new += 1
    if (matchesContainerDetailTagFilter(row, 'existing')) stats.existing += 1
    if (matchesContainerDetailTagFilter(row, 'noOemPrice')) stats.noOemPrice += 1
    if (matchesContainerDetailTagFilter(row, 'abnormalImport')) stats.abnormalImport += 1
    if (matchesContainerDetailTagFilter(row, 'active')) stats.active += 1
    if (matchesContainerDetailTagFilter(row, 'inactive')) stats.inactive += 1
  })

  return stats
}

function normalizeText(value?: string) {
  return (value ?? '').trim().toLowerCase()
}

function matchesTextFilter(value: string | undefined, filter: string | undefined) {
  const normalizedFilter = normalizeText(filter)
  if (!normalizedFilter) return true
  return normalizeText(value).includes(normalizedFilter)
}

function isEmptyNumberRange(filter: ContainerDetailNumberRangeFilter | undefined) {
  return filter?.min == null && filter?.max == null
}

function matchesNumberRange(value: number | undefined, filter: ContainerDetailNumberRangeFilter | undefined) {
  if (isEmptyNumberRange(filter)) return true
  if (value == null) return false
  if (filter?.min != null && value < filter.min) return false
  if (filter?.max != null && value > filter.max) return false
  return true
}

function matchesOneOf<T extends string>(value: T, selected: T[] | undefined) {
  return !selected?.length || selected.includes(value)
}

function getColumnSortValue(row: ContainerDetail, field: ContainerDetailSortField): string | number | undefined {
  switch (field) {
    case 'itemNumber':
      return getContainerDetailItemNumber(row)
    case 'barcode':
      return getContainerDetailBarcode(row)
    case 'productName':
      return getContainerDetailProductName(row)
    case 'englishName':
      return getContainerDetailEnglishName(row)
    case 'productType':
      return getContainerDetailProductTypeFilterKey(row)
    case 'newProduct':
      return row.是否新商品 ? 1 : 0
    case 'matchType':
      return getContainerDetailMatchType(row)
    case 'containerPieces':
      return row.装柜件数
    case 'containerQuantity':
      return row.装柜数量
    case 'domesticPrice':
      return row.国内价格
    case 'floatRate':
      return row.调整浮率
    case 'transportCost':
      return row.运输成本
    case 'warehouseImportPrice':
      return row.warehouseImportPrice
    case 'importPrice':
      return row.进口价格
    case 'oemPrice':
      return row.贴牌价格
    case 'warehouseStatus':
      return row.warehouseIsActive === true ? 1 : 0
    case 'remark':
      return row.备注
    default:
      return undefined
  }
}

function compareColumnValues(a: string | number | undefined, b: string | number | undefined) {
  const aEmpty = a == null || (typeof a === 'string' && !a.trim())
  const bEmpty = b == null || (typeof b === 'string' && !b.trim())
  if (aEmpty && bEmpty) return 0
  if (aEmpty) return 1
  if (bEmpty) return -1
  if (typeof a === 'number' && typeof b === 'number') return a - b
  return String(a).localeCompare(String(b), 'zh-CN', { numeric: true, sensitivity: 'base' })
}

export function applyContainerDetailColumnState(
  rows: ContainerDetail[],
  filters: ContainerDetailColumnFilters,
  sortState?: ContainerDetailSortState,
) {
  const filtered = rows.filter((row) => (
    matchesTextFilter(getContainerDetailItemNumber(row), filters.itemNumber) &&
    matchesTextFilter(getContainerDetailBarcode(row), filters.barcode) &&
    matchesTextFilter(getContainerDetailProductName(row), filters.productName) &&
    matchesTextFilter(getContainerDetailEnglishName(row), filters.englishName) &&
    matchesTextFilter(row.备注, filters.remark) &&
    matchesOneOf(getContainerDetailProductTypeFilterKey(row), filters.productTypes) &&
    matchesOneOf(row.是否新商品 ? 'new' : 'existing', filters.newProductStates) &&
    matchesOneOf(getContainerDetailMatchType(row), filters.matchTypes) &&
    matchesOneOf(getContainerDetailWarehouseStatusFilterKey(row), filters.warehouseStatus) &&
    matchesNumberRange(row.装柜件数, filters.containerPieces) &&
    matchesNumberRange(row.装柜数量, filters.containerQuantity) &&
    matchesNumberRange(row.国内价格, filters.domesticPrice) &&
    matchesNumberRange(row.调整浮率, filters.floatRate) &&
    matchesNumberRange(row.运输成本, filters.transportCost) &&
    matchesNumberRange(row.warehouseImportPrice, filters.warehouseImportPrice) &&
    matchesNumberRange(row.进口价格, filters.importPrice) &&
    matchesNumberRange(row.贴牌价格, filters.oemPrice)
  ))

  if (!sortState) return filtered

  return filtered
    .map((row, index) => ({ row, index }))
    .sort((left, right) => {
      const result = compareColumnValues(
        getColumnSortValue(left.row, sortState.field),
        getColumnSortValue(right.row, sortState.field),
      )
      if (result === 0) return left.index - right.index
      return sortState.order === 'ascend' ? result : -result
    })
    .map((item) => item.row)
}

export interface BuildContainerDetailQueryOptions {
  containerGuid: string
  filters: ContainerDetailColumnFilters
  selectedTags: ContainerDetailTagFilter[]
  sortState?: ContainerDetailSortState
  pageNumber: number
  pageSize: number
}

function assignQueryValue<K extends keyof ContainerDetailQuery>(
  target: ContainerDetailQuery,
  key: K,
  value: ContainerDetailQuery[K],
) {
  target[key] = value
}

function assignTrimmedText<K extends keyof ContainerDetailQuery>(
  target: ContainerDetailQuery,
  key: K,
  value?: string,
) {
  const normalized = value?.trim()
  if (normalized) {
    assignQueryValue(target, key, normalized as ContainerDetailQuery[K])
  }
}

function assignNonEmptyArray<K extends keyof ContainerDetailQuery, V>(
  target: ContainerDetailQuery,
  key: K,
  value?: V[],
) {
  if (value?.length) {
    assignQueryValue(target, key, [...value] as unknown as ContainerDetailQuery[K])
  }
}

function assignNumberRange(
  target: ContainerDetailQuery,
  minKey: keyof ContainerDetailQuery,
  maxKey: keyof ContainerDetailQuery,
  range?: ContainerDetailNumberRangeFilter,
) {
  // 0 是有效筛选值，不能用 truthy 判断丢掉。
  if (range?.min != null) {
    assignQueryValue(target, minKey, range.min as ContainerDetailQuery[typeof minKey])
  }
  if (range?.max != null) {
    assignQueryValue(target, maxKey, range.max as ContainerDetailQuery[typeof maxKey])
  }
}

export function buildContainerDetailQuery({
  containerGuid,
  filters,
  selectedTags,
  sortState,
  pageNumber,
  pageSize,
}: BuildContainerDetailQueryOptions): ContainerDetailQuery {
  const query: ContainerDetailQuery = {
    containerGuid,
    pageNumber,
    pageSize,
  }

  assignTrimmedText(query, 'itemNumber', filters.itemNumber)
  assignTrimmedText(query, 'barcode', filters.barcode)
  assignTrimmedText(query, 'productName', filters.productName)
  assignTrimmedText(query, 'englishName', filters.englishName)
  assignTrimmedText(query, 'remark', filters.remark)
  assignNonEmptyArray(query, 'productTypes', filters.productTypes)
  assignNonEmptyArray(query, 'newProductStates', filters.newProductStates)
  assignNonEmptyArray(query, 'matchTypes', filters.matchTypes)
  assignNonEmptyArray(query, 'warehouseStatus', filters.warehouseStatus)

  assignNumberRange(query, 'containerPiecesMin', 'containerPiecesMax', filters.containerPieces)
  assignNumberRange(query, 'containerQuantityMin', 'containerQuantityMax', filters.containerQuantity)
  assignNumberRange(query, 'domesticPriceMin', 'domesticPriceMax', filters.domesticPrice)
  assignNumberRange(query, 'floatRateMin', 'floatRateMax', filters.floatRate)
  assignNumberRange(query, 'transportCostMin', 'transportCostMax', filters.transportCost)
  assignNumberRange(query, 'warehouseImportPriceMin', 'warehouseImportPriceMax', filters.warehouseImportPrice)
  assignNumberRange(query, 'importPriceMin', 'importPriceMax', filters.importPrice)
  assignNumberRange(query, 'oemPriceMin', 'oemPriceMax', filters.oemPrice)

  const remoteTags = selectedTags.filter((tag) => tag !== 'all')
  assignNonEmptyArray(query, 'selectedTags', remoteTags)

  if (sortState) {
    query.sortBy = sortState.field
    query.sortOrder = sortState.order
  }

  return query
}

export function mergeContainerDetailLoadedItems(
  loadedItems: ContainerDetail[],
  nextItems: ContainerDetail[],
): ContainerDetail[] {
  const merged = [...loadedItems]
  const indexByGuid = new Map<string, number>()

  merged.forEach((item, index) => {
    if (item.hguid) {
      indexByGuid.set(item.hguid, index)
    }
  })

  nextItems.forEach((item) => {
    const existingIndex = item.hguid ? indexByGuid.get(item.hguid) : undefined
    if (existingIndex == null) {
      if (item.hguid) {
        indexByGuid.set(item.hguid, merged.length)
      }
      merged.push(item)
      return
    }

    // 重复明细保留原位置，但以后端最新页数据覆盖，避免编辑后刷新显示旧值。
    merged[existingIndex] = item
  })

  return merged
}

export interface ContainerDetailRemoteQueryResetState<Key = string> {
  selectedRowKeys: Key[]
  loadedItems: ContainerDetail[]
  pageNumber: number
}

export function getContainerDetailRemoteQueryResetState<Key = string>(
  _state?: Partial<ContainerDetailRemoteQueryResetState<Key>>,
): ContainerDetailRemoteQueryResetState<Key> {
  // 远程查询条件变化后，旧选择和旧分页块都不再代表当前结果集。
  return {
    selectedRowKeys: [],
    loadedItems: [],
    pageNumber: 1,
  }
}

export function applyContainerDetailWarehouseStatusByProductCodes(
  rows: ContainerDetail[],
  productCodes: string[],
  isActive: boolean,
) {
  const productCodeSet = new Set(productCodes.map((value) => value.trim()).filter(Boolean))

  return rows.map((row) => {
    const productCode = getContainerDetailProductCode(row)
    return productCode && productCodeSet.has(productCode)
      ? { ...row, warehouseIsActive: isActive }
      : row
  })
}

export function rollbackContainerDetailWarehouseStatuses(
  rows: ContainerDetail[],
  previousStatuses: Array<{ key: string; warehouseIsActive?: boolean }>,
  getRowKey: (row: ContainerDetail) => string,
) {
  const previousStatusMap = new Map(previousStatuses.map((item) => [item.key, item.warehouseIsActive]))

  return rows.map((row) => {
    const key = getRowKey(row)
    return previousStatusMap.has(key)
      ? { ...row, warehouseIsActive: previousStatusMap.get(key) }
      : row
  })
}

export interface ContainerDetailWarehouseActionResultLike {
  success?: boolean
  isSuccess?: boolean
  failedCount?: number
  FailedCount?: number
  errors?: string[]
  Errors?: string[]
  message?: string
  Message?: string
}

export function getContainerDetailWarehouseActionFailureMessage(
  result: ContainerDetailWarehouseActionResultLike,
  fallback: string,
) {
  const failedCount = Number(result.failedCount ?? result.FailedCount ?? 0)
  const errors = result.errors ?? result.Errors ?? []
  if (result.success === false || result.isSuccess === false || failedCount > 0) {
    return result.message ?? result.Message ?? errors.join('；') ?? fallback
  }
  return undefined
}

export function buildContainerDetailTranslationUpdates(
  rows: ContainerDetail[],
  translations: Record<string, string>,
): UpdateContainerDetailRequest[] {
  const updates: UpdateContainerDetailRequest[] = []

  rows.forEach((row) => {
    const name = getContainerDetailTranslationSource(row)
    const englishName = name ? translations[name] : undefined

    if (row.hguid && isValidContainerDetailEnglishTranslation(englishName)) {
      updates.push({ hguid: row.hguid, 英文名称: englishName!.trim() })
    }
  })

  return updates
}

export function countContainerDetailInvalidTranslationResults(
  rows: ContainerDetail[],
  translations: Record<string, string>,
) {
  return rows.filter((row) => {
    const name = getContainerDetailTranslationSource(row)
    const englishName = name ? translations[name] : undefined
    return Boolean(englishName) && !isValidContainerDetailEnglishTranslation(englishName)
  }).length
}

export function buildContainerDetailEnglishNameUpdates(
  rows: ContainerDetail[],
  englishName: string,
): UpdateContainerDetailRequest[] {
  const normalizedEnglishName = englishName.trim()
  if (!isValidContainerDetailEnglishTranslation(normalizedEnglishName)) return []

  return rows
    .filter((row) => Boolean(row.hguid))
    .map((row) => ({ hguid: row.hguid, 英文名称: normalizedEnglishName }))
}

export function buildContainerDetailClearEnglishNameUpdates(
  rows: ContainerDetail[],
): UpdateContainerDetailRequest[] {
  return rows
    .filter((row) => Boolean(row.hguid))
    .map((row) => ({ hguid: row.hguid, ClearEnglishName: true }))
}

export function applyContainerDetailEnglishNameUpdates(
  rows: ContainerDetail[],
  updates: Pick<UpdateContainerDetailRequest, 'hguid' | '英文名称'>[],
): ContainerDetail[] {
  const updateMap = new Map(updates.map((item) => [item.hguid, item.英文名称]))

  return rows.map((row) => (
    updateMap.has(row.hguid)
      ? withContainerDetailEnglishName(row, updateMap.get(row.hguid))
      : row
  ))
}

function roundToDigits(value: number, digits: number) {
  const base = 10 ** digits
  return Math.round((value + Number.EPSILON) * base) / base
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

export function normalizeContainerDetailPushToHqPayload(raw: unknown, fallbackMessage?: string): PushProductsToHqResult | null {
  if (!isPlainRecord(raw)) return null

  const errors = Array.isArray(raw.errors)
    ? raw.errors.map(String)
    : []
  const successCount = Number(raw.successCount ?? raw.productsAdded ?? 0) +
    Number(raw.successCount === undefined ? raw.productsUpdated ?? 0 : 0)
  const failedCount = Number(raw.failedCount ?? raw.errorCount ?? errors.length)
  const affectedRowCount =
    Number(raw.affectedRowCount ?? 0) ||
    Number(raw.productsAdded ?? 0) +
      Number(raw.productsUpdated ?? 0) +
      Number(raw.warehouseInventoriesCreated ?? 0) +
      Number(raw.warehouseInventoriesUpdated ?? 0) +
      Number(raw.storeRetailPricesCreated ?? 0) +
      Number(raw.storeRetailPricesUpdated ?? 0) +
      Number(raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0) +
      Number(raw.productSetCodesUpdated ?? 0) +
      Number(raw.storeMultiCodesCreated ?? 0) +
      Number(raw.storeMultiCodesUpdated ?? 0)

  return {
    ...(raw as Partial<PushProductsToHqResult>),
    successCount,
    failedCount,
    totalCount: Number(raw.totalCount ?? successCount + failedCount),
    affectedRowCount,
    errors,
    message: typeof raw.message === 'string' ? raw.message : fallbackMessage,
  }
}

export function extractPushToHqErrorResult(error: unknown): PushProductsToHqResult | null {
  if (!isPlainRecord(error) || !('payload' in error)) return null
  const payload = error.payload
  if (!isPlainRecord(payload)) return null
  const fallbackMessage = typeof payload.message === 'string'
    ? payload.message
    : error instanceof Error
      ? error.message
      : undefined
  return (
    normalizeContainerDetailPushToHqPayload(payload.data, fallbackMessage) ??
    normalizeContainerDetailPushToHqPayload(payload.details, fallbackMessage) ??
    normalizeContainerDetailPushToHqPayload(payload, fallbackMessage)
  )
}

export function calculateContainerDetailTransportCost(row: ContainerDetail, container?: Pick<ContainerMain, '运费' | '总体积'> | null) {
  const freight = container?.运费
  const totalVolume = container?.总体积
  const containerQuantity = row.装柜数量
  const unitVolume = row.单件体积 ?? row.商品信息?.单件体积
  const detailVolume = row.合计装柜体积 ?? (
    row.装柜件数 != null && unitVolume != null
      ? row.装柜件数 * unitVolume
      : undefined
  )

  if (
    freight == null ||
    freight < 0 ||
    !totalVolume ||
    totalVolume <= 0 ||
    containerQuantity == null ||
    containerQuantity <= 0 ||
    detailVolume == null ||
    detailVolume < 0
  ) {
    return row.运输成本
  }

  return roundToDigits((freight * detailVolume) / containerQuantity / totalVolume, 2)
}

export function calculateContainerDetailImportPrice(
  row: ContainerDetail,
  container: Pick<ContainerMain, '汇率'> | null | undefined,
  floatRate: number,
  transportCost: number | undefined,
) {
  const exchangeRate = container?.汇率

  if (!exchangeRate || exchangeRate <= 0 || row.国内价格 == null) {
    return row.进口价格
  }

  return roundToDigits(((row.国内价格 / exchangeRate + (transportCost ?? 0)) * floatRate * 10) / 11, 2)
}

export function buildContainerDetailFloatRateUpdates(
  rows: ContainerDetail[],
  container: Pick<ContainerMain, '汇率' | '运费' | '总体积'> | null | undefined,
  floatRate?: number,
): UpdateContainerDetailRequest[] {
  return rows
    .filter((row) => row.hguid)
    .map((row): UpdateContainerDetailRequest | null => {
      const nextFloatRate = floatRate ?? row.调整浮率 ?? 1
      const transportCost = calculateContainerDetailTransportCost(row, container)
      const importPrice = calculateContainerDetailImportPrice(row, container, nextFloatRate, transportCost)
      const hasChange =
        row.调整浮率 !== nextFloatRate ||
        row.运输成本 !== transportCost ||
        row.进口价格 !== importPrice

      if (!hasChange) {
        return null
      }

      return {
        hguid: row.hguid,
        调整浮率: nextFloatRate,
        运输成本: transportCost,
        进口价格: importPrice,
      }
    })
    .filter((update): update is UpdateContainerDetailRequest => update !== null)
}

interface ContainerDetailDetectedPrice {
  ProductCode?: string
  productCode?: string
  ItemNumber?: string
  itemNumber?: string
  SupplierCode?: string
  supplierCode?: string
  Barcode?: string
  barcode?: string
  Exists?: boolean
  exists?: boolean
  MatchType?: string
  matchType?: string
  ProductName?: string
  productName?: string
  name?: string
  EnglishName?: string
  englishName?: string
  nameEn?: string
  DomesticPrice?: number
  domesticPrice?: number
  WarehouseDomesticPrice?: number
  warehouseDomesticPrice?: number
  OEMPrice?: number
  oemPrice?: number
  WarehouseOEMPrice?: number
  warehouseOEMPrice?: number
  DomesticOEMPrice?: number
  domesticOEMPrice?: number
  labelPrice?: number
  WarehouseVolume?: number
  warehouseVolume?: number
  PackingQuantity?: number
  packingQuantity?: number
  packingQty?: number
  UnitVolume?: number
  unitVolume?: number
  Volume?: number
  volume?: number
}

export interface ContainerDetailDetectionItem {
  ProductCode?: string
  ItemNumber?: string
  SupplierCode?: string
}

function isMissingPrice(value?: number) {
  return value == null || value <= 0
}

function normalizeMatchKey(value?: string) {
  return value?.trim().toUpperCase()
}

function getContainerDetailDetectionProductCode(row: ContainerDetail) {
  const productCode = getContainerDetailProductCode(row)
  return productCode
}

function buildSupplierItemMatchKey(supplierCode?: string, itemNumber?: string) {
  const normalizedSupplierCode = normalizeMatchKey(supplierCode)
  const normalizedItemNumber = normalizeMatchKey(itemNumber)
  return normalizedSupplierCode && normalizedItemNumber
    ? `${normalizedSupplierCode}:${normalizedItemNumber}`
    : undefined
}

export function buildContainerDetailDetectionItems(rows: ContainerDetail[]): ContainerDetailDetectionItem[] {
  return rows
    .map((row) => ({
      // 检测同时携带商品编码和固定供应商 200 + 货号，由匹配结果决定最终展示方式。
      ProductCode: getContainerDetailDetectionProductCode(row),
      ItemNumber: getContainerDetailItemNumber(row),
      SupplierCode: '200',
    }))
    .filter((item) => item.ProductCode || item.ItemNumber)
}

function getDetectedDomesticPrice(item: ContainerDetailDetectedPrice) {
  return item.DomesticPrice ?? item.domesticPrice ?? item.WarehouseDomesticPrice ?? item.warehouseDomesticPrice
}

function getDetectedOemPrice(item: ContainerDetailDetectedPrice) {
  return item.WarehouseOEMPrice ?? item.warehouseOEMPrice ?? item.DomesticOEMPrice ?? item.domesticOEMPrice ?? item.labelPrice ?? item.oemPrice ?? item.OEMPrice
}

function getDetectedProductName(item: ContainerDetailDetectedPrice) {
  return item.productName ?? item.ProductName ?? item.name
}

function getDetectedEnglishName(item: ContainerDetailDetectedPrice) {
  return item.englishName ?? item.EnglishName ?? item.nameEn
}

function getDetectedPackingQuantity(item: ContainerDetailDetectedPrice) {
  return item.PackingQuantity ?? item.packingQuantity ?? item.packingQty
}

function getDetectedUnitVolume(item: ContainerDetailDetectedPrice) {
  return item.WarehouseVolume ?? item.warehouseVolume ?? item.volume ?? item.Volume ?? item.unitVolume ?? item.UnitVolume
}

function calculateContainerDetailTotalAmount(row: ContainerDetail) {
  if (row.装柜数量 == null || row.国内价格 == null) return row.合计装柜金额
  return roundToDigits(row.装柜数量 * row.国内价格 * (row.调整浮率 ?? 1), 2)
}

function calculateContainerDetailTotalVolume(row: ContainerDetail) {
  const unitVolume = row.单件体积 ?? row.商品信息?.单件体积
  if (row.装柜件数 == null || unitVolume == null) return row.合计装柜体积
  return roundToDigits(row.装柜件数 * unitVolume, 3)
}

function buildDetectedPriceMaps(items: ContainerDetailDetectedPrice[]) {
  const productCodeMap = new Map<string, ContainerDetailDetectedPrice>()
  const supplierItemMap = new Map<string, ContainerDetailDetectedPrice>()

  items.forEach((item) => {
    if ((item.Exists ?? item.exists) === false) return
    const productCode = normalizeMatchKey(item.productCode ?? item.ProductCode)
    // 后端旧版本可能不回传 SupplierCode；本入口请求固定为 200，因此按 200 兼容旧响应。
    const supplierItemKey = buildSupplierItemMatchKey(item.supplierCode ?? item.SupplierCode ?? '200', item.itemNumber ?? item.ItemNumber)
    if (productCode) productCodeMap.set(productCode, item)
    if (supplierItemKey) supplierItemMap.set(supplierItemKey, item)
  })

  return { productCodeMap, supplierItemMap }
}

interface ContainerDetailDetectedMatch {
  item: ContainerDetailDetectedPrice
  matchType: ContainerDetailMatchTypeFilter
}

function getDetectedMatchType(item: ContainerDetailDetectedPrice) {
  return item.matchType ?? item.MatchType
}

function isDetectedItemNumberMatch(item: ContainerDetailDetectedPrice) {
  const normalized = getDetectedMatchType(item)?.trim().toLowerCase()
  return normalized === 'item_number' || normalized === 'itemnumber' || normalized === 'supplieritem' || normalized === 'supplier_item'
}

function resolveContainerDetailDetectedMatch(
  row: ContainerDetail,
  detectedMaps: ReturnType<typeof buildDetectedPriceMaps>,
): ContainerDetailDetectedMatch | undefined {
  const itemNumber = normalizeMatchKey(getContainerDetailItemNumber(row))
  const supplierItemKey = buildSupplierItemMatchKey('200', itemNumber)
  const detectionProductCode = normalizeMatchKey(getContainerDetailDetectionProductCode(row))

  // 商品编码能匹配时优先展示商品编码匹配；只有没有商品编码命中时才落到供应商+货号。
  const productCodeMatch = detectionProductCode ? detectedMaps.productCodeMap.get(detectionProductCode) : undefined
  if (productCodeMatch) {
    return {
      item: productCodeMatch,
      matchType: isDetectedItemNumberMatch(productCodeMatch) ? 'supplierItem' : 'productCode',
    }
  }

  // 商品编码未命中时，200 + 货号命中才展示为供应商货号匹配。
  const supplierItemMatch = supplierItemKey ? detectedMaps.supplierItemMap.get(supplierItemKey) : undefined
  if (supplierItemMatch) {
    return {
      item: supplierItemMatch,
      matchType: 'supplierItem',
    }
  }

  return undefined
}

export function buildContainerDetailMatchStatusUpdates(
  rows: ContainerDetail[],
  detectedItems: ContainerDetailDetectedPrice[],
): UpdateContainerDetailRequest[] {
  const detectedMaps = buildDetectedPriceMaps(detectedItems)

  return rows
    .map((row): UpdateContainerDetailRequest | null => {
      if (!row.hguid) return null
      const match = resolveContainerDetailDetectedMatch(row, detectedMaps)
      if (!match) return null

      return {
        hguid: row.hguid,
        matchType: match.matchType,
        是否新商品: false,
      }
    })
    .filter((update): update is UpdateContainerDetailRequest => update !== null)
}

export function buildContainerDetailMatchedPriceUpdates(
  rows: ContainerDetail[],
  detectedItems: ContainerDetailDetectedPrice[],
  container?: Pick<ContainerMain, '汇率' | '运费' | '总体积'> | null,
): UpdateContainerDetailRequest[] {
  return buildContainerDetailMatchedDomesticDataUpdates(rows, detectedItems, container)
}

export function buildContainerDetailMatchedDomesticDataUpdates(
  rows: ContainerDetail[],
  detectedItems: ContainerDetailDetectedPrice[],
  container?: Pick<ContainerMain, '汇率' | '运费' | '总体积'> | null,
): UpdateContainerDetailRequest[] {
  const detectedMaps = buildDetectedPriceMaps(detectedItems)

  return rows
    .map((row): UpdateContainerDetailRequest | null => {
      if (!row.hguid) return null

      const detectedMatch = resolveContainerDetailDetectedMatch(row, detectedMaps)
      if (!detectedMatch) return null

      const update: UpdateContainerDetailRequest = { hguid: row.hguid }
      const match = detectedMatch.item
      update.matchType = detectedMatch.matchType
      update.是否新商品 = false
      const domesticPrice = getDetectedDomesticPrice(match)
      const oemPrice = getDetectedOemPrice(match)
      const productName = getDetectedProductName(match)
      const englishName = getDetectedEnglishName(match)
      const packingQuantity = getDetectedPackingQuantity(match)
      const unitVolume = getDetectedUnitVolume(match)

      if (isMissingPrice(row.国内价格) && domesticPrice != null && domesticPrice > 0) {
        update.国内价格 = domesticPrice
      }
      if (isMissingPrice(row.贴牌价格) && oemPrice != null && oemPrice > 0) {
        update.贴牌价格 = oemPrice
      }
      if (productName && productName !== getContainerDetailProductName(row)) {
        update.商品名称 = productName
      }
      if (englishName && englishName !== getContainerDetailEnglishName(row)) {
        update.英文名称 = englishName
      }
      if (packingQuantity != null && packingQuantity > 0 && packingQuantity !== row.单件装箱数) {
        update.单件装箱数 = packingQuantity
        if (row.装柜件数 != null) {
          update.装柜数量 = roundToDigits(row.装柜件数 * packingQuantity, 2)
        }
      }
      if (unitVolume != null && unitVolume >= 0 && unitVolume !== row.单件体积) {
        update.单件体积 = unitVolume
      }

      const nextRow = mergeContainerDetailPatch(row, update as Partial<ContainerDetail>)
      const totalVolume = calculateContainerDetailTotalVolume(nextRow)
      if (totalVolume !== row.合计装柜体积) update.合计装柜体积 = totalVolume

      const amountRow = mergeContainerDetailPatch(row, update as Partial<ContainerDetail>)
      const totalAmount = calculateContainerDetailTotalAmount(amountRow)
      if (totalAmount !== row.合计装柜金额) update.合计装柜金额 = totalAmount

      const pricedRow = mergeContainerDetailPatch(row, update as Partial<ContainerDetail>)
      const transportCost = calculateContainerDetailTransportCost(pricedRow, container)
      const importPrice = calculateContainerDetailImportPrice(
        { ...pricedRow, 运输成本: transportCost },
        container,
        pricedRow.调整浮率 ?? 1,
        transportCost,
      )
      if (transportCost !== row.运输成本) update.运输成本 = transportCost
      if (importPrice !== row.进口价格) update.进口价格 = importPrice

      return Object.keys(update).length > 1 ? update : null
    })
    .filter((update): update is UpdateContainerDetailRequest => update !== null)
}

export interface ContainerDetailHqPushSelection {
  productCodes: string[]
  items: PushProductsToHqItem[]
  skippedNewProductCount: number
  missingProductCodeCount: number
}

export function buildContainerDetailHqPushSelection(rows: ContainerDetail[]): ContainerDetailHqPushSelection {
  const productCodes: string[] = []
  const items: PushProductsToHqItem[] = []
  let skippedNewProductCount = 0
  let missingProductCodeCount = 0
  const candidateKeys = new Set<string>()

  rows.forEach((row) => {
    const isNewProduct = Boolean(row.是否新商品)
    // 本地没有的新商品不能写 HQ，避免误创建还未补齐资料的商品。
    if (isNewProduct) {
      skippedNewProductCount += 1
      return
    }

    const productCode = row.商品编码?.trim() || row.商品信息?.商品编码?.trim()
    const localSupplierCode = row.localSupplierCode?.trim() || row.商品信息?.localSupplierCode?.trim()
    const itemNumber = row.商品信息?.货号?.trim()
    const productName = getContainerDetailProductName(row)?.trim()
    const englishName = getContainerDetailEnglishName(row)?.trim()
    const barcode = row.商品信息?.条形码?.trim()
    const imageUrl = row.商品图片?.trim() || row.商品信息?.商品图片?.trim()
    if (!productCode && !(localSupplierCode && itemNumber)) {
      missingProductCodeCount += 1
      return
    }

    const candidateKey = productCode
      ? `code:${productCode.toUpperCase()}`
      : `supplier-item:${localSupplierCode!.toUpperCase()}:${itemNumber!.toUpperCase()}`
    if (candidateKeys.has(candidateKey)) {
      return
    }
    candidateKeys.add(candidateKey)

    if (productCode && !productCodes.includes(productCode)) {
      productCodes.push(productCode)
    }

    // 发送候选项时保留货柜明细里的供应商、货号和价格，交由后端决定如何定位 HQ 记录。
    items.push({
      productCode: productCode || undefined,
      localSupplierCode,
      itemNumber,
      productName,
      englishName,
      barcode,
      imageUrl,
      domesticPrice: row.国内价格 == null ? undefined : Number(row.国内价格),
      importPrice: row.进口价格 == null ? undefined : Number(row.进口价格),
      oemPrice: row.贴牌价格 == null ? undefined : Number(row.贴牌价格),
      isNewProduct,
    })
  })

  return {
    productCodes,
    items,
    skippedNewProductCount,
    missingProductCodeCount,
  }
}
