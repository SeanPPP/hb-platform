import assert from "node:assert/strict";
import { PRODUCT_PAGE_SIZE, SUPPLIER_PAGE_SIZE, getPageRows } from "./pagination";

assert.equal(SUPPLIER_PAGE_SIZE, 20);
assert.equal(PRODUCT_PAGE_SIZE, 20);
assert.deepEqual(getPageRows([1, 2, 3], 2, 2), [3]);
