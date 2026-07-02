export type WarehouseLocationColumnFilters = Record<string, string[]>
export type WarehouseLocationTableFilters = Record<string, readonly unknown[] | null | undefined>
export type TextFilterMode = 'contains' | 'eq' | 'starts' | 'ends'
export type ComparableFilterMode = 'eq' | 'range' | 'gte' | 'lte'

export interface LocationFilterQuery {
  locationType?: number
  isUsed?: boolean
  locationCode?: string
  locationBarcode?: string
  updatedBy?: string
  status?: number
  filters?: Record<string, string[]>
}

const FILTER_TOKEN_PREFIXES = ['contains', 'eq', 'starts', 'ends', 'gte', 'lte'] as const
const FILTER_TOKEN_NAMESPACE = '__filter'
const SERVER_FILTER_KEYS = [
  'locationCode',
  'locationBarcode',
  'updatedBy',
  'updatedAt',
  'status',
  'locationType',
  'productItemNumber',
  'productBarcode',
  'productName',
] as const

export function setFilterValues(
  filters: WarehouseLocationColumnFilters,
  key: string,
  values?: Array<string | number | boolean | undefined | null>,
) {
  const normalizedValues = (values ?? [])
    .map((value) => value === undefined || value === null ? '' : String(value).trim())
    .filter(Boolean)

  if (!normalizedValues.length) {
    if (!(key in filters)) {
      return filters
    }
    const nextFilters = { ...filters }
    delete nextFilters[key]
    return nextFilters
  }

  return {
    ...filters,
    [key]: normalizedValues,
  }
}

function buildModeToken(mode: (typeof FILTER_TOKEN_PREFIXES)[number], value?: string | number) {
  const normalizedValue = value === undefined || value === null ? '' : String(value).trim()
  return normalizedValue ? `${FILTER_TOKEN_NAMESPACE}:${mode}:${normalizedValue}` : undefined
}

function splitFilterToken(value?: string) {
  const normalizedValue = value?.trim() ?? ''
  const namespacePrefix = `${FILTER_TOKEN_NAMESPACE}:`
  if (!normalizedValue.startsWith(namespacePrefix)) {
    return { mode: undefined, value: normalizedValue }
  }

  const tokenBody = normalizedValue.slice(namespacePrefix.length)
  const separatorIndex = tokenBody.indexOf(':')
  if (separatorIndex <= 0) {
    return { mode: undefined, value: normalizedValue }
  }

  const rawMode = tokenBody.slice(0, separatorIndex)
  const tokenValue = tokenBody.slice(separatorIndex + 1).trim()
  const mode = FILTER_TOKEN_PREFIXES.find((item) => item === rawMode)
  if (!mode) {
    return { mode: undefined, value: normalizedValue }
  }
  return { mode, value: tokenValue }
}

export function buildTextFilterTokens(mode: TextFilterMode, value?: string | number) {
  const token = buildModeToken(mode, value)
  return token ? [token] : []
}

export function parseTextFilterTokens(values?: string[]) {
  const firstValue = values?.find((value) => value.trim()) ?? ''
  const parsed = splitFilterToken(firstValue)
  if (parsed.mode === 'eq' || parsed.mode === 'starts' || parsed.mode === 'ends') {
    return { mode: parsed.mode, value: parsed.value } satisfies { mode: TextFilterMode; value: string }
  }

  return {
    // 兼容旧列头筛选：没有模式前缀的文本值继续按 contains 处理。
    mode: 'contains' as TextFilterMode,
    value: parsed.mode === 'contains' ? parsed.value : firstValue.trim(),
  }
}

export function buildRangeFilterTokens(min?: string | number, max?: string | number) {
  const tokens: string[] = []
  if (min !== undefined && min !== null && String(min).trim()) {
    tokens.push(`gte:${String(min).trim()}`)
  }
  if (max !== undefined && max !== null && String(max).trim()) {
    tokens.push(`lte:${String(max).trim()}`)
  }
  return tokens
}

export function findFilterTokenValue(values: string[] | undefined, prefix: 'gte:' | 'lte:') {
  return values?.find((value) => value.startsWith(prefix))?.slice(prefix.length) ?? ''
}

export function buildComparableFilterTokens(
  mode: ComparableFilterMode,
  values: { value?: string | number; min?: string | number; max?: string | number },
) {
  if (mode === 'range') {
    return buildRangeFilterTokens(values.min, values.max)
  }
  if (mode === 'gte') {
    return buildRangeFilterTokens(values.value, undefined)
  }
  if (mode === 'lte') {
    return buildRangeFilterTokens(undefined, values.value)
  }

  const token = buildModeToken(mode, values.value)
  return token ? [token] : []
}

export function parseComparableFilterTokens(values?: string[]) {
  const normalizedValues = values?.map((value) => value.trim()).filter(Boolean) ?? []
  const gteValue = findFilterTokenValue(normalizedValues, 'gte:')
  const lteValue = findFilterTokenValue(normalizedValues, 'lte:')
  if (gteValue && lteValue) {
    return { mode: 'range' as ComparableFilterMode, min: gteValue, max: lteValue, value: '' }
  }
  if (gteValue) {
    return { mode: 'gte' as ComparableFilterMode, value: gteValue, min: '', max: '' }
  }
  if (lteValue) {
    return { mode: 'lte' as ComparableFilterMode, value: lteValue, min: '', max: '' }
  }

  const parsed = splitFilterToken(normalizedValues[0])
  if (parsed.mode === 'eq') {
    return { mode: 'eq' as ComparableFilterMode, value: parsed.value, min: '', max: '' }
  }

  // 兼容旧日期筛选：裸值默认视为精确匹配。
  return { mode: 'eq' as ComparableFilterMode, value: normalizedValues[0] ?? '', min: '', max: '' }
}

export function normalizeLocationTableFilters(
  filters: WarehouseLocationTableFilters,
): WarehouseLocationColumnFilters {
  const filterKeyMap: Record<string, string> = {
    itemNumbers: 'productItemNumber',
    productBarcodes: 'productBarcode',
    productNames: 'productName',
  }

  return Object.entries(filters).reduce<WarehouseLocationColumnFilters>((current, [key, value]) => {
    if (!value?.length) {
      return current
    }
    const mappedFilterKey = filterKeyMap[key] ?? key
    return setFilterValues(current, mappedFilterKey, value.map((item) => String(item).trim()))
  }, {})
}

function getSingleFilterValue(values?: string[]) {
  return values?.length === 1 ? values[0] : undefined
}

function parseBooleanFilter(values?: string[]) {
  const value = getSingleFilterValue(values)
  if (value === 'true') {
    return true
  }
  if (value === 'false') {
    return false
  }
  return undefined
}

export function buildLocationFilterQuery(columnFilters: WarehouseLocationColumnFilters): LocationFilterQuery {
  const nestedFilters = SERVER_FILTER_KEYS.reduce<Record<string, string[]>>((current, key) => {
    const values = columnFilters[key]
    if (values?.length) {
      current[key] = values
    }
    return current
  }, {})

  const query: LocationFilterQuery = {
    isUsed: parseBooleanFilter(columnFilters.usage),
    filters: Object.keys(nestedFilters).length ? nestedFilters : undefined,
  }

  // usage 仍走顶层 IsUsed；其他列头筛选统一放进 filters，保留 eq/starts/range 等 token 语义。
  return query
}
