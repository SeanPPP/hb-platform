/**
 * 报表只在内存中保留“完整结果”。key 覆盖所有会改变业务结果的安全筛选条件，
 * 但该 key 不进入性能日志或应用日志，避免把搜索词等用户输入写入日志。
 */
export interface ReportSnapshotKeyInput {
  accountIdentity: string;
  tab: "revenue" | "product";
  startDate: string;
  endDate: string;
  compareStartDate: string;
  compareEndDate: string;
  compareMode: string;
  branchCodes: readonly string[];
  scopeVersion?: number;
  period?: string;
  supplierKind?: string;
  supplierCode?: string | null;
  search?: string;
  page?: number;
  pageSize?: number;
  detail?: string;
}

export interface CompleteReportSnapshot<T> {
  data: T;
  storedAt: number;
  statisticUpdatedAt: string | null;
  cacheVersion: string | null;
}

export function createReportSnapshotKey(input: ReportSnapshotKeyInput): string {
  return JSON.stringify({
    accountIdentity: input.accountIdentity,
    tab: input.tab,
    period: input.period ?? "",
    startDate: input.startDate,
    endDate: input.endDate,
    compareStartDate: input.compareStartDate,
    compareEndDate: input.compareEndDate,
    compareMode: input.compareMode,
    branchCodes: [...input.branchCodes].sort(),
    scopeVersion: input.scopeVersion ?? null,
    supplierKind: input.supplierKind ?? null,
    supplierCode: input.supplierCode ?? null,
    search: input.search ?? "",
    page: input.page ?? null,
    pageSize: input.pageSize ?? null,
    detail: input.detail ?? null,
  });
}

export const MAX_REPORT_SNAPSHOTS = 8;

export function isReportScopeValid(
  accountIdentity: string,
  scope: { isSuccess: boolean; isError: boolean },
  branchCodes: readonly string[],
): boolean {
  return accountIdentity.trim().length > 0 && scope.isSuccess && !scope.isError && branchCodes.length > 0;
}

export function getCompleteReportSnapshot<T>(
  cache: Map<string, CompleteReportSnapshot<T>>,
  key: string,
): CompleteReportSnapshot<T> | undefined {
  const snapshot = cache.get(key);
  if (snapshot) {
    cache.delete(key);
    cache.set(key, snapshot);
  }
  return snapshot;
}

export function saveCompleteReportSnapshot<T>(
  cache: Map<string, CompleteReportSnapshot<T>>,
  key: string,
  data: T,
  metadata: { statisticUpdatedAt?: string | null; cacheVersion?: string | null } = {},
  storedAt: number = Date.now(),
): void {
  if (!Number.isFinite(storedAt)) return;
  cache.delete(key);
  cache.set(key, {
    data,
    storedAt,
    statisticUpdatedAt: metadata.statisticUpdatedAt ?? null,
    cacheVersion: metadata.cacheVersion ?? null,
  });
  // 每个页面只保留最近使用的少量条件，避免长会话筛选和翻页持续占用内存。
  while (cache.size > MAX_REPORT_SNAPSHOTS) cache.delete(cache.keys().next().value!);
}

export function getReportSnapshotDisplay<TLive, TDisplay>(
  live: { data?: TLive; isFetching: boolean; isError: boolean },
  cached?: CompleteReportSnapshot<TDisplay>,
  mapComplete: (data: TLive) => TDisplay | undefined = (data) => data as unknown as TDisplay,
  scopeValid = true,
): TDisplay | undefined {
  if (!scopeValid) return undefined;
  if (live.data !== undefined && !live.isFetching && !live.isError) {
    const display = mapComplete(live.data);
    if (display !== undefined) return display;
  }
  return cached?.data;
}

export function formatReportSnapshotTime(value: string | null, locale = "en-AU"): string | null {
  if (!value) return null;
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) return null;
  return new Date(timestamp).toLocaleString(locale, {
    dateStyle: "short",
    timeStyle: "short",
  });
}
