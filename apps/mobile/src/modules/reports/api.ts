import { REPORT_QUERY_TIMEOUT_MS } from "./report-config";

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
  compareTransactions: number;
  averageTransaction: number;
  compareAverageTransaction: number;
}

export interface ExecutiveBranchPerformanceSnapshot {
  rows: BranchRevenueRow[];
  statisticsPending: boolean;
  statisticsExpectedBranchCount: number | null;
  statisticsSnapshotBranchCount: number;
  isComplete: boolean;
  pollingExhausted: boolean;
  pollingAttemptCount: number;
}

export interface ExecutiveBranchPerformancePollingOptions {
  signal?: AbortSignal;
  delaysMs?: readonly number[];
  deadlineMs?: number;
  now?: () => number;
  wait?: (delayMs: number, signal?: AbortSignal) => Promise<void>;
}

export interface RevenueDetailSnapshot<Row> {
  rows: Row[];
  statisticsPending: boolean;
  statisticsExpectedItemCount: number | null;
  statisticsSnapshotItemCount: number | null;
  isComplete: boolean;
  pollingExhausted: boolean;
  pollingAttemptCount: number;
}

export interface RevenueDetailPollingOptions {
  signal?: AbortSignal;
  delaysMs?: readonly number[];
  deadlineMs?: number;
  now?: () => number;
  wait?: (delayMs: number, signal?: AbortSignal) => Promise<void>;
}

const REVENUE_STATISTICS_POLL_DEADLINE_MS = 8_000;
const REVENUE_STATISTICS_POLL_DELAYS_MS = [200, 400, 800, 1_600, 3_200, 6_400] as const;

export interface HourlyRevenueRow {
  id: string;
  hour: number;
  label: string;
  revenue: number;
  compareRevenue: number;
  revenueDelta: number;
  revenueDeltaRatio: number | null;
  transactions: number;
  compareTransactions: number;
  averageTransaction: number;
  compareAverageTransaction: number;
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
  compareTransactions: number;
  averageTransaction: number;
  compareAverageTransaction: number;
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

function asBoolean(value: unknown, fallback = false) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLocaleLowerCase();
    if (normalized === "true" || normalized === "1") return true;
    if (normalized === "false" || normalized === "0") return false;
  }
  return fallback;
}

function asCount(value: unknown) {
  const count = asNullableNumber(value);
  return count === null ? null : Math.max(0, Math.trunc(count));
}

function getRatio(delta: number, compareRevenue: number, explicitRatio: unknown) {
  const normalizedRatio = asNullableNumber(explicitRatio);
  if (normalizedRatio !== null) {
    return normalizedRatio;
  }
  return compareRevenue !== 0 ? delta / compareRevenue : null;
}

function getAverageTransaction(revenue: number, transactions: number, explicitAverage: unknown) {
  const average = asNullableNumber(explicitAverage);
  if (average !== null) {
    return average;
  }
  return transactions > 0 ? revenue / transactions : 0;
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

function getBranchSnapshotRecord(payload: unknown) {
  let record = asRecord(payload);
  for (let depth = 0; record && depth < 4; depth += 1) {
    if (
      pick(
        record,
        "branches",
        "Branches",
        "statisticsPending",
        "StatisticsPending",
        "statisticsExpectedBranchCount",
        "StatisticsExpectedBranchCount",
        "statisticsSnapshotBranchCount",
        "StatisticsSnapshotBranchCount",
      ) !== undefined
    ) {
      return record;
    }
    record = asRecord(pick(record, "data", "Data"));
  }
  return null;
}

function getRevenueDetailSnapshotRecord(payload: unknown) {
  const root = asRecord(payload);
  if (!root) return null;

  // 新接口直接携带 items；现有 HTTP 包络把数组放在 data，但完整性字段仍在根级。
  // 两种形状都必须携带同一组元数据，绝不把裸数组当成完整快照。
  if (root.items !== undefined) {
    return root;
  }
  if (Array.isArray(root.data)) {
    return {
      ...root,
      items: root.data,
    };
  }
  return asRecord(root.data);
}

function isBooleanMetadata(value: unknown) {
  if (typeof value === "boolean") return true;
  if (typeof value === "number") return value === 0 || value === 1;
  if (typeof value !== "string") return false;
  const normalized = value.trim().toLowerCase();
  return normalized === "true" || normalized === "false" || normalized === "0" || normalized === "1";
}

function asNonNegativeInteger(value: unknown) {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0
    ? value
    : null;
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
  const transactions = asNumber(pick(item, "transactions", "Transactions", "orderCount", "OrderCount", "receiptCount", "ReceiptCount"));
  const compareTransactions = asNumber(pick(item, "transactionsLY", "TransactionsLY", "orderCountLY", "OrderCountLY", "receiptCountLY", "ReceiptCountLY"));
  return {
    id: branchCode || String(index),
    branchCode,
    branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName"), branchCode),
    revenue,
    compareRevenue,
    revenueDelta,
    revenueDeltaRatio: getRatio(revenueDelta, compareRevenue, pick(item, "revenueDeltaRatio", "RevenueDeltaRatio", "growthRate", "GrowthRate")),
    transactions,
    compareTransactions,
    averageTransaction: getAverageTransaction(revenue, transactions, pick(item, "aov", "Aov", "averageTransaction", "AverageTransaction", "avgTransaction", "AvgTransaction")),
    compareAverageTransaction: getAverageTransaction(compareRevenue, compareTransactions, pick(item, "aovLY", "AovLY", "averageTransactionLY", "AverageTransactionLY", "avgTransactionLY", "AvgTransactionLY")),
  };
}

function normalizeHourlyRow(raw: unknown, index: number): HourlyRevenueRow {
  const item = asRecord(raw) ?? {};
  const rawHour = pick(item, "hour", "Hour", "hourOfDay", "HourOfDay");
  const hour = parseHour(rawHour, index);
  const revenue = asNumber(pick(item, "revenue", "Revenue", "salesAmount", "SalesAmount", "turnover", "Turnover"));
  const compareRevenue = asNumber(pick(item, "revenueLY", "RevenueLY", "compareRevenue", "CompareRevenue", "previousRevenue", "PreviousRevenue"));
  const revenueDelta = asNumber(pick(item, "revenueDelta", "RevenueDelta", "difference", "Difference"), revenue - compareRevenue);
  const transactions = asNumber(pick(item, "transactions", "Transactions", "orderCount", "OrderCount", "receiptCount", "ReceiptCount"));
  const compareTransactions = asNumber(pick(item, "transactionsLY", "TransactionsLY", "orderCountLY", "OrderCountLY", "receiptCountLY", "ReceiptCountLY"));
  return {
    id: String(hour),
    hour,
    label: asString(pick(item, "label", "Label", "hour", "Hour"), `${String(hour).padStart(2, "0")}:00`),
    revenue,
    compareRevenue,
    revenueDelta,
    revenueDeltaRatio: getRatio(revenueDelta, compareRevenue, pick(item, "revenueDeltaRatio", "RevenueDeltaRatio", "growthRate", "GrowthRate")),
    transactions,
    compareTransactions,
    averageTransaction: getAverageTransaction(revenue, transactions, pick(item, "aov", "Aov", "averageTransaction", "AverageTransaction", "avgTransaction", "AvgTransaction")),
    compareAverageTransaction: getAverageTransaction(compareRevenue, compareTransactions, pick(item, "aovLY", "AovLY", "averageTransactionLY", "AverageTransactionLY", "avgTransactionLY", "AvgTransactionLY")),
  };
}

function normalizeDailyRow(raw: unknown, index: number): DailyRevenueRow {
  const item = asRecord(raw) ?? {};
  const date = normalizeDateLabel(pick(item, "date", "Date", "businessDate", "BusinessDate"), String(index));
  const branchCode = asString(pick(item, "branchCode", "BranchCode", "storeCode", "StoreCode"));
  const revenue = asNumber(pick(item, "revenue", "Revenue", "salesAmount", "SalesAmount", "turnover", "Turnover"));
  const compareRevenue = asNumber(pick(item, "revenueLY", "RevenueLY", "compareRevenue", "CompareRevenue", "previousRevenue", "PreviousRevenue"));
  const revenueDelta = asNumber(pick(item, "revenueDelta", "RevenueDelta", "difference", "Difference"), revenue - compareRevenue);
  const transactions = asNumber(pick(item, "transactions", "Transactions", "orderCount", "OrderCount", "receiptCount", "ReceiptCount"));
  const compareTransactions = asNumber(pick(item, "transactionsLY", "TransactionsLY", "orderCountLY", "OrderCountLY", "receiptCountLY", "ReceiptCountLY"));
  return {
    id: `${date}-${branchCode || index}`,
    date,
    branchCode,
    branchName: asString(pick(item, "branchName", "BranchName", "storeName", "StoreName"), branchCode),
    revenue,
    compareRevenue,
    revenueDelta,
    revenueDeltaRatio: getRatio(revenueDelta, compareRevenue, pick(item, "revenueDeltaRatio", "RevenueDeltaRatio", "growthRate", "GrowthRate")),
    transactions,
    compareTransactions,
    averageTransaction: getAverageTransaction(revenue, transactions, pick(item, "aov", "Aov", "averageTransaction", "AverageTransaction", "avgTransaction", "AvgTransaction")),
    compareAverageTransaction: getAverageTransaction(compareRevenue, compareTransactions, pick(item, "aovLY", "AovLY", "averageTransactionLY", "AverageTransactionLY", "avgTransactionLY", "AvgTransactionLY")),
  };
}

export function normalizeBranchRevenueRows(payload: unknown) {
  return getRows(payload).map(normalizeBranchRow);
}

export function normalizeExecutiveBranchPerformance(
  payload: unknown,
): ExecutiveBranchPerformanceSnapshot {
  const rows = normalizeBranchRevenueRows(payload);
  const snapshot = getBranchSnapshotRecord(payload);
  const rawPending = snapshot
    ? pick(snapshot, "statisticsPending", "StatisticsPending")
    : undefined;
  const rawExpectedCount = snapshot
    ? pick(snapshot, "statisticsExpectedBranchCount", "StatisticsExpectedBranchCount")
    : undefined;
  const rawSnapshotCount = snapshot
    ? pick(snapshot, "statisticsSnapshotBranchCount", "StatisticsSnapshotBranchCount")
    : undefined;
  const statisticsPending = asBoolean(
    rawPending,
  );
  const statisticsExpectedBranchCount = asCount(rawExpectedCount);
  const statisticsSnapshotBranchCount = asCount(rawSnapshotCount) ?? rows.length;
  const hasCompleteMetadata = isBooleanMetadata(rawPending)
    && statisticsExpectedBranchCount !== null
    && asCount(rawSnapshotCount) !== null;
  // 营业额排行是跨分店聚合，裸数组或缺少任一计数都无法证明完整范围；
  // 因此强制进入 Pending 轮询，不能沿用旧客户端的“非空即完整”兼容逻辑。
  const isComplete = hasCompleteMetadata
    && !statisticsPending
    && statisticsSnapshotBranchCount >= statisticsExpectedBranchCount;

  return {
    rows,
    statisticsPending: hasCompleteMetadata ? statisticsPending : true,
    statisticsExpectedBranchCount,
    statisticsSnapshotBranchCount,
    isComplete,
    pollingExhausted: false,
    pollingAttemptCount: 1,
  };
}

/**
 * 后端补算期间可能返回非空的部分快照。轮询留在同一个请求会话内，
 * 这样冷启动耗时会覆盖初次请求和退避，而不会把每次追数误记成新的 warm 会话。
 */
export async function pollExecutiveBranchPerformance(
  loadSnapshot: (signal?: AbortSignal) => Promise<ExecutiveBranchPerformanceSnapshot>,
  options: ExecutiveBranchPerformancePollingOptions = {},
): Promise<ExecutiveBranchPerformanceSnapshot> {
  const delaysMs = options.delaysMs ?? REVENUE_STATISTICS_POLL_DELAYS_MS;
  const deadlineMs = Math.max(0, options.deadlineMs ?? REVENUE_STATISTICS_POLL_DEADLINE_MS);
  const now = options.now ?? Date.now;
  const wait = options.wait ?? waitForRevenueStatistics;
  const deadlineAt = now() + deadlineMs;
  let latest: ExecutiveBranchPerformanceSnapshot | null = null;
  let pollingAttemptCount = 0;

  for (let attempt = 0; ; attempt += 1) {
    throwIfRevenueRequestAborted(options.signal);
    latest = await loadSnapshot(options.signal);
    pollingAttemptCount += 1;
    throwIfRevenueRequestAborted(options.signal);

    if (latest.isComplete) {
      return {
        ...latest,
        pollingExhausted: false,
        pollingAttemptCount,
      };
    }

    const configuredDelayMs = delaysMs[attempt];
    const remainingMs = deadlineAt - now();
    if (configuredDelayMs === undefined || remainingMs <= 0) break;
    await wait(Math.min(Math.max(0, configuredDelayMs), remainingMs), options.signal);
  }

  return {
    ...(latest as ExecutiveBranchPerformanceSnapshot),
    pollingExhausted: true,
    pollingAttemptCount,
  };
}

function normalizeRevenueDetailSnapshot<Row>(
  payload: unknown,
  normalizeRow: (raw: unknown, index: number) => Row,
): RevenueDetailSnapshot<Row> {
  const snapshot = getRevenueDetailSnapshotRecord(payload);
  const rawItems = snapshot?.items;
  const rows = Array.isArray(rawItems) ? rawItems.map(normalizeRow) : [];
  const rawPending = snapshot?.statisticsPending;
  const statisticsExpectedItemCount = asNonNegativeInteger(snapshot?.statisticsExpectedItemCount);
  const statisticsSnapshotItemCount = asNonNegativeInteger(snapshot?.statisticsSnapshotItemCount);
  const hasCompleteMetadata = Array.isArray(rawItems)
    && typeof rawPending === "boolean"
    && statisticsExpectedItemCount !== null
    && statisticsSnapshotItemCount !== null;
  // 分时/逐日明细必须来自同一份可证明完整的统计快照。缺字段、裸数组或计数不一致时
  // 一律 fail-closed，禁止渲染业务行或把“首条可见”计入两秒性能样本。
  const isComplete = hasCompleteMetadata
    && !rawPending
    && statisticsExpectedItemCount === statisticsSnapshotItemCount
    && statisticsSnapshotItemCount === rows.length;

  return {
    rows,
    statisticsPending: hasCompleteMetadata ? !isComplete : true,
    statisticsExpectedItemCount,
    statisticsSnapshotItemCount,
    isComplete,
    pollingExhausted: false,
    pollingAttemptCount: 1,
  };
}

/**
 * 分时与逐日统计和总榜共享同一有界追数策略：整个轮询属于一次冷会话，
 * 因而不会把补算中的后续请求误判为新的 warm 数据。
 */
export async function pollRevenueDetailSnapshot<Row>(
  loadSnapshot: (signal?: AbortSignal) => Promise<RevenueDetailSnapshot<Row>>,
  options: RevenueDetailPollingOptions = {},
): Promise<RevenueDetailSnapshot<Row>> {
  const delaysMs = options.delaysMs ?? REVENUE_STATISTICS_POLL_DELAYS_MS;
  const deadlineMs = Math.max(0, options.deadlineMs ?? REVENUE_STATISTICS_POLL_DEADLINE_MS);
  const now = options.now ?? Date.now;
  const wait = options.wait ?? waitForRevenueStatistics;
  const deadlineAt = now() + deadlineMs;
  let latest: RevenueDetailSnapshot<Row> | null = null;
  let pollingAttemptCount = 0;

  for (let attempt = 0; ; attempt += 1) {
    throwIfRevenueRequestAborted(options.signal);
    latest = await loadSnapshot(options.signal);
    pollingAttemptCount += 1;
    throwIfRevenueRequestAborted(options.signal);

    if (latest.isComplete) {
      return {
        ...latest,
        pollingExhausted: false,
        pollingAttemptCount,
      };
    }

    const configuredDelayMs = delaysMs[attempt];
    const remainingMs = deadlineAt - now();
    if (configuredDelayMs === undefined || remainingMs <= 0) break;
    await wait(Math.min(Math.max(0, configuredDelayMs), remainingMs), options.signal);
  }

  return {
    ...(latest as RevenueDetailSnapshot<Row>),
    pollingExhausted: true,
    pollingAttemptCount,
  };
}

export function normalizeHourlyRevenueRows(payload: unknown) {
  return getRows(payload).map(normalizeHourlyRow);
}

export function normalizeDailyRevenueRows(payload: unknown) {
  return getRows(payload).map(normalizeDailyRow);
}

export function normalizeHourlyRevenueSnapshot(payload: unknown) {
  return normalizeRevenueDetailSnapshot(payload, normalizeHourlyRow);
}

export function normalizeDailyRevenueSnapshot(payload: unknown) {
  return normalizeRevenueDetailSnapshot(payload, normalizeDailyRow);
}

export async function fetchExecutiveBranchPerformance(
  query: RevenueReportQuery,
  options: Pick<ExecutiveBranchPerformancePollingOptions, "signal"> = {},
) {
  const apiClient = await getApiClient();
  return pollExecutiveBranchPerformance(
    async (signal) => {
      const response = await apiClient.get("/react/v1/dashboard/executive-branch-performance", {
        params: buildParams(query),
        ...getRevenueReportRequestConfig(signal),
      });
      return normalizeExecutiveBranchPerformance(response.data);
    },
    options,
  );
}

export async function fetchExecutiveHourlyTraffic(
  query: RevenueReportQuery,
  options: Pick<RevenueDetailPollingOptions, "signal"> = {},
) {
  const apiClient = await getApiClient();
  return pollRevenueDetailSnapshot(
    async (signal) => {
      const response = await apiClient.get("/react/v1/dashboard/executive-hourly-traffic", {
        params: buildParams(query),
        ...getRevenueReportRequestConfig(signal),
      });
      return normalizeHourlyRevenueSnapshot(response.data);
    },
    options,
  );
}

export async function fetchBranchDailyPerformance(
  query: RevenueReportQuery,
  options: Pick<RevenueDetailPollingOptions, "signal"> = {},
) {
  const apiClient = await getApiClient();
  return pollRevenueDetailSnapshot(
    async (signal) => {
      const response = await apiClient.get("/react/v1/dashboard/branch-daily-performance", {
        params: buildParams(query),
        ...getRevenueReportRequestConfig(signal),
      });
      return normalizeDailyRevenueSnapshot(response.data);
    },
    options,
  );
}

export function getRevenueReportRequestConfig(signal?: AbortSignal) {
  return signal
    ? { timeout: REPORT_QUERY_TIMEOUT_MS, signal } as const
    : { timeout: REPORT_QUERY_TIMEOUT_MS } as const;
}

function throwIfRevenueRequestAborted(signal?: AbortSignal) {
  if (!signal?.aborted) return;
  const error = new Error("Revenue statistics request aborted");
  error.name = "AbortError";
  throw error;
}

function waitForRevenueStatistics(delayMs: number, signal?: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    try {
      throwIfRevenueRequestAborted(signal);
    } catch (error) {
      reject(error);
      return;
    }

    const timer = setTimeout(() => {
      signal?.removeEventListener("abort", onAbort);
      resolve();
    }, delayMs);
    const onAbort = () => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
      try {
        throwIfRevenueRequestAborted(signal);
      } catch (error) {
        reject(error);
      }
    };
    signal?.addEventListener("abort", onAbort, { once: true });
  });
}
