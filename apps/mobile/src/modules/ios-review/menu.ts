import type { AppNavigationMenuItem } from "../navigation/types";

// iOS 商店审核会话完全离线，真实员工敏感资料审核入口必须显式排除。
export const IOS_REVIEW_EXCLUDED_ROUTE_NAMES = ["employee-profile-review"] as const;

export const IOS_REVIEW_MENU_ITEMS: ReadonlyArray<AppNavigationMenuItem> = [
  { routeName: "home", titleKey: "tabs.home", icon: "home", permission: "Orders.Create", order: 10 },
  { routeName: "orders", titleKey: "tabs.orders", icon: "clipboard-list", permission: "Orders.View", order: 20 },
  { routeName: "cart", titleKey: "tabs.cart", icon: "cart-outline", permission: "Orders.Create", order: 30 },
  { routeName: "warehouse", titleKey: "tabs.warehouse", icon: "warehouse", permission: "Warehouse.ManageProducts", order: 40 },
  { routeName: "domestic-purchase", titleKey: "tabs.domesticPurchase", icon: "shopping-outline", permission: "DomesticPurchase.ManageProducts", order: 45 },
  { routeName: "local-supplier-invoices", titleKey: "tabs.localSupplierInvoices", icon: "receipt-text-outline", permission: "LocalPurchase.View", order: 46 },
  { routeName: "advertisements", titleKey: "tabs.advertisements", icon: "image-multiple", permission: "Advertisements.View", order: 47 },
  { routeName: "promotions", titleKey: "tabs.promotions", icon: "ticket-percent-outline", permission: "Promotions.View", order: 48 },
  { routeName: "product-query", titleKey: "tabs.productQuery", icon: "barcode-scan", permission: "StoreProducts.View", order: 50 },
  { routeName: "installment-orders", titleKey: "tabs.installmentOrders", icon: "cash-clock", permission: "InstallmentOrders.View", order: 51 },
  { routeName: "store-vouchers", titleKey: "tabs.storeVouchers", icon: "ticket-percent-outline", permission: "StoreVouchers.View", order: 52 },
  { routeName: "attendance-personal", titleKey: "tabs.attendancePersonal", icon: "calendar-clock", permission: "Attendance.Schedule.ViewSelf", order: 55 },
  { routeName: "attendance-management", titleKey: "tabs.attendanceManagement", icon: "calendar-clock", permission: "Attendance.Schedule.ViewStore", order: 56 },
  { routeName: "seasonal-cards", titleKey: "tabs.seasonalCards", icon: "gift-outline", permission: "SeasonalCards.Remaining.ViewManagedStore", order: 56 },
  { routeName: "users", titleKey: "tabs.users", icon: "account-group-outline", permission: "Users.View", order: 57 },
  { routeName: "employee-profile", titleKey: "tabs.employeeProfile", icon: "card-account-details-outline", permission: "EmployeeProfiles.View", order: 58 },
  { routeName: "device-management", titleKey: "tabs.deviceManagement", icon: "cellphone-cog", permission: "DeviceRegistration.View", order: 59 },
  { routeName: "reports", titleKey: "tabs.reports", icon: "chart-box-outline", permission: "Reports.ProductMovement.View", order: 59 },
  { routeName: "settings", titleKey: "tabs.settings", icon: "account-circle-outline", order: 60 },
];

export const IOS_REVIEW_ROUTE_NAMES = IOS_REVIEW_MENU_ITEMS.map(
  (item) => item.routeName
);
