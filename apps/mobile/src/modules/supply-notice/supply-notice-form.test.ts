import assert from "node:assert/strict";
import { EMPTY_SUPPLY_NOTICE_DRAFT, buildSupplyNoticeInput } from "./supply-notice-draft";

// 后续计划必选。
assert.deepEqual(buildSupplyNoticeInput(EMPTY_SUPPLY_NOTICE_DRAFT), { errorKey: "form.planRequired" });
// 选了日期精度却没选日期。
assert.deepEqual(
  buildSupplyNoticeInput({ ...EMPTY_SUPPLY_NOTICE_DRAFT, supplyPlan: "WillRestock", expectedPrecision: "Day" }),
  { errorKey: "form.dateInvalid" },
);
// 某月：把所选日期原样交给后端展开为整月。
assert.deepEqual(
  buildSupplyNoticeInput({ ...EMPTY_SUPPLY_NOTICE_DRAFT, supplyPlan: "Seasonal", expectedPrecision: "Month", expectedDate: "2026-09-17", storeFacingNote: " 圣诞季 " }),
  { input: { supplyPlan: "Seasonal", expectedPrecision: "Month", expectedFrom: "2026-09-17", storeFacingNote: "圣诞季", internalNote: null } },
);
// 不再供应：忽略日期。
assert.deepEqual(
  buildSupplyNoticeInput({ ...EMPTY_SUPPLY_NOTICE_DRAFT, supplyPlan: "Discontinued", expectedPrecision: "Day", expectedDate: "2026-10-05" }),
  { input: { supplyPlan: "Discontinued", expectedPrecision: "Unknown", expectedFrom: null, storeFacingNote: null, internalNote: null } },
);
console.log("supply-notice-form.test: ok");
