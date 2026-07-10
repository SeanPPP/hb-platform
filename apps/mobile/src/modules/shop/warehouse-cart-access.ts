import type { AccessControl } from "../auth/types";

export function shouldLoadAllStoresForWarehouseCart(
  access: Pick<AccessControl, "canCreateOrder" | "isWarehouseStaffOnly">
) {
  return access.isWarehouseStaffOnly && access.canCreateOrder;
}
