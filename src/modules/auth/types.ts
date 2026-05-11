export interface LoginRequest {
  username: string;
  password: string;
}

export interface TokenResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiry: string;
  refreshTokenExpiry: string;
  success: boolean;
  message: string;
}

export interface RefreshTokenRequest {
  accessToken: string;
  refreshToken: string;
}

export interface UserStoreDto {
  storeCode: string;
  storeName: string;
}

export interface CurrentUser {
  userGUID: string;
  username: string;
  email: string;
  fullName?: string;
  phone?: string;
  permissions: string[];
  roleNames: string[];
  storeNames: string[];
  stores?: UserStoreDto[];
}

export interface AccessControl {
  isAdmin: boolean;
  isManager: boolean;
  isUser: boolean;
  isWarehouseStaff: boolean;
  isWarehouseManager: boolean;
  isStoreStaff: boolean;
  isStoreManager: boolean;
  isStoreLevelManager: boolean;
  onlyOrder: boolean;
  canReadOrder: boolean;
  canWriteOrder: boolean;
  canDeleteOrder: boolean;
  canReadProduct: boolean;
  canWriteProduct: boolean;
  canDeleteProduct: boolean;
  canReadUser: boolean;
  canWriteUser: boolean;
  canDeleteUser: boolean;
  canReadRole: boolean;
  canWriteRole: boolean;
  canDeleteRole: boolean;
  canReadStore: boolean;
  canWriteStore: boolean;
  canDeleteStore: boolean;
  canManageWarehouse: boolean;
  canManageStore: boolean;
  canViewReports: boolean;
  canExportData: boolean;
  canModifyPrice: boolean;
  canDeletePrice: boolean;
  hasPermission: (permission: string) => boolean;
  hasRole: (role: string) => boolean;
  onlyRole: (role: string) => boolean;
  hasAnyRole: (roles: string[]) => boolean;
  hasAllRoles: (roles: string[]) => boolean;
  managedStoreCodes: () => string[] | null;
}
