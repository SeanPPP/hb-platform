const responseError = () => new Error('分店销量响应无效');

function normalizeIdentity(value) {
  return String(value || '').trim();
}

function normalizeQuantity(value) {
  const quantity = Number(value);
  if (!Number.isFinite(quantity)) throw responseError();
  return quantity;
}

function normalizeDate(value) {
  const date = normalizeIdentity(value);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) throw responseError();
  return date;
}

export function normalizeStoreSalesResponse(raw, expected = {}) {
  if (!raw || typeof raw !== 'object' || !Array.isArray(raw.stores)) {
    throw responseError();
  }

  const supplierCode = normalizeIdentity(raw.supplierCode);
  const productCode = normalizeIdentity(raw.productCode);
  const days = Number(raw.days);
  const startDate = normalizeDate(raw.startDate);
  const endDate = normalizeDate(raw.endDate);
  const snapshotVersion = normalizeIdentity(raw.snapshotVersion);
  const expectedSupplierCode = normalizeIdentity(expected.supplierCode);
  const expectedProductCode = normalizeIdentity(expected.productCode);
  const expectedDays = Number(expected.days);
  const expectedStartDate = normalizeIdentity(expected.startDate);
  const expectedEndDate = normalizeIdentity(expected.endDate);
  const expectedSnapshotVersion = normalizeIdentity(expected.snapshotVersion);
  if (
    !supplierCode
    || !productCode
    || !Number.isInteger(days)
    || (expectedSupplierCode && supplierCode !== expectedSupplierCode)
    || (expectedProductCode && productCode !== expectedProductCode)
    || (Number.isFinite(expectedDays) && days !== expectedDays)
    || (expectedStartDate && startDate !== expectedStartDate)
    || (expectedEndDate && endDate !== expectedEndDate)
    || !snapshotVersion
    || (expectedSnapshotVersion && snapshotVersion !== expectedSnapshotVersion)
  ) {
    throw responseError();
  }

  const enabledStoreCount = Number(raw.enabledStoreCount);
  const totalSalesQuantity = normalizeQuantity(raw.totalSalesQuantity);
  const expectedTotalSalesQuantity = Number(expected.totalSalesQuantity);
  if (!Number.isInteger(enabledStoreCount) || enabledStoreCount < 0) {
    throw responseError();
  }
  if (
    Number.isFinite(expectedTotalSalesQuantity)
    && Math.abs(totalSalesQuantity - expectedTotalSalesQuantity) > 0.000001
  ) {
    throw responseError();
  }

  const storeCodes = new Set();
  const stores = raw.stores.map((rawStore) => {
    if (!rawStore || typeof rawStore !== 'object') throw responseError();
    const storeCode = normalizeIdentity(rawStore.storeCode);
    const storeName = normalizeIdentity(rawStore.storeName) || storeCode;
    const normalizedCode = storeCode.toLocaleLowerCase('en-AU');
    if (!storeCode || storeCodes.has(normalizedCode)) throw responseError();
    storeCodes.add(normalizedCode);
    return {
      storeCode,
      storeName,
      salesQuantity: normalizeQuantity(rawStore.salesQuantity),
    };
  });
  if (stores.length !== enabledStoreCount) throw responseError();

  const storeTotal = stores.reduce((sum, store) => sum + store.salesQuantity, 0);
  if (Math.abs(storeTotal - totalSalesQuantity) > 0.000001) throw responseError();

  stores.sort((left, right) => (
    right.salesQuantity - left.salesQuantity
    || left.storeName.localeCompare(right.storeName, undefined, { sensitivity: 'base' })
    || left.storeCode.localeCompare(right.storeCode, undefined, { sensitivity: 'base' })
  ));

  return {
    ...raw,
    supplierCode,
    productCode,
    days,
    startDate,
    endDate,
    snapshotVersion,
    enabledStoreCount,
    totalSalesQuantity,
    stores,
  };
}

export function filterStoreSales(stores, query) {
  const normalizedQuery = normalizeIdentity(query).toLocaleLowerCase();
  const source = Array.isArray(stores) ? stores : [];
  if (!normalizedQuery) return source;
  return source.filter((store) => (
    normalizeIdentity(store?.storeName).toLocaleLowerCase().includes(normalizedQuery)
    || normalizeIdentity(store?.storeCode).toLocaleLowerCase().includes(normalizedQuery)
  ));
}
