import assert from "node:assert/strict";
import {
  DEFAULT_REPORT_SORT,
  getReportSortKey,
  isDefaultReportSort,
  sortReportRows,
  toggleReportSort,
  type ReportSortValues,
} from "./sorting";

// 点表头：换列默认降序，同列在降序/升序之间来回切换。
assert.deepEqual(DEFAULT_REPORT_SORT, { field: "amount", order: "desc" });
assert.equal(isDefaultReportSort(DEFAULT_REPORT_SORT), true);
assert.equal(isDefaultReportSort({ field: "amount", order: "asc" }), false);
assert.deepEqual(toggleReportSort(DEFAULT_REPORT_SORT, "quantity"), { field: "quantity", order: "desc" });
assert.deepEqual(toggleReportSort({ field: "quantity", order: "desc" }, "quantity"), { field: "quantity", order: "asc" });
assert.deepEqual(toggleReportSort({ field: "quantity", order: "asc" }, "quantity"), { field: "quantity", order: "desc" });
assert.deepEqual(toggleReportSort({ field: "quantity", order: "asc" }, "unitPrice"), { field: "unitPrice", order: "desc" });
assert.equal(getReportSortKey({ field: "unitPrice", order: "asc" }), "unitPrice:asc");

interface Row {
  code: string;
  amount: number;
  compareAmount: number;
  quantity: number;
  compareQuantity: number | null;
  averagePrice: number | null;
  compareAveragePrice: number | null;
}

const values: ReportSortValues<Row> = {
  amount: (row) => [row.amount, row.compareAmount],
  quantity: (row) => [row.quantity, row.compareQuantity],
  unitPrice: (row) => [row.averagePrice, row.compareAveragePrice],
};

const rows: Row[] = [
  { code: "B", amount: 90, compareAmount: 0, quantity: 30, compareQuantity: null, averagePrice: 3, compareAveragePrice: null },
  { code: "A", amount: 100, compareAmount: 50, quantity: 10, compareQuantity: 5, averagePrice: 10, compareAveragePrice: 10 },
  { code: "D", amount: 39, compareAmount: 36, quantity: 5, compareQuantity: 3, averagePrice: 7.8, compareAveragePrice: 12 },
  { code: "E", amount: 39, compareAmount: 0, quantity: 5, compareQuantity: 0, averagePrice: 7.8, compareAveragePrice: null },
  { code: "G", amount: 0, compareAmount: 80, quantity: 0, compareQuantity: 4, averagePrice: null, compareAveragePrice: 20 },
  { code: "C", amount: 39, compareAmount: 36, quantity: 5, compareQuantity: 3, averagePrice: 7.8, compareAveragePrice: 12 },
];
const snapshot = rows.map((row) => row.code);
const codes = (sorted: Row[]) => sorted.map((row) => row.code);
const byCode = (row: Row) => row.code;

// 本期值相同时比同期值，再相同按代码升序兜底（C、D 完全相同）。
assert.deepEqual(codes(sortReportRows(rows, DEFAULT_REPORT_SORT, values, byCode)), ["A", "B", "C", "D", "E", "G"]);
assert.deepEqual(codes(sortReportRows(rows, { field: "amount", order: "asc" }, values, byCode)), ["G", "E", "C", "D", "B", "A"]);
assert.deepEqual(codes(sortReportRows(rows, { field: "quantity", order: "desc" }, values, byCode)), ["B", "A", "C", "D", "E", "G"]);
// 同期数量为 null 的 B 不影响本期排序；E 的同期 0 小于 C/D 的 3。
assert.deepEqual(codes(sortReportRows(rows, { field: "quantity", order: "asc" }, values, byCode)), ["G", "E", "C", "D", "A", "B"]);
// 均价为 null 的 G 在降序和升序下都排最后；同期均价 null 的 E 排在同期有值的 C/D 之后。
assert.deepEqual(codes(sortReportRows(rows, { field: "unitPrice", order: "desc" }, values, byCode)), ["A", "C", "D", "E", "B", "G"]);
assert.deepEqual(codes(sortReportRows(rows, { field: "unitPrice", order: "asc" }, values, byCode)), ["B", "C", "D", "E", "A", "G"]);

// 返回新数组，不修改入参。
assert.deepEqual(rows.map((row) => row.code), snapshot);
assert.notEqual(sortReportRows(rows, DEFAULT_REPORT_SORT, values, byCode), rows);
assert.deepEqual(sortReportRows([], DEFAULT_REPORT_SORT, values, byCode), []);
