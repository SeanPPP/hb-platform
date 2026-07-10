import assert from "node:assert/strict";
import { canNavigateToMonth, isMonthDateInRange } from "./month-date-picker-range";

assert.equal(isMonthDateInRange("2025-07-11", "2025-07-11", "2026-07-11"), true);
assert.equal(isMonthDateInRange("2025-07-10", "2025-07-11", "2026-07-11"), false);
assert.equal(isMonthDateInRange("2026-07-12", "2025-07-11", "2026-07-11"), false);
assert.equal(canNavigateToMonth(new Date(2025, 6, 1), "2025-07-11", "2026-07-11"), true);
assert.equal(canNavigateToMonth(new Date(2025, 5, 1), "2025-07-11", "2026-07-11"), false);
assert.equal(canNavigateToMonth(new Date(2026, 7, 1), "2025-07-11", "2026-07-11"), false);
