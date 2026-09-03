import { reportApplicationLog } from "@/shared/logging/log-center-runtime";
import type { ApplicationLogInput } from "@/shared/logging/log-center";

export const REPORT_FIRST_DATA_BUDGET_MS = {
  cold: 2_000,
  warm: 500,
} as const;

/** 点击后超过该时间仍未启动对应报告查询，则不再把标记归因到后续会话。 */
export const REPORT_NAVIGATION_MARKER_TTL_MS = 10_000;

export type ReportLoadCacheState = keyof typeof REPORT_FIRST_DATA_BUDGET_MS;

export type ReportNavigationMarkerKind = "revenue" | "product";

export type ReportLoadPerformanceReport =
  | "revenue"
  | "revenue-hourly"
  | "revenue-daily"
  | "product"
  | "supplier-branches"
  | "product-branches";

export type ReportLoadPerformanceMeasurement = Readonly<{
  cacheState: ReportLoadCacheState;
  navigationMs: number;
  requestMs: number;
  normalizeRenderMs: number;
  totalMs: number;
  budgetMs: number;
  meetsFirstDataBudget: boolean;
}>;

export type ReportLoadPerformanceEvent = ReportLoadPerformanceMeasurement & Readonly<{
  event: "report_first_data";
  report: ReportLoadPerformanceReport;
}>;

type ReportLoadPerformanceLogger = (event: ReportLoadPerformanceEvent) => void;
type ReportLoadPerformanceReporter = (input: ApplicationLogInput) => void;

type ActiveReportLoad = Readonly<{
  requestStartedAt: number;
  navigationStartedAt?: number;
  navigationReport?: ReportNavigationMarkerKind;
  cacheState: ReportLoadCacheState;
  normalizedAt?: number;
}>;

type ReportNavigationMarker = Readonly<{
  startedAt: number;
  actionId: number;
}>;

const reportNavigationMarkers = new Map<ReportNavigationMarkerKind, ReportNavigationMarker>();
let nextReportNavigationActionId = 0;

/** 在确认将要导航或切换报表后记录单调时钟起点。相同报表的新点击只替换未消费标记。 */
export function markReportNavigationStart(
  report: ReportNavigationMarkerKind,
  startedAt: number = monotonicNow(),
): number | null {
  if (!Number.isFinite(startedAt)) return null;
  discardReportNavigationStart(report);
  const actionId = createReportNavigationActionId();
  reportNavigationMarkers.set(report, { startedAt, actionId });
  return actionId;
}

/**
 * 底栏进入报告中心时还不知道常驻页面当前激活的页签，因此为两个候选页签写入同组标记。
 * 任一活动页签消费后会整组删除，未激活页签不能把同一次点击留给后续查询。
 */
export function markReportHubNavigationStart(
  startedAt: number = monotonicNow(),
): number | null {
  if (!Number.isFinite(startedAt)) return null;
  discardReportNavigationStart("revenue");
  discardReportNavigationStart("product");
  const actionId = createReportNavigationActionId();
  const marker = { startedAt, actionId } as const;
  reportNavigationMarkers.set("revenue", marker);
  reportNavigationMarkers.set("product", marker);
  return actionId;
}

/** 返回仍有效的标记 token；无查询的焦点恢复可用它安全清理同一动作。 */
export function getPendingReportNavigationToken(
  report: ReportNavigationMarkerKind,
  checkedAt: number = monotonicNow(),
): number | null {
  const marker = reportNavigationMarkers.get(report);
  if (!marker) return null;
  const age = checkedAt - marker.startedAt;
  if (!Number.isFinite(checkedAt) || age < 0 || age > REPORT_NAVIGATION_MARKER_TTL_MS) {
    discardReportNavigationStart(report, marker.actionId);
    return null;
  }
  return marker.actionId;
}

export function hasPendingReportNavigationStart(
  report: ReportNavigationMarkerKind,
  checkedAt: number = monotonicNow(),
): boolean {
  return getPendingReportNavigationToken(report, checkedAt) !== null;
}

/**
 * 丢弃一个标记；同组底栏标记会一起删除。expectedActionId 可避免旧异步清理误删新点击。
 */
export function discardReportNavigationStart(
  report: ReportNavigationMarkerKind,
  expectedActionId?: number,
): void {
  const marker = reportNavigationMarkers.get(report);
  if (!marker || (expectedActionId !== undefined && marker.actionId !== expectedActionId)) return;
  for (const [kind, candidate] of reportNavigationMarkers) {
    if (candidate.actionId === marker.actionId) {
      reportNavigationMarkers.delete(kind);
    }
  }
}

/**
 * 标记按报表类型隔离且无论是否有效都只消费一次，避免过期点击污染后续进入页面的查询。
 */
export function consumeReportNavigationStart(
  report: ReportNavigationMarkerKind,
  consumedAt: number = monotonicNow(),
): number | null {
  const marker = reportNavigationMarkers.get(report);
  if (!marker) return null;
  discardReportNavigationStart(report, marker.actionId);

  const age = consumedAt - marker.startedAt;
  if (!Number.isFinite(consumedAt) || age < 0 || age > REPORT_NAVIGATION_MARKER_TTL_MS) {
    return null;
  }
  return marker.startedAt;
}

function createReportNavigationActionId(): number {
  nextReportNavigationActionId += 1;
  return nextReportNavigationActionId;
}

/**
 * 量化报表从请求开始到首条数据可见的本地耗时。
 * 该类不持有 React 状态、网络对象或业务数据，可安全放进 Screen 的 useRef。
 */
export class ReportLoadPerformanceTimer {
  private activeLoad: ActiveReportLoad | null = null;
  private navigationStartedAt: number | undefined;

  public constructor(
    private readonly now: () => number = monotonicNow,
  ) {}

  /**
   * 每次发起新的报表查询时调用；请求分段仍从本次 query 开始计算。
   * 顶层报表优先消费最新点击；没有新动作时，并发启动或失败重试只替换请求分段，
   * 并延续同一动作的导航起点。
   */
  public start(
    cacheState: ReportLoadCacheState,
    navigationReport?: ReportNavigationMarkerKind,
  ): void {
    const requestStartedAt = this.readNow();
    if (navigationReport) {
      // 每个真实 query 都先尝试认领最新动作；没有新动作时才延续失败重试的旧起点。
      const pendingNavigationStartedAt = consumeReportNavigationStart(navigationReport, requestStartedAt);
      if (pendingNavigationStartedAt !== null) {
        this.navigationStartedAt = pendingNavigationStartedAt;
      }
    }
    this.activeLoad = {
      requestStartedAt,
      navigationStartedAt: this.navigationStartedAt,
      navigationReport,
      cacheState,
    };
  }

  /** API 原始响应已解析、归一化并提交到页面可渲染数据后调用。 */
  public markDataNormalized(): void {
    let activeLoad = this.activeLoad;
    if (!activeLoad || activeLoad.normalizedAt !== undefined) return;
    const normalizedAt = this.readNow();
    activeLoad = this.claimPendingNavigationStart(activeLoad, normalizedAt);
    this.activeLoad = {
      ...activeLoad,
      normalizedAt,
    };
  }

  /**
   * 首个业务行实际可见时调用。只有完整、未取消的会话会产生一次测量结果；
   * 调用方仅在返回非 null 时记录或上报，避免重复完成被重复统计。
   */
  public markFirstRowVisible(): ReportLoadPerformanceMeasurement | null {
    let activeLoad = this.activeLoad;
    if (!activeLoad || activeLoad.normalizedAt === undefined) return null;

    const visibleAt = this.readNow();
    activeLoad = this.claimPendingNavigationStart(activeLoad, visibleAt);
    if (activeLoad.normalizedAt === undefined) return null;
    this.activeLoad = null;
    const navigationStartedAt = activeLoad.navigationStartedAt ?? activeLoad.requestStartedAt;
    const navigationMs = elapsed(navigationStartedAt, activeLoad.requestStartedAt);
    const requestMs = elapsed(activeLoad.requestStartedAt, activeLoad.normalizedAt);
    const normalizeRenderMs = elapsed(activeLoad.normalizedAt, visibleAt);
    const totalMs = elapsed(navigationStartedAt, visibleAt);
    const budgetMs = REPORT_FIRST_DATA_BUDGET_MS[activeLoad.cacheState];
    this.navigationStartedAt = undefined;

    return {
      cacheState: activeLoad.cacheState,
      navigationMs,
      requestMs,
      normalizeRenderMs,
      totalMs,
      budgetMs,
      meetsFirstDataBudget: totalMs <= budgetMs,
    };
  }

  /** 查询失败后清除会话，后续视图回调不能把失败请求误记成成功。 */
  public fail(): void {
    const activeLoad = this.activeLoad;
    if (activeLoad?.navigationReport) {
      // 失败也属于当前焦点动作：认领晚到 marker 并留给同 key Retry，避免残留或沿用旧焦点。
      const pendingNavigationStartedAt = consumeReportNavigationStart(
        activeLoad.navigationReport,
        this.readNow(),
      );
      if (pendingNavigationStartedAt !== null) {
        this.navigationStartedAt = pendingNavigationStartedAt;
      }
    }
    this.activeLoad = null;
  }

  /** 查询被取消或页面卸载后清除会话，避免过期回调产生成功计时。 */
  public cancel(): void {
    if (this.activeLoad?.navigationReport) {
      discardReportNavigationStart(this.activeLoad.navigationReport);
    }
    this.activeLoad = null;
    this.navigationStartedAt = undefined;
  }

  private readNow(): number {
    const value = this.now();
    return Number.isFinite(value) ? value : 0;
  }

  /**
   * React Query 在 cancelRefetch=false 时会复用同 key 的在途请求，不会再次进入 queryFn。
   * 若新用户操作发生在旧请求启动之后，就在该请求首次产出完整可见数据时认领最新标记，
   * 覆盖旧动作并把请求分段重定基准到本次操作，避免把操作前耗时算入当前样本。
   */
  private claimPendingNavigationStart(
    activeLoad: ActiveReportLoad,
    claimedAt: number,
  ): ActiveReportLoad {
    if (!activeLoad.navigationReport) return activeLoad;

    const navigationStartedAt = consumeReportNavigationStart(activeLoad.navigationReport, claimedAt);
    if (navigationStartedAt === null) return activeLoad;

    this.navigationStartedAt = navigationStartedAt;
    const requestStartedAt = Math.max(activeLoad.requestStartedAt, navigationStartedAt);
    return {
      ...activeLoad,
      requestStartedAt,
      navigationStartedAt,
      normalizedAt: activeLoad.normalizedAt === undefined
        ? undefined
        : Math.max(activeLoad.normalizedAt, requestStartedAt),
    };
  }
}

type ReportLoadVisibilityGateStartOptions = Readonly<{
  preserveVisibility?: boolean;
  preservePresentation?: boolean;
  restorePhysicalState?: Readonly<{
    firstRowVisible: boolean;
    presentationReady: boolean;
  }>;
}>;

/**
 * 弹窗首数据的三重门禁：数据已归一化、第一行位于视口、展示动画已结束。
 * 同 query key 刷新可保留后两项，避免依赖不会再次触发的 onLayout/viewability。
 */
export class ReportLoadVisibilityGate {
  private firstRowVisible = false;
  private presentationReady = false;
  private hasActiveLoad = false;
  private acceptsVisibilityEvents = false;

  public constructor(
    private readonly timer = new ReportLoadPerformanceTimer(),
  ) {}

  public start(
    cacheState: ReportLoadCacheState,
    options: ReportLoadVisibilityGateStartOptions = {},
  ): void {
    if (options.restorePhysicalState) {
      // 请求失败只结束计时会话；同 key 重试可从仍在屏幕上的物理状态恢复门禁。
      this.firstRowVisible = options.restorePhysicalState.firstRowVisible;
      this.presentationReady = options.restorePhysicalState.presentationReady;
    } else {
      if (!options.preserveVisibility) {
        this.firstRowVisible = false;
      }
      if (!options.preservePresentation) {
        this.presentationReady = false;
      }
    }
    this.timer.start(cacheState);
    this.hasActiveLoad = true;
    this.acceptsVisibilityEvents = true;
  }

  public markDataNormalized(): ReportLoadPerformanceMeasurement | null {
    if (!this.hasActiveLoad) return null;
    this.timer.markDataNormalized();
    return this.tryComplete();
  }

  public setFirstRowVisible(visible: boolean): ReportLoadPerformanceMeasurement | null {
    if (!this.acceptsVisibilityEvents) return null;
    this.firstRowVisible = visible;
    return this.tryComplete();
  }

  public setPresentationReady(ready: boolean): ReportLoadPerformanceMeasurement | null {
    if (!this.acceptsVisibilityEvents) return null;
    this.presentationReady = ready;
    return this.tryComplete();
  }

  public fail(): void {
    this.timer.fail();
    this.reset();
  }

  public cancel(): void {
    this.timer.cancel();
    this.reset();
  }

  private tryComplete(): ReportLoadPerformanceMeasurement | null {
    if (!this.hasActiveLoad || !this.firstRowVisible || !this.presentationReady) return null;
    const measurement = this.timer.markFirstRowVisible();
    if (measurement) {
      this.hasActiveLoad = false;
    }
    return measurement;
  }

  private reset(): void {
    this.firstRowVisible = false;
    this.presentationReady = false;
    this.hasActiveLoad = false;
    this.acceptsVisibilityEvents = false;
  }
}

/** 只有当前查询状态成功且缓存仍可直接展示时，才允许使用 warm 预算。 */
export function hasUsableSuccessfulReportCache<T>(
  status: string | undefined,
  data: T | undefined,
  isUsable: (data: T) => boolean,
): data is T {
  return status === "success" && data !== undefined && isUsable(data);
}

/**
 * 只记录耗时和报告标识，不包含门店、商品或用户数据，便于在真机日志核验 2 秒预算。
 */
export function recordReportLoadPerformance(
  report: ReportLoadPerformanceReport,
  measurement: ReportLoadPerformanceMeasurement,
  logger: ReportLoadPerformanceLogger = (event) => console.info("[report-performance]", event),
  reporter: ReportLoadPerformanceReporter = reportApplicationLog,
): ReportLoadPerformanceEvent {
  const event: ReportLoadPerformanceEvent = {
    event: "report_first_data",
    report,
    ...measurement,
  };
  logger(event);
  reporter(buildReportLoadApplicationLog(event));
  return event;
}

/**
 * 性能日志只携带固定白名单字段；空账号字段会覆盖日志中心的默认用户上下文，
 * 避免性能样本包含账号、门店、商品或查询日期。
 */
export function buildReportLoadApplicationLog(
  event: ReportLoadPerformanceEvent,
): ApplicationLogInput {
  return {
    level: event.meetsFirstDataBudget ? "Information" : "Warning",
    message: "报表首条数据可见耗时",
    sourceType: "mobile.report.performance",
    category: "ReportPerformance",
    userId: "",
    userName: "",
    properties: event,
  };
}

function elapsed(startedAt: number, finishedAt: number): number {
  return Math.max(0, finishedAt - startedAt);
}

function monotonicNow(): number {
  return typeof performance === "undefined"
    ? Date.now()
    : performance.now();
}
