const ROOT = "priceUpdates";

/** 不传参数返回前缀键，用于一次性失效/重置所有分店与筛选条件下的任务列表。 */
export function priceUpdateTasksQueryKey(
  storeCode?: string | null,
  tab?: string,
  kind?: string,
  hqSyncFailedOnly?: boolean
) {
  return storeCode === undefined
    ? ([ROOT, "tasks"] as const)
    : ([ROOT, "tasks", storeCode, tab, kind, Boolean(hqSyncFailedOnly)] as const);
}

/** 工作台角标与列表页共用；mutation 成功后按前缀失效。 */
export function priceUpdateCountQueryKey(storeCode?: string | null) {
  return storeCode === undefined ? ([ROOT, "count"] as const) : ([ROOT, "count", storeCode] as const);
}
