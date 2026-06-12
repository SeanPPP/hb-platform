import { strict as assert } from "node:assert";
import { canMaintainWarehouseLocations } from "./location-permissions";
import type { AccessControl } from "@/modules/auth/types";

function accessWithWarehouseStaffFlag(isWarehouseStaff: boolean): AccessControl {
  return {
    isWarehouseStaff,
  } as AccessControl;
}

assert.equal(
  canMaintainWarehouseLocations(accessWithWarehouseStaffFlag(true)),
  true,
  "Admin/WarehouseManager/WarehouseStaff 归一到 isWarehouseStaff 后应允许维护货位"
);

assert.equal(
  canMaintainWarehouseLocations(accessWithWarehouseStaffFlag(false)),
  false,
  "设备授权没有账号角色时不允许新增、编辑、删除货位"
);

console.log("location-permissions.test.ts: ok");
