import assert from "node:assert/strict";
import {
  formatMoney,
  formatRatio,
  formatSignedMoney,
  formatSignedWholeDollars,
  formatWholeCount,
  formatWholeDollars,
  getDeltaIntent,
  getIntentColor,
} from "./format";

assert.equal(formatWholeDollars(47_825.6), "$47,826");
assert.equal(formatWholeDollars(Number.NaN), "—");
assert.equal(formatSignedWholeDollars(-181.4), "-$181");
assert.equal(formatSignedWholeDollars(1_968.5), "+$1,969");
assert.equal(formatSignedWholeDollars(-0.4), "$0", "四舍五入为 0 时不带符号");
assert.equal(formatSignedWholeDollars(undefined), "—");
assert.equal(formatWholeCount(1_234.4), "1,234");
assert.equal(formatWholeCount(null), "—");

assert.equal(formatMoney(1234.5), "$1,234.50");
assert.equal(formatMoney(undefined), "$0.00");
assert.equal(formatSignedMoney(12.3), "+$12.30");
assert.equal(formatSignedMoney(-12.3), "-$12.30");
assert.equal(formatSignedMoney(0), "$0.00");
assert.equal(formatRatio(0.125), "+12.5%");
assert.equal(formatRatio(1.5), "+150.0%");
assert.equal(formatRatio(-0.12345), "-12.3%");
assert.equal(formatRatio(undefined), "--");
assert.equal(getDeltaIntent(1), "positive");
assert.equal(getDeltaIntent(-1), "negative");
assert.equal(getDeltaIntent(0), "neutral");
assert.equal(getDeltaIntent(100, null), "positive");
assert.equal(getDeltaIntent(-100, null), "negative");
assert.equal(getIntentColor("positive"), "#0F8A5F");
