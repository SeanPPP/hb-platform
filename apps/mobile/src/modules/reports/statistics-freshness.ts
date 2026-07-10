import { useQuery } from "@tanstack/react-query";

export type StatisticsRunStatus = "Success" | "Failed" | "Running";

export interface StatisticsFreshness {
  lastSuccessfulAtUtc: string | null;
  latestRunStatus: StatisticsRunStatus | null;
}

export const STATISTICS_FRESHNESS_QUERY_KEY = ["reports", "statistics-freshness"] as const;

export function normalizeStatisticsFreshness(payload: unknown): StatisticsFreshness {
  const value = payload && typeof payload === "object" ? payload as Record<string, unknown> : {};
  const status = value.latestRunStatus;
  return {
    lastSuccessfulAtUtc: typeof value.lastSuccessfulAtUtc === "string" ? value.lastSuccessfulAtUtc : null,
    latestRunStatus: status === "Success" || status === "Failed" || status === "Running" ? status : null,
  };
}

export function formatStatisticsFreshnessTime(value: string | null) {
  if (!value) return null;
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return null;
  const pad = (part: number) => String(part).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function getStatisticsFreshnessRefetchInterval(
  freshness?: Pick<StatisticsFreshness, "latestRunStatus">,
) {
  // 任务运行期间短轮询，进入成功或失败终态后立即停止，避免常驻高频请求。
  return freshness?.latestRunStatus === "Running" ? 5_000 : false;
}

export async function fetchStatisticsFreshness() {
  const { apiClient } = await import("@/shared/api/client");
  const response = await apiClient.get("/react/v1/dashboard/statistics-freshness");
  return normalizeStatisticsFreshness(response.data);
}

export function useStatisticsFreshnessQuery() {
  return useQuery({
    queryKey: STATISTICS_FRESHNESS_QUERY_KEY,
    queryFn: fetchStatisticsFreshness,
    retry: false,
    refetchInterval: (query) => getStatisticsFreshnessRefetchInterval(query.state.data),
  });
}
