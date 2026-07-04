export interface RevenueReportQuery {
  startDate: string;
  endDate: string;
  compareStartDate: string;
  compareEndDate: string;
  compareMode: "ByDate" | "ByWeek";
  branchCodes?: string[];
  topN?: number;
}

export interface BranchRevenueRow {
  id: string;
  branchCode: string;
  branchName: string;
  revenue: number;
  compareRevenue: number;
  revenueDelta: number;
  revenueDeltaRatio: number | null;
  transactions: number;
  averageTransaction: number;
}

export interface HourlyRevenueRow {
  id: string;
  hour: number;
  label: string;
  revenue: number;
  compareRevenue: number;
  revenueDelta: number;
  revenueDeltaRatio: number | null;
  transactions: number;
}

export interface DailyRevenueRow {
  id: string;
  date: string;
  branchCode: string;
  branchName: string;
  revenue: number;
  compareRevenue: number;
  revenueDelta: number;
  revenueDeltaRatio: number | null;
  transactions: number;
}

async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asString(value: unknown, fallback = "") {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return fallback;
}

function asNumber(value: unknown, fallback = 0) {
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function asNullableNumber(value: unknown) {
  if (value === null || value === undefined || value === "") {
    return null;
  }
  const parsed = asNumber(value, Number.NaN);
  return Number.isFinite(parsed) ? parsed : null;
}

function getRatio(delta: number, compareRevenue: number, explicitRatio: unknown) {
  const normalizedRatio = asNullableNumber(explicitRatio);
  if (normalizedRatio !== null) {
    return normalizedRatio;
  }
  return compareRevenue !== 0 ? delta / compareRevenue : null;
}

function parseHour(value: unknown, fallback: number) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return Math.trunc(value);
  }
  if (typeof value === "string") {
    const match = value.match(/\d{1,2}/);
    if (match) {
      return Number(match[0]);
    }
  }
  return fallback;
}

function normalizeDateLabel(value: unknown, fallback: string) {
  const date = asString(value, fallback);
  return date.length >= 10 ? date.slice(0, 10) : date;
}

function getRows(payload: unknown) {
  if (Array.isArray(payload)) {
    return payload;
  }
  const root = asRecord(payload) ?? {};
  const data = pick(root, "items", "Items", "rows", "Rows", "branches", "Branches", "data", "Data");
  if (Array.isArray(data)) {
    return data;
  }
  const nested = asRecord(data);
  if (nested) {
    return getRows(nested);
  }
  return [];
}

function buildParams(query: RevenueReportQuery) {
  const params = new URLSearchParams({
    startDate: query.startDate,
    endDate: query.endDate,
    compareStartDate: query.compareStartDate,
    compareEndDate: query.compareEndDate,
    compareMode: query.compareMode,
  });
  query.branchCodes?.filter(Boolean).forEach((branchCode) => {
    params.append("branchCodes", branchCode);
  });
  if (query.topN != null) {
    params.set("topN", String(query.topN));
  }
  return params;
}

function normalizeBranchRow(raw: unknown, index: number): BranchRevenueRow {
  const item = asRecord(raw) ?? {};
  const branchCode = asString(pick(item, "branchCode", "BranchCode", "storeCode", "StoreCode"), `branch-${index}`);
  const revenue = asNumber(pick(item, "revenue", "Revenue", "salesAmount", "SalesAmount", "turnover", "Turnover"));
  const compareRevenue = asNumber(pick(item, "revenueLY", "RevenueLY", "compareRevenue", "CompareRevenue", "previousRevenue", "PreviousRevenue", "totalRevenueLY", "TotalRevenueLY"));
  const revenueDelta = asNumber(pick(item, "revenueDelta", "RevenueDelta", "difference", "Difference"), revenue - compareRevenue);
  return {
    id: branchCode || String(index),
    branchCode,
    branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName"), branchCode),
    revenue,
    compareRevenue,
    revenueDelta,
    revenueDeltaRatio: getRatio(revenueDelta, compareRevenue, pick(item, "revenueDeltaRatio", "RevenueDeltaRatio", "growthRate", "GrowthRate")),
    transactions: asNumber(pick(item, "transactions", "Transactions", "orderCount", "OrderCount", "receiptCount", "ReceiptCount")),
    averageTransaction: asNumber(pick(item, "aov", "Aov", "averageTransaction", "AverageTransaction", "avgTransaction", "AvgTransaction")),
  };
}

function normalizeHourlyRow(raw: unknown, index: number): HourlyRevenueRow {
  const item = asRecord(raw) ?? {};
  const rawHour = pick(item, "hour", "Hour", "hourOfDay", "HourOfDay");
  const hour = parseHour(rawHour, index);
  const revenue = asNumber(pick(item, "revenue", "Revenue", "salesAmount", "SalesAmount", "turnover", "Turnover"));
  const compareRevenue = asNumber(pick(item, "revenueLY", "RevenueLY", "compareRevenue", "CompareRevenue", "previousRevenue", "PreviousRevenue"));
  const revenueDelta = asNumber(pick(item, "revenueDelta", "RevenueDelta", "difference", "Difference"), revenue - compareRevenue);
  return {
    id: String(hour),
    hour,
    label: asString(pick(item, "label", "Label", "hour", "Hour"), `${String(hour).padStart(2, "0")}:00`),
    revenue,
    compareRevenue,
    revenueDelta,
    revenueDeltaRatio: getRatio(revenueDelta, compareRevenue, pick(item, "revenueDeltaRatio", "RevenueDeltaRatio", "growthRate", "GrowthRate")),
    transactions: asNumber(pick(item, "transactions", "Transactions", "orderCount", "OrderCount", "receiptCount", "ReceiptCount")),
  };
}

function normalizeDailyRow(raw: unknown, index: number): DailyRevenueRow {
  const item = asRecord(raw) ?? {};
  const date = normalizeDateLabel(pick(item, "date", "Date", "businessDate", "BusinessDate"), String(index));
  const branchCode = asString(pick(item, "branchCode", "BranchCode", "storeCode", "StoreCode"));
  const revenue = asNumber(pick(item, "revenue", "Revenue", "salesAmount", "SalesAmount", "turnover", "Turnover"));
  const compareRevenue = asNumber(pick(item, "revenueLY", "RevenueLY", "compareRevenue", "CompareRevenue", "previousRevenue", "PreviousRevenue"));
  const revenueDelta = asNumber(pick(item, "revenueDelta", "RevenueDelta", "difference", "Difference"), revenue - compareRevenue);
  return {
    id: `${date}-${branchCode || index}`,
    date,
    branchCode,
    branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName"), branchCode),
    revenue,
    compareRevenue,
    revenueDelta,
    revenueDeltaRatio: getRatio(revenueDelta, compareRevenue, pick(item, "revenueDeltaRatio", "RevenueDeltaRatio", "growthRate", "GrowthRate")),
    transactions: asNumber(pick(item, "transactions", "Transactions", "orderCount", "OrderCount", "receiptCount", "ReceiptCount")),
  };
}

export function normalizeBranchRevenueRows(payload: unknown) {
  return getRows(payload).map(normalizeBranchRow);
}

export function normalizeHourlyRevenueRows(payload: unknown) {
  return getRows(payload).map(normalizeHourlyRow);
}

export function normalizeDailyRevenueRows(payload: unknown) {
  return getRows(payload).map(normalizeDailyRow);
}

export async function fetchExecutiveBranchPerformance(query: RevenueReportQuery) {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/react/v1/dashboard/executive-branch-performance", {
    params: buildParams(query),
  });
  return normalizeBranchRevenueRows(response.data);
}

export async function fetchExecutiveHourlyTraffic(query: RevenueReportQuery) {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/react/v1/dashboard/executive-hourly-traffic", {
    params: buildParams(query),
  });
  return normalizeHourlyRevenueRows(response.data);
}

export async function fetchBranchDailyPerformance(query: RevenueReportQuery) {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/react/v1/dashboard/branch-daily-performance", {
    params: buildParams(query),
  });
  return normalizeDailyRevenueRows(response.data);
}
