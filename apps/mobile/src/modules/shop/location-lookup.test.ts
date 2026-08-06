import assert from "node:assert/strict";
import { isLocationLookupEnabled } from "./location-lookup";

const baseAccess = {
  isAdmin: false,
  isWarehouseManager: false,
  isWarehouseStaffOnly: false,
};

assert.equal(
  isLocationLookupEnabled(baseAccess),
  false,
  "普通账号不应启用货位查询能力",
);
assert.equal(
  isLocationLookupEnabled({ ...baseAccess, isAdmin: true }),
  true,
  "管理员应启用货位查询能力",
);
assert.equal(
  isLocationLookupEnabled({ ...baseAccess, isWarehouseManager: true }),
  true,
  "仓库经理应启用货位查询能力",
);
assert.equal(
  isLocationLookupEnabled({ ...baseAccess, isWarehouseStaffOnly: true }),
  true,
  "纯仓库员工应启用货位查询能力",
);

console.log("location-lookup.test.ts: ok");
