import { buildListParams } from "./api";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

const filtered = buildListParams({
  pageNumber: 2,
  pageSize: 20,
  keyword: "  front pda  ",
  storeCode: "1001",
  status: 1,
  deviceSystem: "  Android  ",
  deviceType: "  PDA  ",
});

assertEqual(filtered.page, 2, "keeps page number");
assertEqual(filtered.pageSize, 20, "keeps page size");
assertEqual(filtered.keyword, "front pda", "trims keyword");
assertEqual(filtered.status, 1, "keeps status");
assertEqual(filtered.deviceSystem, "Android", "trims device system");
assertEqual(filtered.deviceType, "PDA", "trims device type");

const empty = buildListParams({
  keyword: "   ",
  storeCode: "",
  deviceSystem: "",
  deviceType: null,
});

assertEqual(empty.keyword, undefined, "blank keyword is omitted");
assertEqual(empty.storeCode, undefined, "blank store code is omitted");
assertEqual(empty.deviceSystem, undefined, "blank device system is omitted");
assertEqual(empty.deviceType, undefined, "null device type is omitted");
