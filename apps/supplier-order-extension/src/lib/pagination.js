export const DEFAULT_PAGE_SIZE = 20;

function firstString(obj, keys) {
  for (const k of keys) {
    const v = obj[k];
    if (typeof v === 'string' && v) return v;
    if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  }
  return '';
}

function firstNumber(obj, keys) {
  for (const k of keys) {
    const v = obj[k];
    if (typeof v === 'number' && Number.isFinite(v)) return v;
    if (typeof v === 'string' && v !== '' && Number.isFinite(Number(v))) return Number(v);
  }
  return null;
}

function toCycle(type, entry) {
  if (!entry || typeof entry !== 'object') return null;
  const orderNo = firstString(entry, [
    'orderNo',
    'orderNumber',
    'invoiceNo',
    'invoiceNumber',
    'no',
    'code',
  ]);
  const date = firstString(entry, [
    'date',
    'purchaseDate',
    'orderDate',
    'salesDate',
    'createdAt',
    'transactionDate',
    'timestamp',
  ]);
  const quantity = firstNumber(entry, [
    type === 'order' ? 'purchaseQuantity' : 'salesQuantity',
    'quantity',
    'qty',
    'amount',
    'count',
  ]) ?? 0;
  const priceKeys =
    type === 'order'
      ? ['averagePurchasePrice', 'avgPurchasePrice', 'purchasePrice', 'unitPrice', 'price']
      : ['averageSalePrice', 'averageSellPrice', 'avgSellPrice', 'sellPrice', 'salePrice', 'price'];
  const price = firstNumber(entry, priceKeys);
  return { type, orderNo, date, quantity, price };
}

// 归一化采购周期响应（订货/销售字段映射）
export function normalizeCycles(raw) {
  if (Array.isArray(raw?.cycles)) {
    const orders = [];
    const sales = [];
    for (const cycle of raw.cycles) {
      if (!cycle || typeof cycle !== 'object') continue;
      orders.push({
        type: 'order',
        orderNo: Array.isArray(cycle.invoiceNumbers)
          ? cycle.invoiceNumbers.filter(Boolean).join(', ')
          : firstString(cycle, ['invoiceNumber', 'invoiceNo', 'orderNo']),
        date: firstString(cycle, ['purchaseDate']),
        quantity: firstNumber(cycle, ['purchaseQuantity']) ?? 0,
        price: firstNumber(cycle, ['averagePurchasePrice']),
      });
      const salesStartDate = firstString(cycle, ['salesStartDate']);
      const salesEndDate = firstString(cycle, ['salesEndDate']);
      sales.push({
        type: 'sales',
        orderNo: '',
        date: salesEndDate || salesStartDate,
        dateRange:
          salesStartDate && salesEndDate ? `${salesStartDate} — ${salesEndDate}` : salesEndDate,
        quantity: firstNumber(cycle, ['salesQuantity']) ?? 0,
        price: firstNumber(cycle, ['averageSalePrice']),
      });
    }
    return { orders, sales };
  }

  const ordersSource = raw?.orders || raw?.purchaseCycles || raw?.cycles || [];
  const salesSource = raw?.sales || raw?.sellRecords || [];
  return {
    orders: (Array.isArray(ordersSource) ? ordersSource : [])
      .map((e) => toCycle('order', e))
      .filter(Boolean),
    sales: (Array.isArray(salesSource) ? salesSource : [])
      .map((e) => toCycle('sales', e))
      .filter(Boolean),
  };
}

export function parseDate(value) {
  if (value == null) return 0;
  const t = Date.parse(value);
  return Number.isFinite(t) ? t : 0;
}

export function sortByDateDesc(items) {
  return [...items].sort((a, b) => parseDate(b.date) - parseDate(a.date));
}

// 采购周期最多 6 次/12 个月，订货与销售按日期倒序混排
export function buildTimeline({
  orders = [],
  sales = [],
  now = Date.now(),
  maxOrderCycles = 6,
  maxMonths = 12,
} = {}) {
  const cutoffDate = new Date(now);
  cutoffDate.setUTCMonth(cutoffDate.getUTCMonth() - maxMonths);
  const cutoff = cutoffDate.getTime();
  const within = (e) => {
    if (!e.date) return true;
    const t = parseDate(e.date);
    return t === 0 || t >= cutoff;
  };
  const orderList = sortByDateDesc((orders || []).filter(within)).slice(0, maxOrderCycles);
  const salesList = (sales || []).filter(within);
  const merged = [
    ...orderList.map((e) => ({ ...e, type: 'order' })),
    ...salesList.map((e) => ({ ...e, type: 'sales' })),
  ];
  return sortByDateDesc(merged);
}

export function filterTimeline(timeline, filter = 'all') {
  const list = timeline || [];
  if (filter === 'order') return list.filter((e) => e.type === 'order');
  if (filter === 'sales') return list.filter((e) => e.type === 'sales');
  return list;
}

export function paginate(items, page = 1, pageSize = DEFAULT_PAGE_SIZE) {
  const list = items || [];
  const total = list.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const start = (safePage - 1) * pageSize;
  return {
    items: list.slice(start, start + pageSize),
    total,
    page: safePage,
    pageSize,
    totalPages,
  };
}
