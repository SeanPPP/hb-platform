import assert from "node:assert/strict";
import { formatSupplyExpected } from "./format-expected";

const t = (key: string, params?: Record<string, unknown>) => (params ? `${key}:${JSON.stringify(params)}` : key);
const year = 2026;

assert.equal(formatSupplyExpected({ expectedFrom: null, expectedTo: null, expectedPrecision: "Unknown", isOverdue: false }, t, year), "expectedUnknown");
assert.equal(formatSupplyExpected({ expectedFrom: "2026-10-05", expectedTo: "2026-10-05", expectedPrecision: "Day", isOverdue: false }, t, year), "10月5日");
assert.equal(formatSupplyExpected({ expectedFrom: "2027-10-05", expectedTo: "2027-10-05", expectedPrecision: "Day", isOverdue: false }, t, year), "2027年10月5日");
assert.equal(formatSupplyExpected({ expectedFrom: "2026-10-05", expectedTo: "2026-10-10", expectedPrecision: "Range", isOverdue: false }, t, year), 'expectedRange:{"from":"10月5日","to":"10月10日"}');
assert.equal(formatSupplyExpected({ expectedFrom: "2026-10-01", expectedTo: "2026-10-31", expectedPrecision: "Month", isOverdue: false }, t, year), 'expectedMonth:{"month":10}');
assert.equal(formatSupplyExpected({ expectedFrom: "2027-09-01", expectedTo: "2027-09-30", expectedPrecision: "Month", isOverdue: false }, t, year), 'expectedMonthWithYear:{"year":2027,"month":9}');
// 逾期优先：不再把过期日期当承诺展示。
assert.equal(formatSupplyExpected({ expectedFrom: "2020-01-01", expectedTo: "2020-01-01", expectedPrecision: "Day", isOverdue: true }, t, year), "expectedOverdue");
// 后端只给到日期，带时间的 ISO 串也要能解析。
assert.equal(formatSupplyExpected({ expectedFrom: "2026-10-05T00:00:00", expectedTo: "2026-10-05T00:00:00", expectedPrecision: "Day", isOverdue: false }, t, year), "10月5日");

console.log("format-expected.test: ok");
