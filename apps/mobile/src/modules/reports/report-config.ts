// 日期报表可能覆盖较长区间，统一给予一分钟完成现场聚合查询。
export const REPORT_QUERY_TIMEOUT_MS = 60_000;

// 报表查询统一关闭自动重试，避免一次慢查询被重复放大。
export const REPORT_QUERY_OPTIONS = { retry: false } as const;
