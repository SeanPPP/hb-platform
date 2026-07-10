import { shouldLoadAllStoresForWarehouseCart } from "./warehouse-cart-access";

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

assertEqual(
  shouldLoadAllStoresForWarehouseCart({
    isWarehouseStaffOnly: true,
    canCreateOrder: true,
  }),
  true,
  "pure WarehouseStaff with Orders.Create loads all stores for dedicated cart"
);

assertEqual(
  shouldLoadAllStoresForWarehouseCart({
    isWarehouseStaffOnly: true,
    canCreateOrder: false,
  }),
  false,
  "pure WarehouseStaff without Orders.Create keeps normal scoped store loading"
);

assertEqual(
  shouldLoadAllStoresForWarehouseCart({
    isWarehouseStaffOnly: false,
    canCreateOrder: true,
  }),
  false,
  "admins and managers keep normal store loading even when they can create orders"
);
