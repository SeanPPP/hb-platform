import assert from "node:assert/strict";
import { searchCreateSuppliers } from "./supplier-search";
const suppliers = [
  { supplierCode: "SP003", supplierName: "321" },
  { supplierCode: "SP0078", supplierName: "A PLUS" },
  { supplierCode: "200", supplierName: "Hotbargain" },
  { supplierCode: "SP002", supplierName: "12" },
];
assert.deepEqual(
  searchCreateSuppliers(suppliers, "  a plus ", "en").map(
    (x) => x.supplierCode,
  ),
  ["SP0078"],
);
assert.deepEqual(
  searchCreateSuppliers(suppliers, "sp00", "en").map((x) => x.supplierCode),
  ["SP002", "SP003", "SP0078"],
);
assert.equal(
  searchCreateSuppliers(suppliers, "200", "zh")[0].supplierName,
  "Hotbargain",
);
assert.equal(searchCreateSuppliers(suppliers, "不存在", "zh").length, 0);
assert.equal(searchCreateSuppliers(suppliers, " ", "en").length, 4);
assert.equal(
  suppliers[0].supplierCode,
  "SP003",
  "搜索排序不能修改原供应商数组",
);
console.log("supplier-search.test.ts: ok");
