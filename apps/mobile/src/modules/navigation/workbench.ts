export interface WorkbenchNavigationItem {
  routeName: string;
  labelKey: string;
  icon: string;
}

export interface WorkbenchNavigationSection {
  key:
    | "sales-product"
    | "warehouse-purchase"
    | "operations-reports"
    | "people-management";
  titleKey: string;
  items: WorkbenchNavigationItem[];
}

const WORKBENCH_SECTIONS: WorkbenchNavigationSection[] = [
  {
    key: "sales-product",
    titleKey: "groups.salesProduct",
    items: [
      { routeName: "product-query", labelKey: "routes.productQuery", icon: "barcode-scan" },
      { routeName: "home", labelKey: "routes.storeOrdering", icon: "storefront-outline" },
      { routeName: "cart", labelKey: "routes.cart", icon: "cart-outline" },
      { routeName: "orders", labelKey: "routes.orders", icon: "clipboard-list-outline" },
      { routeName: "installment-orders", labelKey: "routes.installmentOrders", icon: "cash-clock" },
      { routeName: "store-vouchers", labelKey: "routes.storeVouchers", icon: "ticket-confirmation-outline" },
      { routeName: "seasonal-cards", labelKey: "routes.seasonalCards", icon: "cards-outline" },
    ],
  },
  {
    key: "warehouse-purchase",
    titleKey: "groups.warehousePurchase",
    items: [
      { routeName: "warehouse", labelKey: "routes.warehouse", icon: "warehouse" },
      { routeName: "domestic-purchase", labelKey: "routes.domesticPurchase", icon: "shopping-outline" },
      { routeName: "local-supplier-invoices", labelKey: "routes.localSupplierInvoices", icon: "receipt-text-outline" },
    ],
  },
  {
    key: "operations-reports",
    titleKey: "groups.operationsReports",
    items: [
      { routeName: "advertisements", labelKey: "routes.advertisements", icon: "bullhorn-outline" },
      { routeName: "promotions", labelKey: "routes.promotions", icon: "sale-outline" },
      { routeName: "reports", labelKey: "routes.reports", icon: "chart-line" },
    ],
  },
  {
    key: "people-management",
    titleKey: "groups.peopleManagement",
    items: [
      { routeName: "attendance-personal", labelKey: "routes.attendancePersonal", icon: "clock-check-outline" },
      { routeName: "attendance-management", labelKey: "routes.attendanceManagement", icon: "calendar-edit" },
      { routeName: "users", labelKey: "routes.users", icon: "account-group-outline" },
      { routeName: "employee-profile", labelKey: "routes.employeeProfile", icon: "card-account-details-outline" },
      { routeName: "employee-profile-review", labelKey: "routes.employeeProfileReview", icon: "account-check-outline" },
      { routeName: "device-management", labelKey: "routes.deviceManagement", icon: "cellphone-cog" },
    ],
  },
];

export function buildWorkbenchSections(
  visibleRouteNames: Iterable<string>
): WorkbenchNavigationSection[] {
  const visibleRoutes = new Set(visibleRouteNames);

  return WORKBENCH_SECTIONS.map((section) => ({
    ...section,
    items: section.items.filter((item) => visibleRoutes.has(item.routeName)),
  })).filter((section) => section.items.length > 0);
}
