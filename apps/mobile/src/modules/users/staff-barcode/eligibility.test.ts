import assert from "node:assert/strict";
import { test } from "node:test";
import { canManageStaffBarcode } from "./eligibility";

const base = { authenticated: true, deviceOnly: false, canEditUsers: true, canManagePosStore: true,
  actorGuid: "manager-a", actorRoles: ["StoreManager"], targetGuid: "staff-b", targetStatus: 1, targetRoles: ["StoreStaff"] };

test("员工码入口与后端角色和门店边界一致", () => {
  assert.equal(canManageStaffBarcode(base), true);
  for (const patch of [
    { authenticated: false }, { deviceOnly: true }, { canEditUsers: false }, { canManagePosStore: false },
    { targetStatus: 0 }, { targetGuid: "MANAGER-A" }, { targetRoles: ["User"] },
    { targetRoles: ["StoreStaff", "StoreManager"] }, { targetRoles: ["Employee", "WarehouseAdmin"] },
    { actorRoles: ["WarehouseManager"] }, { actorGuid: "" },
  ]) assert.equal(canManageStaffBarcode({ ...base, ...patch }), false, JSON.stringify(patch));
  assert.equal(canManageStaffBarcode({ ...base, actorRoles: ["管理员"], targetRoles: ["Employee", "StoreManager"] }), true);
  assert.equal(canManageStaffBarcode({ ...base, actorRoles: ["Admin"], targetRoles: ["Admin"] }), false);
  assert.equal(canManageStaffBarcode({ ...base, targetRoles: ["店铺员工"] }), true);
});
