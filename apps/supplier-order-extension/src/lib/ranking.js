const DEFAULT_PRODUCT_IMAGE_BASE_URL =
  'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200';
const DEFAULT_RANKING_PAGE_SIZE = 50;
const RANKING_PAGE_SIZES = new Set([50, 100, 200]);
const SALES_RANK_BANDS = new Set(['top-10', 'top-20', 'top-30']);

export function normalizeStoreOptions(data) {
  const stores = data && Array.isArray(data.stores) ? data.stores : [];
  const seen = new Set();
  const result = [];
  for (const store of stores) {
    if (!store || typeof store !== 'object') continue;
    const code = String(store.storeCode ?? store.code ?? '').trim();
    if (!code || seen.has(code.toUpperCase())) continue;
    seen.add(code.toUpperCase());
    const name = String(store.storeName ?? store.name ?? code).trim() || code;
    result.push({ code, name });
  }
  return result;
}

export function normalizeRankingDays(value) {
  return Number(value) === 90 ? 90 : 60;
}

export function normalizeRankingPageSize(value) {
  const numericValue = Number(value);
  return RANKING_PAGE_SIZES.has(numericValue) ? numericValue : DEFAULT_RANKING_PAGE_SIZE;
}

export function normalizeSalesRankBand(value) {
  const normalized = typeof value === 'string' ? value.trim().toLowerCase() : '';
  return SALES_RANK_BANDS.has(normalized) ? normalized : null;
}

export function formatSalesRankBand(value) {
  const normalized = normalizeSalesRankBand(value);
  if (!normalized) return '';
  return `TOP ${normalized.slice(4)}%`;
}

export function transitionRankingPagination(state, action) {
  const pageSize = normalizeRankingPageSize(state?.pageSize);
  if (action?.type === 'page-size') {
    return { page: 1, pageSize: normalizeRankingPageSize(action.pageSize) };
  }
  if (action?.type === 'context') return { page: 1, pageSize };
  const requestedPage = Number(action?.page);
  const page = Number.isFinite(requestedPage) ? Math.max(1, Math.trunc(requestedPage)) : 1;
  return { page, pageSize };
}

export function beginRankingLoad({
  page = 1,
  pageSize = DEFAULT_RANKING_PAGE_SIZE,
  data = null,
  legacyItems = null,
} = {}, { clear = false } = {}) {
  const requestedPage = Math.max(1, Math.trunc(Number(page)) || 1);
  const requestedPageSize = normalizeRankingPageSize(pageSize);
  const checkpoint = {
    page: Number.isInteger(data?.page) && data.page >= 1 ? data.page : requestedPage,
    // pageSize 是用户偏好；请求失败时保留新选择，旧页数据仅作为可见回退内容。
    pageSize: requestedPageSize,
    data,
    legacyItems,
  };
  return {
    checkpoint,
    state: {
      page: requestedPage,
      pageSize: requestedPageSize,
      data: clear ? null : data,
      legacyItems: clear ? null : legacyItems,
      loading: true,
      error: null,
    },
  };
}

export function restoreRankingLoad(checkpoint, error) {
  return {
    page: Math.max(1, Math.trunc(Number(checkpoint?.page)) || 1),
    pageSize: normalizeRankingPageSize(checkpoint?.pageSize),
    data: checkpoint?.data ?? null,
    legacyItems: checkpoint?.legacyItems ?? null,
    loading: false,
    error: String((error && error.message) || error || ''),
  };
}

export function resolveRankingRetryTarget(target, { supplierCode, days } = {}) {
  const targetSupplierCode = String(target?.supplierCode || '').trim().toUpperCase();
  const currentSupplierCode = String(supplierCode || '').trim().toUpperCase();
  const targetDays = Number(target?.days);
  const page = Number(target?.page);
  const pageSize = Number(target?.pageSize);
  if (
    !targetSupplierCode
    || targetSupplierCode !== currentSupplierCode
    || ![60, 90].includes(targetDays)
    || targetDays !== normalizeRankingDays(days)
    || !Number.isInteger(page)
    || page < 1
    || normalizeRankingPageSize(pageSize) !== pageSize
  ) {
    return null;
  }
  return { page, pageSize };
}

export function resolveRankingViewState({
  hasSupplier,
  loading,
  error,
  totalRankedCount,
} = {}) {
  if (!hasSupplier) return 'no-supplier';
  if (loading) return 'loading';
  if (error) return 'error';
  if (totalRankedCount === 0) return 'empty';
  if (Number(totalRankedCount) > 0) return 'content';
  return 'idle';
}

export function normalizeTopSalesRequest({ topPercent, page, pageSize } = {}) {
  const providedCount = [topPercent, page, pageSize].filter((value) => value != null).length;
  if (providedCount === 0) return null;
  const numericTopPercent = Number(topPercent);
  const numericPage = Number(page);
  const numericPageSize = Number(pageSize);
  if (
    providedCount !== 3
    || numericTopPercent !== 30
    || !Number.isInteger(numericPage)
    || numericPage < 1
    || normalizeRankingPageSize(numericPageSize) !== numericPageSize
  ) {
    throw new Error('无效的热销榜分页参数');
  }
  return {
    topPercent: numericTopPercent,
    page: numericPage,
    pageSize: numericPageSize,
  };
}

export function normalizeSupplierOptions(profiles) {
  const seen = new Set();
  const result = [];
  for (const profile of Array.isArray(profiles) ? profiles : []) {
    if (!profile || typeof profile !== 'object') continue;
    const code = String(profile.supplierCode ?? '').trim();
    const key = code.toUpperCase();
    if (!code || seen.has(key)) continue;
    seen.add(key);
    const name = String(profile.displayName ?? code).trim() || code;
    result.push({ code, name });
  }
  return result;
}

export function shouldPreserveManualSupplier({
  manualSupplierCode,
  detectedSupplierCode,
  previousDetectedSupplierCode,
}) {
  if (!String(manualSupplierCode || '').trim()) return false;
  const detectedCode = String(detectedSupplierCode || '').trim();
  const previousCode = String(previousDetectedSupplierCode || '').trim();
  return !detectedCode || detectedCode.toUpperCase() === previousCode.toUpperCase();
}

export function paginateRanking(items, requestedPage, pageSize = DEFAULT_RANKING_PAGE_SIZE) {
  const source = Array.isArray(items) ? items : [];
  const normalizedPageSize = normalizeRankingPageSize(pageSize);
  const totalPages = Math.max(1, Math.ceil(source.length / normalizedPageSize));
  const numericPage = Number.isFinite(Number(requestedPage)) ? Math.trunc(Number(requestedPage)) : 1;
  const page = Math.min(totalPages, Math.max(1, numericPage));
  const start = (page - 1) * normalizedPageSize;
  return {
    items: source.slice(start, start + normalizedPageSize),
    page,
    totalPages,
    totalItems: source.length,
    pageSize: normalizedPageSize,
  };
}

export function normalizeTopSalesPage(raw, {
  requestedPage = 1,
  requestedPageSize = DEFAULT_RANKING_PAGE_SIZE,
  requestedSupplierCode = null,
  requestedDays = null,
} = {}) {
  if (!raw || typeof raw !== 'object' || Array.isArray(raw) || !Array.isArray(raw.items)) {
    throw new Error('无效的热销榜分页响应');
  }
  const data = raw;
  const source = data.items;
  const topPercent = data.topPercent;
  const responsePage = data.page;
  const responsePageSize = data.pageSize;
  const responseTotalPages = data.totalPages;
  const totalProductCount = data.totalProductCount;
  const expectedSupplierCode = String(requestedSupplierCode || '').trim().toUpperCase();
  const responseSupplierCode = String(data.supplierCode || '').trim().toUpperCase();
  const hasMatchingContext = (!expectedSupplierCode || responseSupplierCode === expectedSupplierCode)
    && (requestedDays == null || data.days === normalizeRankingDays(requestedDays));
  const isLegacy = (topPercent == null || topPercent === 10)
    && responsePage == null
    && responsePageSize == null
    && responseTotalPages == null;

  if (isLegacy) {
    const expectedLegacyTotal = Number.isInteger(totalProductCount) && totalProductCount >= 0
      ? Math.ceil(totalProductCount * 0.1)
      : -1;
    const legacyTotal = data.totalRankedCount == null
      ? source.length
      : data.totalRankedCount;
    const hasValidRanks = source.every(
      (item, index) => item
        && typeof item === 'object'
        && item.rank === index + 1
        && (item.salesRankBand == null || item.salesRankBand === 'top-10'),
    );
    if (
      !hasMatchingContext
      || expectedLegacyTotal < 0
      || !Number.isInteger(legacyTotal)
      || legacyTotal !== expectedLegacyTotal
      || source.length !== expectedLegacyTotal
      || !hasValidRanks
    ) {
      throw new Error('无效的热销榜分页响应');
    }
    const paged = paginateRanking(source, requestedPage, requestedPageSize);
    return {
      mode: 'legacy',
      topPercent: 10,
      items: paged.items,
      totalRankedCount: paged.totalItems,
      page: paged.page,
      pageSize: paged.pageSize,
      totalPages: paged.totalPages,
    };
  }

  if (
    topPercent !== 30
    || !hasMatchingContext
    || !Number.isInteger(totalProductCount)
    || totalProductCount < 0
    || !Number.isInteger(data.totalRankedCount)
    || data.totalRankedCount < 0
    || !Number.isInteger(responsePage)
    || responsePage < 1
    || !Number.isInteger(responsePageSize)
    || normalizeRankingPageSize(responsePageSize) !== responsePageSize
    || !Number.isInteger(responseTotalPages)
    || responseTotalPages < 0
    || normalizeRankingPageSize(requestedPageSize) !== Number(requestedPageSize)
    || responsePageSize !== Number(requestedPageSize)
    || !Number.isInteger(Number(requestedPage))
    || Number(requestedPage) < 1
  ) {
    throw new Error('无效的热销榜分页响应');
  }

  const totalRankedCount = data.totalRankedCount;
  const expectedTotalRankedCount = Math.ceil(totalProductCount * 0.3);
  const expectedTotalPages = totalRankedCount === 0
    ? 0
    : Math.ceil(totalRankedCount / responsePageSize);
  const expectedPage = expectedTotalPages === 0
    ? 1
    : Math.min(Number(requestedPage), expectedTotalPages);
  const expectedItemCount = totalRankedCount === 0
    ? 0
    : Math.min(responsePageSize, totalRankedCount - ((responsePage - 1) * responsePageSize));
  const firstExpectedRank = ((responsePage - 1) * responsePageSize) + 1;
  const hasValidItems = source.length === expectedItemCount && source.every(
    (item, index) => item
      && typeof item === 'object'
      && item.rank === firstExpectedRank + index
      && item.salesRankBand === (
        item.rank <= Math.ceil(totalProductCount * 0.1)
          ? 'top-10'
          : item.rank <= Math.ceil(totalProductCount * 0.2)
            ? 'top-20'
            : 'top-30'
      ),
  );
  if (
    totalRankedCount !== expectedTotalRankedCount
    || responseTotalPages !== expectedTotalPages
    || responsePage !== expectedPage
    || !hasValidItems
  ) {
    throw new Error('无效的热销榜分页响应');
  }

  return {
    mode: 'server',
    topPercent: 30,
    items: source,
    totalRankedCount,
    page: responsePage,
    pageSize: responsePageSize,
    totalPages: responseTotalPages,
  };
}

export function formatAverageSellingPrice(value) {
  if (value == null || value === '') return '—';
  const numericValue = Number(value);
  if (!Number.isFinite(numericValue)) return '—';
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: 'AUD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(numericValue);
}

export function buildDefaultProductImageUrl(itemNumber, productCode) {
  const imageKey = String(itemNumber || productCode || '').trim();
  return imageKey
    ? `${DEFAULT_PRODUCT_IMAGE_BASE_URL}/${encodeURIComponent(imageKey)}.jpg`
    : '';
}

function toAbsoluteUrl(value, apiOrigin) {
  const raw = String(value || '').trim();
  if (!raw) return '';
  try {
    return new URL(raw, `${String(apiOrigin || '').replace(/\/$/, '')}/`).href;
  } catch {
    return '';
  }
}

export function buildProductImageCandidates(item, apiOrigin) {
  const candidates = [
    toAbsoluteUrl(item && item.imageUrl, apiOrigin),
    buildDefaultProductImageUrl(item && item.itemNumber),
    buildDefaultProductImageUrl(item && item.productCode),
  ].filter(Boolean);
  return [...new Set(candidates)];
}
