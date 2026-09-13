import type {
  AccessPermission,
  AccessPermissionCategory,
  AccessStoreOption,
  AssignUserAccessRolesInput,
  AssignUserAccessStoresInput,
  AssignUserDirectPermissionsInput,
  UserAccessPermissionAccess,
  UserAccessPermissionState,
  UserAccessRole,
  UserAccessStore,
} from "@/modules/users/access-management-types";
import type {
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
  UpdateStoreUserPosTerminalPermissionsPayload,
} from "@/modules/users/types";

export interface IdentityPagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface IdentityUserQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  roleGuid?: string;
  storeGuid?: string;
  isActive?: boolean;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
}

export interface IdentityUserStore {
  storeGUID: string;
  storeName: string;
  storeCode: string;
  isActive?: boolean;
  isPrimary: boolean;
  assignedAt?: string;
}

export interface IdentityUser {
  userGUID: string;
  username: string;
  email: string;
  fullName?: string;
  phone?: string;
  lastLoginAt?: string;
  lastLoginIp?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  currentStore?: string;
  roleNames: string[];
  storeNames: string[];
  roles: IdentityRole[];
  stores: IdentityUserStore[];
  permissions: string[];
  exactPermissions: string[];
}

export type IdentityUserDetail = IdentityUser;

export interface IdentityCreateUserInput {
  username: string;
  email: string;
  password: string;
  passwordFormat?: "raw" | "clientSha256";
  fullName?: string;
  isActive?: boolean;
  roleGuids?: string[];
  storeGuids?: string[];
}

export interface IdentityUpdateUserInput {
  username: string;
  email: string;
  fullName?: string;
  isActive?: boolean;
}

export interface IdentityUpdateUserPasswordInput {
  newPassword: string;
  passwordFormat?: "raw" | "clientSha256";
  forcePasswordChange?: boolean;
}

export interface IdentityUserLoginRecordQuery {
  page?: number;
  pageSize?: number;
}

export type IdentityUserLoginStatus = "active" | "revoked" | "expired" | string;

export interface IdentityUserLoginRecord {
  sessionId: string;
  loginAt: string;
  ipAddress?: string;
  userAgent?: string;
  expiresAt: string;
  isRevoked: boolean;
  isExpired: boolean;
  status: IdentityUserLoginStatus;
}

export interface IdentityRoleQuery {
  page?: number;
  pageSize?: number;
  searchKeyword?: string;
  isActive?: boolean;
  sortBy?: string;
  sortDirection?: "asc" | "desc";
}

export interface IdentityRole {
  roleGUID: string;
  roleName: string;
  description?: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  userCount: number;
}

export interface IdentityRoleUser {
  userGUID: string;
  username: string;
  email: string;
  fullName?: string;
  isActive: boolean;
  assignedAt: string;
}

export interface IdentityRoleDetail extends IdentityRole {
  users: IdentityRoleUser[];
  permissions: string[];
}

export interface IdentityCreateRoleInput {
  roleName: string;
  description?: string;
  isActive?: boolean;
  permissions?: string[];
}

export interface IdentityUpdateRoleInput {
  roleName: string;
  description?: string;
  isActive?: boolean;
}

export interface IdentityPermission {
  name: string;
  displayName: string;
  description?: string;
  category: string;
  isSystemPermission: boolean;
  createdAt?: string;
  createdBy?: string;
  updatedAt?: string;
  updatedBy?: string;
}

export interface IdentityPermissionCategory {
  category: string;
  displayName: string;
  description?: string;
  permissions: IdentityPermission[];
}

export interface IdentityPermissionAlias {
  canonicalCode: string;
  aliasCodes: string[];
}

export interface IdentityRolePermissionTemplate {
  roleName: string;
  permissionCodes: string[];
}

export interface IdentityPermissionCatalog {
  categories: IdentityPermissionCategory[];
  permissionAliases: IdentityPermissionAlias[];
  roleTemplates: IdentityRolePermissionTemplate[];
  superAdminRoleNames: string[];
}

export interface IdentityRolePermissionState {
  roleGuid: string;
  roleName: string;
  isSuperAdmin: boolean;
  implicitAllPermissions: boolean;
  explicitPermissionCodes: string[];
  effectivePermissionCodes: string[];
}

export interface IdentityAdminErrorMeta {
  message: string;
  status?: number;
  code?: string;
}

// 复用既有用户权限与 POS 权限契约，避免新全局管理页形成第二套写入语义。
export type {
  AccessPermission,
  AccessPermissionCategory,
  AccessStoreOption,
  AssignUserAccessRolesInput,
  AssignUserAccessStoresInput,
  AssignUserDirectPermissionsInput,
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
  UpdateStoreUserPosTerminalPermissionsPayload,
  UserAccessPermissionAccess,
  UserAccessPermissionState,
  UserAccessRole,
  UserAccessStore,
};
