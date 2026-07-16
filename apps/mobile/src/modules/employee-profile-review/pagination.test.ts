import assert from "node:assert/strict";
import { mergeUniqueEmployeeProfileReviewPages } from "./pagination";

const pageOneItem = { requestId: 3 };
const duplicate = { requestId: 2 };
const merged = mergeUniqueEmployeeProfileReviewPages([
  { items: [pageOneItem, duplicate] },
  { items: [{ requestId: 2 }, { requestId: 1 }] },
]);
assert.deepEqual(merged.map((item) => item.requestId), [3, 2, 1]);
assert.equal(merged[0], pageOneItem);
assert.equal(merged[1], duplicate);
