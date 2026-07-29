/** 已激活本地目录的稳定摘要；不暴露校验和、远端响应或 SQLite 行。 */
export type CatalogSummary = Readonly<{
  snapshotId: string;
  catalogVersion: string;
  itemCount: number;
  activatedAt: string;
}>;

/** 目录刷新只能按这个顺序推进；百分比代表已完成的真实持久化或校验事实。 */
export type CatalogRefreshStep =
  | "prepare"
  | "products"
  | "promotions"
  | "activate";

export type CatalogRefreshProgressEvent = Readonly<{
  step: CatalogRefreshStep;
  percent: number;
  completedItemCount?: number;
  totalItemCount?: number;
}>;

export type CatalogRefreshProgressObserver = (
  event: CatalogRefreshProgressEvent,
) => void;

/** 激活已提交后，目录仍可用；后续本地运行时重载失败只作为可见告警。 */
export type CatalogRefreshOutcome =
  | Readonly<{
      kind: "complete";
      summary: CatalogSummary;
    }>
  | Readonly<{
      kind: "activated-with-warning";
      summary: CatalogSummary;
      warningCode:
        | "catalog-runtime-reload-failed"
        | "catalog-activation-verification-failed";
    }>;
