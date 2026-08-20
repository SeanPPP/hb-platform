const DEFAULT_PRODUCT_IMAGE_BASE_URL =
  'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200';
const DEFAULT_RANKING_PAGE_SIZE = 50;

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
  const normalizedPageSize = Number.isInteger(pageSize) && pageSize > 0
    ? pageSize
    : DEFAULT_RANKING_PAGE_SIZE;
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
