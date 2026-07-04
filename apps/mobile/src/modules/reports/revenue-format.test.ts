import assert from "node:assert/strict";
import {
  formatMoney,
  formatRatio,
  formatSignedMoney,
  getDeltaIntent,
  getIntentColor,
} from "./format";

assert.equal(formatMoney(1234.5), "$1,234.50");
assert.equal(formatMoney(undefined), "$0.00");
assert.equal(formatSignedMoney(12.3), "+$12.30");
assert.equal(formatSignedMoney(-12.3), "-$12.30");
assert.equal(formatSignedMoney(0), "$0.00");
assert.equal(formatRatio(0.125), "+12.5%");
assert.equal(formatRatio(-12.345), "-12.3%");
assert.equal(formatRatio(undefined), "--");
assert.equal(getDeltaIntent(1), "positive");
assert.equal(getDeltaIntent(-1), "negative");
assert.equal(getDeltaIntent(0), "neutral");
assert.equal(getDeltaIntent(100, null), "neutral");
assert.equal(getIntentColor("positive"), "#0F8A5F");
