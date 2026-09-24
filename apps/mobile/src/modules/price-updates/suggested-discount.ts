export type SuggestedDiscountInput =
  | { ok: true; rate: number | null }
  | { ok: false };

/**
 * 输入为百分比 0~100，存储为减免比例 0~1。
 * 留空 = 未设置（不比较分店折扣）；0 = 明确无折扣，两者语义不同，不能互相折算。
 */
export function parseSuggestedDiscountInput(text: string): SuggestedDiscountInput {
  const trimmed = text.trim();
  if (!trimmed) {
    return { ok: true, rate: null };
  }
  const percent = Number(trimmed);
  if (!Number.isFinite(percent) || percent < 0 || percent > 100) {
    return { ok: false };
  }
  return { ok: true, rate: Math.round(percent * 100) / 10000 };
}

export function formatSuggestedDiscountInput(rate: number | null | undefined): string {
  if (rate == null || !Number.isFinite(rate)) {
    return "";
  }
  // 先放大再取整，避免 0.07 * 100 = 7.000000000000001 这类浮点尾数进入输入框。
  return String(Math.round(rate * 10000) / 100);
}

export function isSameSuggestedDiscount(left: number | null | undefined, right: number | null | undefined) {
  if (left == null || right == null) {
    return left == null && right == null;
  }
  return Math.abs(left - right) <= 0.00005;
}
