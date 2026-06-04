import type { AccessControl } from "@/modules/auth/types";

export function canMaintainWarehouseLocations(access: AccessControl) {
  // access.isWarehouseStaff 已归一 Admin、WarehouseManager、WarehouseStaff；设备授权不会命中。
  return access.isWarehouseStaff;
}
