import type {
  WarehouseInsightRange,
  WarehouseInsightRangePreset,
  WarehouseInsightRangeValidation,
} from "./types";

/** 区间上限与后端 WarehouseProductInsightRules.MaxRangeDays 一致，含首尾计算。 */
export const WAREHOUSE_INSIGHT_MAX_RANGE_DAYS = 400;
export const WAREHOUSE_INSIGHT_DEFAULT_RANGE_DAYS = 90;
export const WAREHOUSE_INSIGHT_RANGE_PRESETS: WarehouseInsightRangePreset[] = [
  7, 30, 90, 180, 365,
];

const DAY_MS = 86_400_000;

/** 只接受严格的 YYYY-MM-DD，且日期本身必须存在（排除 2026-02-30 这类输入）。 */
export function parseInsightDate(value: string): number | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  const timestamp = Date.parse(`${value}T00:00:00Z`);
  if (!Number.isFinite(timestamp)) return null;
  return new Date(timestamp).toISOString().slice(0, 10) === value
    ? timestamp
    : null;
}

export function formatInsightDate(timestamp: number): string {
  return new Date(timestamp).toISOString().slice(0, 10);
}

export function shiftInsightDate(value: string, days: number): string {
  const timestamp = parseInsightDate(value);
  if (timestamp == null) return value;
  return formatInsightDate(timestamp + days * DAY_MS);
}

/** 天数含首尾：同一天为 1 天。 */
export function countInsightRangeDays(range: WarehouseInsightRange): number {
  const start = parseInsightDate(range.startDate);
  const end = parseInsightDate(range.endDate);
  if (start == null || end == null) return 0;
  return Math.round((end - start) / DAY_MS) + 1;
}

export function validateWarehouseInsightRange(
  range: WarehouseInsightRange,
): WarehouseInsightRangeValidation {
  const start = parseInsightDate(range.startDate);
  const end = parseInsightDate(range.endDate);
  if (start == null || end == null) {
    return { ok: false, dayCount: 0, reason: "format" };
  }
  if (start > end) {
    return { ok: false, dayCount: 0, reason: "order" };
  }
  const dayCount = Math.round((end - start) / DAY_MS) + 1;
  if (dayCount > WAREHOUSE_INSIGHT_MAX_RANGE_DAYS) {
    return {
      ok: false,
      dayCount,
      reason: "tooLong",
      // 超限时以结束日为锚点收敛，保留用户真正关心的近期数据。
      clampedStartDate: formatInsightDate(
        end - (WAREHOUSE_INSIGHT_MAX_RANGE_DAYS - 1) * DAY_MS,
      ),
    };
  }
  return { ok: true, dayCount };
}

/** 预设区间按含首尾计算：近 90 天 = 结束日往前 89 天。 */
export function buildInsightPresetRange(
  endDate: string,
  days: number,
): WarehouseInsightRange {
  return { startDate: shiftInsightDate(endDate, -(days - 1)), endDate };
}

export function matchInsightRangePreset(
  range: WarehouseInsightRange,
): WarehouseInsightRangePreset | null {
  const dayCount = countInsightRangeDays(range);
  return (
    WAREHOUSE_INSIGHT_RANGE_PRESETS.find((preset) => preset === dayCount) ??
    null
  );
}

export function canViewWarehouseProductInsights(
  isAuthenticated: boolean,
  hasPermission: (permission: string) => boolean,
  isReview: boolean,
) {
  return (
    isAuthenticated && !isReview && hasPermission("SalesDashboard.WarehouseFlow.View")
  );
}

/** 售罄率转百分比展示值；未发货时返回 null 而不是 0。 */
export function formatSellThroughRate(rate: number | null): string | null {
  return rate == null ? null : `${Math.round(rate * 100)}%`;
}

/**
 * 进货 → 发货 → 售出 的流向分段占比。
 * 以进货为分母；进货为 0 时退回以发货为分母，避免整条进度条消失。
 */
export function buildInsightFlowSegments(totals: {
  inboundQuantity: number;
  shippedQuantity: number;
  salesQuantity: number;
}) {
  const base = totals.inboundQuantity > 0
    ? totals.inboundQuantity
    : Math.max(totals.shippedQuantity, totals.salesQuantity);
  if (base <= 0) return { sold: 0, shippedUnsold: 0, remaining: 0 };
  const sold = Math.min(totals.salesQuantity, base) / base;
  // 发货可能包含更早进货的库存，分段之和必须封顶在 100%，否则界面会出现 104% 这类占比。
  const shippedUnsold = Math.min(
    Math.max(totals.shippedQuantity - totals.salesQuantity, 0) / base,
    1 - sold,
  );
  return {
    sold,
    shippedUnsold,
    remaining: Math.max(1 - sold - shippedUnsold, 0),
  };
}
