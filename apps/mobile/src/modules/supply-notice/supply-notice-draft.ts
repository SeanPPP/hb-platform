import type { SupplyExpectedPrecision, SupplyNoticeInput, SupplyPlan } from "./types";

/** 移动端只提供“待定 / 具体日期 / 某月”三档；日期范围在网页端录入。 */
export type MobileSupplyPrecision = Extract<SupplyExpectedPrecision, "Unknown" | "Day" | "Month">;

export interface SupplyNoticeDraft {
  supplyPlan: SupplyPlan | null;
  expectedPrecision: MobileSupplyPrecision;
  /** YYYY-MM-DD；某月时取所选日期所在月，后端会展开为整月。 */
  expectedDate: string;
  storeFacingNote: string;
  internalNote: string;
}

export const EMPTY_SUPPLY_NOTICE_DRAFT: SupplyNoticeDraft = {
  supplyPlan: null,
  expectedPrecision: "Unknown",
  expectedDate: "",
  storeFacingNote: "",
  internalNote: "",
};

export const SUPPLY_PLANS: SupplyPlan[] = ["WillRestock", "Undecided", "Seasonal", "Discontinued"];
export const MOBILE_SUPPLY_PRECISIONS: MobileSupplyPrecision[] = ["Unknown", "Day", "Month"];

/** 把草稿转成请求；后续计划必选，选了日期精度却没选日期也拒绝。返回错误文案键（supplyNotice 命名空间）。 */
export function buildSupplyNoticeInput(draft: SupplyNoticeDraft): { input: SupplyNoticeInput } | { errorKey: string } {
  if (!draft.supplyPlan) {
    return { errorKey: "form.planRequired" };
  }
  const discontinued = draft.supplyPlan === "Discontinued";
  const precision: SupplyExpectedPrecision = discontinued ? "Unknown" : draft.expectedPrecision;
  const date = draft.expectedDate.trim();
  if (precision !== "Unknown" && !/^\d{4}-\d{2}-\d{2}$/.test(date)) {
    return { errorKey: "form.dateInvalid" };
  }
  return {
    input: {
      supplyPlan: draft.supplyPlan,
      expectedPrecision: precision,
      expectedFrom: precision === "Unknown" ? null : date,
      storeFacingNote: draft.storeFacingNote.trim() || null,
      internalNote: draft.internalNote.trim() || null,
    },
  };
}
