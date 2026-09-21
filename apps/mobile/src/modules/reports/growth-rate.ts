export type GrowthTone = "up" | "down" | "flat";

export const GROWTH_COLORS: Record<GrowthTone, string> = {
  // 增长率是 11–12px 小字：#16A34A 在白底上只有 3.3:1，调深到 #15803D（5.0:1）。
  up: "#15803D",
  down: "#DC2626",
  flat: "#6B7280",
};

function isFiniteNumber(value: number) {
  return Number.isFinite(value);
}

export function getGrowthTone(current: number, compare: number): GrowthTone {
  if (!isFiniteNumber(current) || !isFiniteNumber(compare) || current === compare) {
    return "flat";
  }
  return current > compare ? "up" : "down";
}

export function formatGrowthRate(current: number, compare: number, newLabel: string) {
  if (!isFiniteNumber(current) || !isFiniteNumber(compare)) {
    return "--";
  }

  // 同期为 0 时不用无穷百分比，直接标记新增。
  if (compare === 0) {
    return current > 0 ? newLabel : "--";
  }

  const rate = (current - compare) / compare;
  const sign = rate > 0 ? "+" : "";
  return `${sign}${(rate * 100).toFixed(1)}%`;
}
