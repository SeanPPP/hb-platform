import {
  clearInvoiceDateRange,
  formatInvoiceDateRangeDisplay,
  selectInvoiceDateRange,
  toInvoiceOrderDateFilters,
} from "./date-range";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}: expected ${expectedText}, got ${actualText}`);
  }
}

assertDeepEqual(
  formatInvoiceDateRangeDisplay({}, { allDatesLabel: "全部日期" }),
  { kind: "all", text: "全部日期" },
  "empty range uses caller supplied all dates label"
);

assertDeepEqual(
  formatInvoiceDateRangeDisplay({ from: "2026-05-01" }),
  { kind: "from-only", text: "2026-05-01 起" },
  "from-only range formats lower bound text"
);

assertDeepEqual(
  formatInvoiceDateRangeDisplay(
    { from: "2026-05-01" },
    { formatFrom: (date) => `From ${date}` }
  ),
  { kind: "from-only", text: "From 2026-05-01" },
  "from-only range can use caller supplied localized text"
);

assertDeepEqual(
  formatInvoiceDateRangeDisplay({ to: "2026-05-31" }),
  { kind: "to-only", text: "截至 2026-05-31" },
  "to-only range formats upper bound text"
);

assertDeepEqual(
  formatInvoiceDateRangeDisplay(
    { to: "2026-05-31" },
    { formatTo: (date) => `Until ${date}` }
  ),
  { kind: "to-only", text: "Until 2026-05-31" },
  "to-only range can use caller supplied localized text"
);

assertDeepEqual(
  formatInvoiceDateRangeDisplay({ from: "2026-05-01", to: "2026-05-31" }),
  { kind: "range", text: "2026-05-01 ~ 2026-05-31" },
  "full range formats both dates"
);

assertDeepEqual(
  selectInvoiceDateRange({}, "2026-05-10"),
  { from: "2026-05-10" },
  "first date selection starts range at from"
);

assertDeepEqual(
  selectInvoiceDateRange({ from: "2026-05-10" }, "2026-05-20"),
  { from: "2026-05-10", to: "2026-05-20" },
  "second later date fills to"
);

assertDeepEqual(
  selectInvoiceDateRange({ from: "2026-05-20" }, "2026-05-10"),
  { from: "2026-05-10", to: "2026-05-20" },
  "second earlier date swaps range order automatically"
);

assertDeepEqual(
  selectInvoiceDateRange({ from: "2026-05-01", to: "2026-05-31" }, "2026-06-05"),
  { from: "2026-06-05" },
  "new selection after a full range starts over"
);

assertDeepEqual(
  clearInvoiceDateRange(),
  {},
  "clearing range resets both from and to"
);

assertDeepEqual(
  toInvoiceOrderDateFilters({ from: "2026-05-01", to: "2026-05-31" }),
  { orderDateFrom: "2026-05-01", orderDateTo: "2026-05-31" },
  "helper preserves existing invoice filter wire shape"
);

assertEqual(
  Object.prototype.hasOwnProperty.call(toInvoiceOrderDateFilters({}), "orderDateFrom"),
  false,
  "empty helper mapping omits orderDateFrom"
);

assertEqual(
  Object.prototype.hasOwnProperty.call(toInvoiceOrderDateFilters({}), "orderDateTo"),
  false,
  "empty helper mapping omits orderDateTo"
);
