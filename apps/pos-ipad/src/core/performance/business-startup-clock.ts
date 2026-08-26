import { businessStartupOrigin } from "./business-startup-origin";
import {
  clientMetrics,
  createBusinessStartupTimer,
} from "./client-metrics";

function monotonicNow(): number {
  return typeof performance === "undefined"
    ? Date.now()
    : performance.now();
}

/** 从 JS 入口加载开始，直到业务 runtime 完成初始化。 */
export const businessStartupClock = createBusinessStartupTimer({
  startedAt: businessStartupOrigin,
  now: monotonicNow,
  record: (draft) => clientMetrics.record(draft),
});
