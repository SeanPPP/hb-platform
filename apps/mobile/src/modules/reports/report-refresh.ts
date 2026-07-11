export type ReportTab = "revenue" | "product";

// 复用正在进行的刷新，避免页头与下拉同时触发时取消并重开请求。
export const REPORT_REFETCH_OPTIONS = { cancelRefetch: false } as const;

export function getReportRefreshQueryOptions(tab: ReportTab) {
  const queryKey = tab === "revenue" ? ["reports"] as const : ["product-report"] as const;
  return {
    queryKey,
    type: "active" as const,
    predicate: (query: { queryKey: readonly unknown[] }) => {
      if (tab === "revenue") {
        return query.queryKey[0] === "reports" && query.queryKey[1] !== "statistics-freshness";
      }
      return query.queryKey[0] === "product-report";
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
