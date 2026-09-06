export type ReportTab = "revenue" | "product";

// 复用正在进行的刷新，避免页头与下拉同时触发时取消并重开请求。
export const REPORT_REFETCH_OPTIONS = { cancelRefetch: false } as const;

export function getReportStoreScopeRefreshQueryOptions(tab: ReportTab) {
  return {
    queryKey: tab === "revenue"
      ? ["reports", "cashier-enabled-stores"] as const
      : ["product-report", "stores"] as const,
    // 账号身份是查询键的后缀；只重验当前页签仍活跃的账号范围。
    exact: false as const,
    type: "active" as const,
  };
}

export function getReportRefreshQueryOptions(tab: ReportTab) {
  const queryKey = tab === "revenue" ? ["reports"] as const : ["product-report"] as const;
  return {
    queryKey,
    type: "active" as const,
    predicate: (query: { queryKey: readonly unknown[] }) => {
      if (tab === "revenue") {
        return query.queryKey[0] === "reports"
          && query.queryKey[1] !== "statistics-freshness"
          && query.queryKey[1] !== "cashier-enabled-stores";
      }
      return query.queryKey[0] === "product-report" && query.queryKey[1] !== "stores";
    },
  };
}

export function createReportRefreshController(
  refreshReport: (tab: ReportTab) => Promise<unknown>,
  refreshFreshness: () => Promise<unknown>,
  onRefreshingChange: (refreshing: boolean) => void = () => undefined,
) {
  let refreshing = false;
  let disposed = false;
  return {
    isRefreshing: () => refreshing,
    dispose() {
      disposed = true;
    },
    resume() {
      disposed = false;
    },
    async refresh(tab: ReportTab) {
      if (disposed || refreshing) return;
      refreshing = true;
      onRefreshingChange(true);
      try {
        await Promise.all([refreshReport(tab), refreshFreshness()]);
      } finally {
        refreshing = false;
        if (!disposed) {
          onRefreshingChange(false);
        }
      }
    },
  };
}
