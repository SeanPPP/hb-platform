export interface LocationLookupAccess {
  isAdmin: boolean;
  isWarehouseManager: boolean;
  isWarehouseStaffOnly: boolean;
}

export function isLocationLookupEnabled(access: LocationLookupAccess) {
  // 仅复用现有 access 能力，不扩展全局角色语义，也不参与服务端请求参数。
  return access.isAdmin || access.isWarehouseManager || access.isWarehouseStaffOnly;
}
