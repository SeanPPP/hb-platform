import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const directory = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(directory, "product-report-screen.tsx"), "utf8");
const pickerStart = source.indexOf("function StorePickerModal(");
const pickerEnd = source.indexOf("function BranchDrilldownModal(", pickerStart);

assert.ok(pickerStart >= 0, "商品报告必须保留分店选择弹层");
assert.ok(pickerEnd > pickerStart, "必须能够隔离分店选择弹层源码");

const pickerSource = source.slice(pickerStart, pickerEnd);

assert.match(pickerSource, /<ScrollView/);
assert.match(pickerSource, /nestedScrollEnabled/);
assert.match(pickerSource, /showsVerticalScrollIndicator/);
assert.match(pickerSource, /keyboardShouldPersistTaps="handled"/);
assert.match(pickerSource, /\{labelAll\}/);
assert.match(pickerSource, /options\.map/);
assert.match(pickerSource, /style=\{styles\.storeModalList\}/);
assert.match(pickerSource, /contentContainerStyle=\{styles\.storeModalListContent\}/);

console.log("product-report-screen-contract.test.ts: ok");
