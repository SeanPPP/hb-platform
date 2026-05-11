import type { AccessControl, CurrentUser } from "@/modules/auth/types";

function createEmptyAccess(): AccessControl {
  const alwaysFalse = () => false;

  return {
    isAdmin: false,
    isManager: false,
    isUser: false,
    isWarehouseStaff: false,
    isWarehouseManager: false,
    isStoreStaff: false,
    isStoreManager: false,
    isStoreLevelManager: false,
    onlyOrder: false,
    canReadOrder: false,
    canWriteOrder: false,
    canDeleteOrder: false,
    canReadProduct: false,
    canWriteProduct: false,
    canDeleteProduct: false,
    canReadUser: false,
    canWriteUser: false,
    canDeleteUser: false,
    canReadRole: false,
    canWriteRole: false,
    canDeleteRole: false,
    canReadStore: false,
    canWriteStore: false,
    canDeleteStore: false,
    canManageWarehouse: false,
    canManageStore: false,
    canViewReports: false,
    canExportData: false,
    canModifyPrice: false,
    canDeletePrice: false,
    hasPermission: alwaysFalse,
    hasRole: alwaysFalse,
    onlyRole: alwaysFalse,
    hasAnyRole: alwaysFalse,
    hasAllRoles: alwaysFalse,
    managedStoreCodes: () => null,
  };
}

export function buildAccess(currentUser?: CurrentUser | null): AccessControl {
  if (!currentUser) {
    return createEmptyAccess();
  }

  const hasPermission = (permission: string) =>
    currentUser.permissions?.some(
      (item) => item.toLowerCase() === permission.toLowerCase()
    ) ?? false;

  const hasRole = (role: string) =>
    currentUser.roleNames?.some(
      (item) => item.toLowerCase() === role.toLowerCase()
    ) ?? false;

  const onlyRole = (role: string) => {
    if (!currentUser.roleNames?.length) {
      return false;
    }
    return hasRole(role) && currentUser.roleNames.length === 1;
  };

  const hasAnyRole = (roles: string[]) => roles.some((role) => hasRole(role));
  const hasAllRoles = (roles: string[]) => roles.every((role) => hasRole(role));

  const isAdmin = hasRole("Admin") || hasRole("管理员");
  const isWarehouseManager =
    hasRole("WarehouseManager") || hasRole("仓库经理");
  const isStoreManager = hasRole("StoreManager") || hasRole("经理");
  const isManager = isStoreManager || isWarehouseManager;
  const isUser = hasRole("User") || hasRole("用户");
  const isWarehouseStaff =
    isAdmin || hasRole("WarehouseStaff") || hasRole("仓库员工") || hasRole("WarehouseManager");
  const isStoreStaff = hasRole("StoreStaff") || hasRole("店铺员工");
  const isStoreLevelManager = isStoreManager && !isAdmin && !isWarehouseManager;
  const onlyOrder = onlyRole("Order") || hasRole("订货员");

  const managedStoreCodes = () => {
    if (isAdmin || isWarehouseManager) {
      return null;
    }
    if (currentUser.stores?.length) {
      return currentUser.stores.map((item) => item.storeCode).filter(Boolean);
    }
    return null;
  };

  const canReadUser = isAdmin || hasPermission("Users.View");
  const canWriteUser =
    isAdmin || hasPermission("Users.Create") || hasPermission("Users.Edit");
  const canDeleteUser = isAdmin || hasPermission("Users.Delete");
  const canReadRole = isAdmin || hasPermission("Roles.View");
  const canWriteRole =
    isAdmin || hasPermission("Roles.Create") || hasPermission("Roles.Edit");
  const canDeleteRole = isAdmin || hasPermission("Roles.Delete");
  const canReadStore = isAdmin || hasPermission("Stores.View");
  const canWriteStore =
    isAdmin || hasPermission("Stores.Create") || hasPermission("Stores.Edit");
  const canDeleteStore = isAdmin || hasPermission("Stores.Delete");
  const canManageWarehouse = isAdmin || hasPermission("Warehouse.Manage");
  const canManageStore =
    isAdmin || hasPermission("Stores.Edit") || hasPermission("Warehouse.Manage");

  const canReadOrder = isAdmin || hasPermission("Orders.View");
  const canWriteOrder =
    isAdmin || hasPermission("Orders.Create") || hasPermission("Orders.Edit");
  const canDeleteOrder = isAdmin || hasPermission("Orders.Delete");
  const canReadProduct = isAdmin || hasPermission("Products.View");
  const canWriteProduct =
    isAdmin || hasPermission("Products.Create") || hasPermission("Products.Edit");
  const canDeleteProduct = isAdmin || hasPermission("Products.Delete");

  const canViewReports = isAdmin || hasPermission("Reports.View");
  const canExportData = isAdmin || hasPermission("Reports.Export");
  const canModifyPrice = isAdmin || hasPermission("Prices.Modify");
  const canDeletePrice = isAdmin || hasPermission("Prices.Delete");

  return {
    isAdmin,
    isManager,
    isUser,
    isWarehouseStaff,
    isWarehouseManager,
    isStoreStaff,
    isStoreManager,
    isStoreLevelManager,
    onlyOrder,
    canReadOrder,
    canWriteOrder,
    canDeleteOrder,
    canReadProduct,
    canWriteProduct,
    canDeleteProduct,
    canReadUser,
    canWriteUser,
    canDeleteUser,
    canReadRole,
    canWriteRole,
    canDeleteRole,
    canReadStore,
    canWriteStore,
    canDeleteStore,
    canManageWarehouse,
    canManageStore,
    canViewReports,
    canExportData,
    canModifyPrice,
    canDeletePrice,
    hasPermission,
    hasRole,
    onlyRole,
    hasAnyRole,
    hasAllRoles,
    managedStoreCodes,
  };
}
